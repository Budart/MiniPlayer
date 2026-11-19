using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Rectangle = System.Windows.Shapes.Rectangle;
using Mgr = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using static MiniPlayer.Native;
using static MiniPlayer.AudioMixer;
using static MiniPlayer.CoverArt;
using static MiniPlayer.Icons;

namespace MiniPlayer
{
    // Posição na barra/tela, camada (acima/atrás), tela cheia e troca de layout horizontal ↔ vertical
    public partial class MainWindow
    {
        ContextMenu openMenu;
        // gancho global de mouse: fecha o menu ao clicar em qualquer lugar fora dele
        LowLevelProc mouseProc; IntPtr mouseHook;
        void HookMouse()
        {
            if (mouseHook != IntPtr.Zero) return;
            mouseProc = (code, wp, lp) =>
            {
                int msg = (int)wp;
                if (code >= 0 && (msg == 0x201 || msg == 0x204 || msg == 0x207))
                {
                    int x = Marshal.ReadInt32(lp), y = Marshal.ReadInt32(lp, 4);
                    // clique numa janela de outro programa (fora do menu e dos submenus, que são janelas deste app) fecha o menu
                    var hw = WindowFromPoint(new POINT { X = x, Y = y });
                    GetWindowThreadProcessId(hw, out uint pid);
                    if (pid != (uint)Environment.ProcessId)
                        Dispatcher.InvokeAsync(() => { if (openMenu != null && openMenu.IsOpen) openMenu.IsOpen = false; });
                }
                return CallNextHookEx(mouseHook, code, wp, lp);
            };
            mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0);
        }
        void UnhookMouse() { if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; } }
        WinEventProc hookProc; bool menuOpen, fullscreen;

        // O player fica por cima da barra de tarefas. Se o mouse vem de um ícone da barra para o player,
        // a barra não percebe que o mouse saiu e deixa o realce ("brilho") do ícone aceso por baixo do player,
        // visível através do fundo transparente. Aqui avisamos a barra (e suas partes internas) que o mouse saiu.
        static readonly EnumProc leaveChild = (h, p) => { PostMessage(h, WM_MOUSELEAVE, IntPtr.Zero, IntPtr.Zero); return true; };
        DateTime lastUnhot;
        void ClearTaskbarHover()
        {
            if ((DateTime.Now - lastUnhot).TotalMilliseconds < 150) return;
            lastUnhot = DateTime.Now;
            try
            {
                PostMessage(bar, WM_MOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
                EnumChildWindows(bar, leaveChild, IntPtr.Zero);
            }
            catch { }
        }

        void Setup()
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); // toolwindow + noactivate
            Place();
            // na abertura (e ao ligar o PC a barra pode terminar de se ajustar depois): refaz layout e posição algumas vezes
            foreach (var ms in new[] { 300, 1500, 4000, 10000 })
            {
                var once = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                once.Tick += (_, __) => { once.Stop(); Relayout(); };
                once.Start();
            }
            // reage na hora quando outra janela (ex: a barra) vem pra frente
            hookProc = (a, ev, hw, o, c, t2, ms) => pending = true;
            var h1 = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, hookProc, 0, 0, 0);       // foco mudou
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            int n = 0;
            t.Tick += (_, __) =>
            {
                // fecha o menu ao clicar fora dele
                if (!pending && ++n % 3 != 0) return;
                if (pending) RequestScan();
                pending = false; CheckFullscreen(); if (!dragging) Place();
            };
            t.Start();
            // espaço livre da barra: relido a cada 5 s em segundo plano (e logo após trocas de janela, que podem abrir/fechar ícones)
            scanTimer = new System.Threading.Timer(_ => Scan(), null, 0, 5000);
            Closed += (_, __) => { t.Stop(); UnhookWinEvent(h1); scanTimer.Dispose(); };
        }

        void Place()
        {
            var me = new WindowInteropHelper(this).Handle;
            if (Settings.Floating)
            {
                ResetCompact();
                SetVertical(Settings.FloatVertical, 56);
                // solto na horizontal: altura pelo conteúdo (título + artista + progresso) com folga, nunca cortando o texto
                if (!Settings.FloatVertical)
                {
                    if (SizeToContent != SizeToContent.WidthAndHeight) SizeToContent = SizeToContent.WidthAndHeight;
                    var pad = new Thickness(6, 5, 6, 5); if (Root.Padding != pad) Root.Padding = pad;
                }
                else { var vpad = new Thickness(4, 0, 4, 0); if (Root.Padding != vpad) Root.Padding = vpad; }
                if (Math.Abs(Left - Settings.FloatX) > 1 || Math.Abs(Top - Settings.FloatY) > 1) { Left = Settings.FloatX; Top = Settings.FloatY; }
                if (!dragging) KeepInsideMonitor();
            }
            else if (GetWindowRect(bar, out var r) && GetWindowRect(me, out var w))
            {
                var src = PresentationSource.FromVisual(this);
                double s = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
                // barra na lateral (esquerda/direita): layout vertical, colado no topo ou acima da bandeja
                if ((r.B - r.T) > (r.R - r.L))
                {
                    SetVertical(true, (r.R - r.L) / s);
                    ResetCompact();
                    UpdateLayout();
                    GetWindowRect(me, out w);
                    int vh = w.B - w.T, vy;
                    if (App.Centered) vy = r.T + (int)(8 * s);
                    else
                    {
                        var tr = isPrimary ? FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null) : IntPtr.Zero;
                        int top = tr != IntPtr.Zero && GetWindowRect(tr, out var tt) ? tt.T : r.B - (int)(160 * s);
                        vy = top - vh - (int)(8 * s);
                    }
                    if (w.L != r.L || w.T != vy) SetWindowPos(me, IntPtr.Zero, r.L, vy, 0, 0, 0x0001 | 0x0004 | 0x0010);
                    if (!menuOpen && !fullscreen) ApplyZ();
                    return;
                }
                SetVertical(false, 0);
                if (SizeToContent != SizeToContent.Width) SizeToContent = SizeToContent.Width; // fixo: altura = da barra
                var barPad = new Thickness(4, 0, 4, 0); if (Root.Padding != barPad) Root.Padding = barPad;
                var hh = (r.B - r.T) / s; if (Math.Abs(Height - hh) > 0.5) Height = hh;
                // encaixa no vão livre da barra (sem cobrir ícones); se não couber, compacta o player por etapas
                int pad = (int)(8 * s);
                var gap = barGap;
                if (gap == null) ResetCompact();
                else if (FitTo((gap.R - gap.L - 2 * pad) / s)) { UpdateLayout(); GetWindowRect(me, out w); }
                int wPx = w.R - w.L, hPx = w.B - w.T, x;
                if (App.Centered) x = (gap?.L ?? r.L) + pad;                    // à esquerda (depois do clima/Widgets)
                else
                {
                    int anchor = gap?.R ?? 0;
                    if (gap == null)
                    {
                        var tray = isPrimary ? FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null) : IntPtr.Zero;  // bandeja do sistema
                        anchor = tray != IntPtr.Zero && GetWindowRect(tray, out var t) ? t.L : r.R - (int)(120 * s);
                    }
                    x = anchor - wPx - pad;                                     // à direita (antes da bandeja/relógio)
                }
                int y = r.T + (r.B - r.T - hPx) / 2;
                if (w.L != x || w.T != y) SetWindowPos(me, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
            }
            if (!menuOpen && !fullscreen) ApplyZ();
        }

        // No modo "atrás das janelas", se o player estiver sobre a barra de tarefas ele fica à frente dela
        // (janelas comuns nunca ocupam a área da barra, então ele continua atrás delas)
        bool OverTaskbar()
        {
            var me = new WindowInteropHelper(this).Handle;
            if (!GetWindowRect(me, out var r)) return false;
            var mi = new MONITORINFO { cb = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(me, 2), ref mi)) return false;
            return r.L < mi.work.L || r.T < mi.work.T || r.R > mi.work.R || r.B > mi.work.B; // sai da área de trabalho = está sobre a barra
        }

        // Win+D, "mostrar área de trabalho" ou clique no desktop: o foco vai para a área de trabalho
        static bool DesktopInFront()
        {
            var fg = GetForegroundWindow(); if (fg == IntPtr.Zero) return false;
            var c = ClassName(fg);
            return c == "WorkerW" || c == "Progman";
        }

        // Camada da janela: acima de tudo (padrão) ou, no modo solto, opcionalmente atrás de todas as janelas
        static int tipsOpen;
        static MainWindow()
        {
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ToolTip), System.Windows.Controls.ToolTip.OpenedEvent, new RoutedEventHandler((_, __) => tipsOpen++));
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ToolTip), System.Windows.Controls.ToolTip.ClosedEvent, new RoutedEventHandler((_, __) => tipsOpen = Math.Max(0, tipsOpen - 1)));
        }
        void ApplyZ()
        {
            if (tipsOpen > 0) return; // balão de informação aberto: fica por cima do player até fechar
            bool behind = Settings.Floating && Settings.FloatBehind && !OverTaskbar() && !DesktopInFront();
            if (Topmost == behind) Topmost = !behind;
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(behind ? 1 : -1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // Solto: o player fica sempre 100% dentro de UM monitor (o que contém o centro dele); nunca entre dois nem fora da tela
        void KeepInsideMonitor()
        {
            var me = new WindowInteropHelper(this).Handle;
            UpdateLayout();
            if (!GetWindowRect(me, out var w)) return;
            var mi = new MONITORINFO { cb = Marshal.SizeOf<MONITORINFO>() };
            var center = new POINT { X = (w.L + w.R) / 2, Y = (w.T + w.B) / 2 };
            if (!GetMonitorInfo(MonitorFromPoint(center, 2 /*mais próximo*/), ref mi)) return;
            var m = mi.rc;
            int x = w.L, y = w.T, ww = w.R - w.L, hh = w.B - w.T;
            x = Math.Max(m.L, Math.Min(x, m.R - ww));
            y = Math.Max(m.T, Math.Min(y, m.B - hh));
            if (x == w.L && y == w.T) return;
            SetWindowPos(me, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
            // posição salva acompanha o ajuste
            var src = PresentationSource.FromVisual(this);
            double s = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            Settings.FloatX = Left = x / s; Settings.FloatY = Top = y / s;
            Settings.Save();
        }

        // Recalcula tudo do zero: mesma coisa que soltar e fixar de novo
        void Relayout()
        {
            if (dragging || menuOpen) return;
            vertical = !vertical; // força SetVertical a reaplicar o layout atual
            SetVertical(!vertical, vWidth);
            UpdateLayout();
            Place();
            UpdateLayout();
            Place();
        }

        // Troca entre layout horizontal (barra embaixo/em cima) e vertical (barra na lateral)
        bool vertical; double vWidth;
        void SetVertical(bool on, double barWidth)
        {
            if (on == vertical && (!on || Math.Abs(barWidth - vWidth) < 0.5)) return;
            vertical = on; vWidth = barWidth;
            void At(UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); }
            var parts = new UIElement[] { Controls, Spectrum, ArtBox, InfoPanel, BatteryPanel, SysPanel };
            Layout.RowDefinitions.Clear();
            Layout.ColumnDefinitions.Clear();
            for (int i = 0; i < 5; i++) Layout.ColumnDefinitions.Add(new ColumnDefinition { Width = on && i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            if (on)
            {
                SizeToContent = SizeToContent.Height; Width = barWidth;
                for (int i = 0; i < 5; i++) Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                At(Controls, 0, 0); At(Spectrum, 0, 0); At(ArtBox, 1, 0); At(InfoPanel, 2, 0); At(BatteryPanel, 3, 0); At(SysPanel, 4, 0);
                SysPanel.Orientation = Orientation.Vertical; SysPanel.HorizontalAlignment = HorizontalAlignment.Center; SysPanel.Margin = new Thickness(0, 0, 0, 8);
                foreach (var e in parts) { Grid.SetColumnSpan(e, 1); Grid.SetRowSpan(e, 1); }
                // botões empilhados e "apertadinhos"
                Controls.Orientation = Orientation.Vertical;
                foreach (var btn in new[] { PrevBtn, PlayBtn, NextBtn, SwapBtn }) btn.Height = 20;
                Controls.HorizontalAlignment = ArtBox.HorizontalAlignment = BatteryPanel.HorizontalAlignment = HorizontalAlignment.Center;
                Controls.Margin = new Thickness(0, 6, 0, 0);
                ArtBox.Margin = new Thickness(0, 6, 0, 0);
                MoveProgress(true);
                BatteryPanel.Orientation = Orientation.Vertical; BatteryPanel.Margin = new Thickness(0, 4, 0, 8);
                // espectro com menos barras (uma sim, uma não)
                for (int i = 0; i < Spectrum.Children.Count; i++) Spectrum.Children[i].Visibility = i % 2 == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                // volta ao layout horizontal padrão (também ao soltar o player que estava numa barra vertical)
                SizeToContent = SizeToContent.Width;
                if (Settings.Floating) SizeToContent = SizeToContent.WidthAndHeight; // solto: altura pelo conteúdo
                At(Controls, 0, 0); At(Spectrum, 0, 0); At(ArtBox, 0, 1); At(InfoPanel, 0, 2); At(BatteryPanel, 0, 3); At(SysPanel, 0, 4);
                SysPanel.Orientation = Orientation.Horizontal; SysPanel.ClearValue(HorizontalAlignmentProperty); SysPanel.Margin = new Thickness(0, 0, 4, 0);
                foreach (var e in parts) { Grid.SetColumnSpan(e, 1); Grid.SetRowSpan(e, 1); }
                Controls.Orientation = Orientation.Horizontal;
                foreach (var btn in new[] { PrevBtn, PlayBtn, NextBtn, SwapBtn }) btn.ClearValue(HeightProperty);
                Controls.ClearValue(HorizontalAlignmentProperty); ArtBox.ClearValue(HorizontalAlignmentProperty); BatteryPanel.ClearValue(HorizontalAlignmentProperty);
                Controls.Margin = new Thickness(0); ArtBox.Margin = new Thickness(6, 0, 0, 0);
                MoveProgress(false);
                BatteryPanel.Orientation = Orientation.Horizontal; BatteryPanel.Margin = new Thickness(0, 0, 4, 0);
                foreach (UIElement r in Spectrum.Children) r.Visibility = Visibility.Visible;
            }
            ApplyVisibility();
            UpdateBatteries();
            RebuildSystem();
            if (dynBgGrad != null) dynBgGrad.EndPoint = vertical ? new Point(0, 1) : new Point(1, 0);
            if (IsLoaded) ApplyLook();
        }

        // ---- Espaço livre na barra (lido em segundo plano) e modo compacto ----
        System.Threading.Timer scanTimer;
        volatile TaskbarSpace.Gap barGap;
        int scanning; DateTime lastScan;
        void Scan()
        {
            if (Settings.Floating || Interlocked.Exchange(ref scanning, 1) == 1) return;
            try { lastScan = DateTime.Now; barGap = TaskbarSpace.Find(bar, App.Centered, isPrimary); }
            finally { scanning = 0; }
        }
        void RequestScan() { if ((DateTime.Now - lastScan).TotalSeconds >= 1) ThreadPool.QueueUserWorkItem(_ => Scan()); }

        // Etapas: 0 normal · 1–2 menos itens do monitor (3 → 1, o resto rola) · 3 sem título · 4 sem capa
        int compact; double fitWidth = -1; bool fitDirty = true;
        void InvalidateFit() => fitDirty = true;
        bool FitTo(double avail)
        {
            if (!fitDirty && Math.Abs(avail - fitWidth) < 1) return false;
            fitDirty = false; fitWidth = avail;
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            for (int level = 0; level <= 4; level++)
            {
                SetCompact(level);
                Root.Measure(inf);
                if (Root.DesiredSize.Width <= avail) break;
            }
            return true;
        }
        // fora da barra horizontal (solto, barra lateral, vão desconhecido): tudo visível; ao voltar, recalcula o encaixe
        void ResetCompact() { if (compact == 0) return; SetCompact(0); fitDirty = true; }
        void SetCompact(int level)
        {
            if (level == compact) return;
            compact = level;
            sysVisible = SysVisibleMax - Math.Min(level, 2);
            ShowSysWindow(false);
            ApplyVisibility();
        }

        // esconde atrás de jogos/vídeos em tela cheia
        void CheckFullscreen()
        {
            var fg = GetForegroundWindow();
            var me = new WindowInteropHelper(this).Handle;
            bool fs = false;
            if (fg != IntPtr.Zero && fg != me)
            {
                var cls = ClassName(fg);
                if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd" && cls != "Shell_SecondaryTrayWnd"
                    && GetWindowRect(fg, out var r))
                {
                    var mi = new MONITORINFO { cb = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi))
                        fs = r.L <= mi.rc.L && r.T <= mi.rc.T && r.R >= mi.rc.R && r.B >= mi.rc.B;
                }
            }
            // estado do próprio Windows: 2=ocupado/tela cheia, 3=jogo D3D, 4=apresentação
            if (!fs && SHQueryUserNotificationState(out var q) == 0 && (q == 2 || q == 3 || q == 4)) fs = true;
            if (fs && !Settings.Floating && MonitorFromWindow(fg, 2) != MonitorFromWindow(bar, 2)) fs = false;
            if (fs == fullscreen) return;
            fullscreen = fs;
            if (fs) { Topmost = false; SetWindowPos(me, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); SetWindowPos(me, fg, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } // atrás da janela cheia
            else ApplyZ();
            Visibility = fs ? Visibility.Hidden : Visibility.Visible;
        }

        void MoveProgress(bool toVertical)
        {
            (Progress.Parent as Panel)?.Children.Remove(Progress);
            if (toVertical)
            {
                Layout.Children.Add(Progress);
                Grid.SetRow(Progress, 2); Grid.SetColumn(Progress, 0);
                Progress.Width = 33; Progress.HorizontalAlignment = HorizontalAlignment.Center; Progress.Margin = new Thickness(0, 3, 0, 0);
            }
            else
            {
                InfoPanel.Children.Add(Progress);
                Progress.ClearValue(WidthProperty); Progress.HorizontalAlignment = HorizontalAlignment.Stretch; Progress.Margin = new Thickness(0, 2, 0, 0);
            }
        }
    }
}
