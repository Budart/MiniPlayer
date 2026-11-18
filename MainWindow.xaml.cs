using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Animation;
using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Mgr = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace MiniPlayer
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int o, int c, uint t, uint ms);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc p, uint pid, uint tid, uint fl);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int st);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int k);
        ContextMenu openMenu;
        // gancho global de mouse: fecha o menu ao clicar em qualquer lugar fora dele
        delegate IntPtr LowLevelProc(int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc p, IntPtr mod, uint tid);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
        LowLevelProc mouseProc; IntPtr mouseHook;
        struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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
        struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cb; public RECT rc, work; public uint f; }
        WinEventProc hookProc; bool menuOpen, fullscreen;

        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
        readonly IntPtr bar; readonly bool isPrimary;
        Mgr mgr; GlobalSystemMediaTransportControlsSession session;
        bool dragging, pending;
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
        delegate bool EnumChildProc(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc f, IntPtr p);

        // O player fica por cima da barra de tarefas. Se o mouse vem de um ícone da barra para o player,
        // a barra não percebe que o mouse saiu e deixa o realce ("brilho") do ícone aceso por baixo do player,
        // visível através do fundo transparente. Aqui avisamos a barra (e suas partes internas) que o mouse saiu.
        static readonly EnumChildProc leaveChild = (h, p) => { PostMessage(h, 0x02A3 /*WM_MOUSELEAVE*/, IntPtr.Zero, IntPtr.Zero); return true; };
        DateTime lastUnhot;
        void ClearTaskbarHover()
        {
            if ((DateTime.Now - lastUnhot).TotalMilliseconds < 150) return;
            lastUnhot = DateTime.Now;
            try
            {
                PostMessage(bar, 0x02A3, IntPtr.Zero, IntPtr.Zero);
                EnumChildWindows(bar, leaveChild, IntPtr.Zero);
            }
            catch { }
        }

        public MainWindow(IntPtr taskbar, bool primary)
        {
            bar = taskbar; isPrimary = primary;
            InitializeComponent();
            SetTitle(L.S("nothing"));
            // antes de exibir: verifica a orientação da barra e já monta o layout certo (vertical/horizontal)
            if (!App.Floating && GetWindowRect(bar, out var br) && (br.B - br.T) > (br.R - br.L))
            {
                double dpi = GetDpiForWindow(bar); double sc = dpi > 0 ? dpi / 96.0 : 1;
                SetVertical(true, (br.R - br.L) / sc);
                Left = br.L / sc; Top = br.T / sc + 8;
            }
            Loaded += async (_, __) => { Setup(); SetupSpectrum(); SetupExtras(); UpdateBatteries(); await InitMedia(); };
            App.BatteriesChanged += UpdateBatteries;
            App.LookChanged += ApplyLook;
            Closed += (_, __) => UnhookMouse(); // nunca deixa o gancho de mouse para trás
            Closed += (_, __) => App.LookChanged -= ApplyLook;
            Loaded += (_, __) => ApplyLook();
            Closed += (_, __) => App.BatteriesChanged -= UpdateBatteries;
            Root.MouseRightButtonUp += (_, __) => ShowMenu();
            MouseEnter += (_, __) => ClearTaskbarHover();
            MouseMove += (_, __) => ClearTaskbarHover();
            Root.MouseLeftButtonDown += (s, e) =>
            {
                if (!App.Floating || App.FloatLocked || e.OriginalSource is Button) return; // travado: não arrasta
                dragging = true; DragMove(); dragging = false; KeepInsideMonitor(); App.FloatX = Left; App.FloatY = Top; App.Save();
            };
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
                if (!pending && ++n % 3 != 0) return; pending = false; CheckFullscreen(); if (!dragging) Place();
            };
            t.Start();
            Closed += (_, __) => { t.Stop(); UnhookWinEvent(h1); };
        }

        void Place()
        {
            var me = new WindowInteropHelper(this).Handle;
            if (App.Floating)
            {
                SetVertical(App.FloatVertical, 56);
                if (Math.Abs(Left - App.FloatX) > 1 || Math.Abs(Top - App.FloatY) > 1) { Left = App.FloatX; Top = App.FloatY; }
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
                var hh = (r.B - r.T) / s; if (Math.Abs(Height - hh) > 0.5) Height = hh;
                int wPx = w.R - w.L, hPx = w.B - w.T, x;
                if (App.Centered) x = r.L + (int)(8 * s);                       // canto esquerdo da barra
                else
                {
                    var tray = isPrimary ? FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null) : IntPtr.Zero;  // bandeja do sistema
                    int anchor = tray != IntPtr.Zero && GetWindowRect(tray, out var t) ? t.L : RightItemsLeft(r, s);
                    x = anchor - wPx - (int)(8 * s);
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
            var sb = new System.Text.StringBuilder(32); GetClassName(fg, sb, 32);
            var c = sb.ToString();
            return c == "WorkerW" || c == "Progman";
        }

        // Camada da janela: acima de tudo (padrão) ou, no modo solto, opcionalmente atrás de todas as janelas
        void ApplyZ()
        {
            bool behind = App.Floating && App.FloatBehind && !OverTaskbar() && !DesktopInFront();
            if (Topmost == behind) Topmost = !behind;
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(behind ? 1 : -1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // Solto: o player fica sempre 100% dentro de UM monitor (o que contém o centro dele); nunca entre dois nem fora da tela
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
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
            App.FloatX = Left = x / s; App.FloatY = Top = y / s;
            App.Save();
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
            var parts = new UIElement[] { Controls, Spectrum, ArtBox, InfoPanel, BatteryPanel };
            Layout.RowDefinitions.Clear();
            Layout.ColumnDefinitions.Clear();
            for (int i = 0; i < 4; i++) Layout.ColumnDefinitions.Add(new ColumnDefinition { Width = on && i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            if (on)
            {
                SizeToContent = SizeToContent.Height; Width = barWidth;
                for (int i = 0; i < 4; i++) Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                At(Controls, 0, 0); At(Spectrum, 0, 0); At(ArtBox, 1, 0); At(InfoPanel, 2, 0); At(BatteryPanel, 3, 0);
                foreach (var e in parts) { Grid.SetColumnSpan(e, 1); Grid.SetRowSpan(e, 1); }
                // botões empilhados e "apertadinhos"
                Controls.Orientation = Orientation.Vertical;
                foreach (var btn in new[] { PrevBtn, PlayBtn, NextBtn, SwapBtn }) btn.Height = 20;
                Controls.HorizontalAlignment = ArtBox.HorizontalAlignment = BatteryPanel.HorizontalAlignment = HorizontalAlignment.Center;
                Controls.Margin = new Thickness(0, 6, 0, 0);
                ArtBox.Margin = new Thickness(0, 6, 0, 0);
                InfoPanel.Visibility = Visibility.Collapsed; // sem textos na vertical
                MoveProgress(true);
                BatteryPanel.Orientation = Orientation.Vertical; BatteryPanel.Margin = new Thickness(0, 4, 0, 8);
                // espectro com menos barras (uma sim, uma não)
                for (int i = 0; i < Spectrum.Children.Count; i++) Spectrum.Children[i].Visibility = i % 2 == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                // volta ao layout horizontal padrão (também ao soltar o player que estava numa barra vertical)
                SizeToContent = SizeToContent.Width;
                if (App.Floating) Height = 40;
                At(Controls, 0, 0); At(Spectrum, 0, 0); At(ArtBox, 0, 1); At(InfoPanel, 0, 2); At(BatteryPanel, 0, 3);
                foreach (var e in parts) { Grid.SetColumnSpan(e, 1); Grid.SetRowSpan(e, 1); }
                Controls.Orientation = Orientation.Horizontal;
                foreach (var btn in new[] { PrevBtn, PlayBtn, NextBtn, SwapBtn }) btn.ClearValue(HeightProperty);
                Controls.ClearValue(HorizontalAlignmentProperty); ArtBox.ClearValue(HorizontalAlignmentProperty); BatteryPanel.ClearValue(HorizontalAlignmentProperty);
                Controls.Margin = new Thickness(0); ArtBox.Margin = new Thickness(6, 0, 0, 0);
                InfoPanel.Visibility = App.HidePlayer ? Visibility.Collapsed : Visibility.Visible;
                MoveProgress(false);
                BatteryPanel.Orientation = Orientation.Horizontal; BatteryPanel.Margin = new Thickness(0, 0, 4, 0);
                foreach (UIElement r in Spectrum.Children) r.Visibility = Visibility.Visible;
            }
            UpdateBatteries();
            if (dynBgGrad != null) dynBgGrad.EndPoint = vertical ? new Point(0, 1) : new Point(1, 0);
        }

        // Monitor sem bandeja: acha (via acessibilidade) o item mais à esquerda do grupo da direita (relógio etc.)
        int cachedAnchor; DateTime anchorTime;
        int RightItemsLeft(RECT r, double s)
        {
            if ((DateTime.Now - anchorTime).TotalSeconds < 5 && cachedAnchor > 0) return cachedAnchor;
            anchorTime = DateTime.Now;
            int best = r.R - (int)(120 * s), mid = (r.L + r.R) / 2;
            try
            {
                var all = System.Windows.Automation.AutomationElement.FromHandle(bar)
                    .FindAll(System.Windows.Automation.TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                int found = int.MaxValue;
                foreach (System.Windows.Automation.AutomationElement el in all)
                {
                    var b = el.Current.BoundingRectangle;
                    if (b.IsEmpty || b.Width < 4 || b.Width > (r.R - r.L) / 3 || b.Left < mid) continue;
                    found = Math.Min(found, (int)b.Left);
                }
                if (found != int.MaxValue) best = found;
            }
            catch { }
            return cachedAnchor = best;
        }

        // esconde atrás de jogos/vídeos em tela cheia
        void CheckFullscreen()
        {
            var fg = GetForegroundWindow();
            var me = new WindowInteropHelper(this).Handle;
            bool fs = false;
            if (fg != IntPtr.Zero && fg != me)
            {
                var sb = new System.Text.StringBuilder(64); GetClassName(fg, sb, 64);
                var cls = sb.ToString();
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
            if (fs && !App.Floating && MonitorFromWindow(fg, 2) != MonitorFromWindow(bar, 2)) fs = false;
            if (fs == fullscreen) return;
            fullscreen = fs;
            if (fs) { Topmost = false; SetWindowPos(me, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); SetWindowPos(me, fg, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } // atrás da janela cheia
            else ApplyZ();
            Visibility = fs ? Visibility.Hidden : Visibility.Visible;
        }

        // Tema e "ocultar player"
        Brush fg = Brushes.White;
        static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        void ApplyLook()
        {
            var th = App.CurrentTheme;
            bool dyn = App.Theme == App.DynamicTheme;
            fg = dyn ? dynFg : B(th.Fg);
            Root.Background = dyn ? DynBg : B(th.Bg);
            // temas com fundo: leve recuo (3 px) para o fundo parecer "dentro" da barra, sem encostar nas bordas dela
            Root.Margin = App.Theme == 0 ? new Thickness(0) : new Thickness(3);
            TitleText.Foreground = fg; ArtistText.Foreground = dyn ? dynFg2 : B(th.Fg2); NoArt.Foreground = fg;
            PrevBtn.Foreground = PlayBtn.Foreground = NextBtn.Foreground = SwapBtn.Foreground = fg; SwapIcon.Stroke = fg;
            ProgFill.Background = ProgTrack.Background = fg;
            SizeScale.ScaleX = SizeScale.ScaleY = App.SizeScales[App.PlayerSize];
            foreach (Rectangle r in Spectrum.Children) r.Fill = th.Spectrum != null ? B(th.Spectrum) : accent;
            var vis = App.HidePlayer ? Visibility.Collapsed : Visibility.Visible;
            Controls.Visibility = Spectrum.Visibility = ArtBox.Visibility = InfoPanel.Visibility = vis;
            if (vertical) InfoPanel.Visibility = Visibility.Collapsed; // barra lateral: sem textos
            UpdateBatteries();
            if (dyn) ApplyDynamic(false);
        }

        // ---- Tema dinâmico: usa a cor da capa (já calculada para o espectro, numa miniatura de 16x16).
        // Custo: 3 pincéis reaproveitados e uma transição curta de cor na troca de faixa — sem memória extra relevante.
        // fundo em degradê: 2 cores da capa, no sentido do comprimento do player (horizontal ou vertical)
        readonly GradientStop dynStop1 = new(Color.FromArgb(0xE6, 0x20, 0x20, 0x20), 0), dynStop2 = new(Color.FromArgb(0xE6, 0x20, 0x20, 0x20), 1);
        LinearGradientBrush dynBgGrad;
        LinearGradientBrush DynBg
        {
            get
            {
                dynBgGrad ??= new LinearGradientBrush { GradientStops = { dynStop1, dynStop2 } };
                dynBgGrad.StartPoint = new Point(0, 0); dynBgGrad.EndPoint = vertical ? new Point(0, 1) : new Point(1, 0);
                return dynBgGrad;
            }
        }
        Color accent2 = Colors.White;
        readonly SolidColorBrush dynFg = new(Colors.White), dynFg2 = new(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF));
        Color lastDyn = Color.FromArgb(0, 0, 0, 1);
        void ApplyDynamic(bool animate)
        {
            var a = accent.Color;
            if (animate && a == lastDyn) return; // mesma capa (ex.: só pausou): nada a fazer
            lastDyn = a;
            bool none = a == Colors.White; // sem capa: cinza neutro
            // fundo: cor da capa escurecida (legível com texto claro), quase opaca
            Color Dark(Color c) => Color.FromArgb(0xE6, (byte)(c.R * 0.32), (byte)(c.G * 0.32), (byte)(c.B * 0.32));
            Color bg1 = none ? Color.FromArgb(0xE6, 0x22, 0x24, 0x28) : Dark(a);
            Color bg2 = none ? Color.FromArgb(0xE6, 0x16, 0x17, 0x1A) : Dark(accent2);
            // texto: branco levemente tingido; secundário: a própria cor da capa clareada
            Color f1 = none ? Colors.White : Mix(a, Colors.White, 0.85);
            Color f2 = none ? Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF) : WithA(Mix(a, Colors.White, 0.45), 0xDD);
            Anim(dynStop1, GradientStop.ColorProperty, bg1); Anim(dynStop2, GradientStop.ColorProperty, bg2);
            Anim(dynFg, SolidColorBrush.ColorProperty, f1); Anim(dynFg2, SolidColorBrush.ColorProperty, f2);
            void Anim(Animatable o, DependencyProperty p, Color c)
            {
                if (!animate) { o.BeginAnimation(p, null); o.SetValue(p, c); return; }
                o.BeginAnimation(p, new ColorAnimation(c, TimeSpan.FromMilliseconds(450)));
            }
        }
        static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        static Color WithA(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        // ======== Barra de progresso, volume pela rolagem, letreiro e dicas de bateria ========
        bool isPlaying;

        void SetupExtras()
        {
            // progresso: atualiza 2x por segundo, só se estiver visível
            var pt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            pt.Tick += (_, __) => UpdateProgress();
            pt.Start();
            Closed += (_, __) => pt.Stop();
            Progress.MouseLeftButtonDown += async (_, e) =>
            {
                e.Handled = true; // não inicia o arraste do player solto
                try
                {
                    var tl = session?.GetTimelineProperties(); if (tl == null) return;
                    double r = Math.Clamp(e.GetPosition(Progress).X / Math.Max(1, Progress.ActualWidth), 0, 1);
                    var target = tl.StartTime + TimeSpan.FromTicks((long)((tl.EndTime - tl.StartTime).Ticks * r));
                    await session.TryChangePlaybackPositionAsync(target.Ticks);
                }
                catch { }
            };
            // volume: rolagem do mouse em qualquer parte do player
            debounce.Tick += (_, __) => { debounce.Stop(); _ = Refresh(true); };
            Root.MouseWheel += (_, e) => { ChangeVolume(e.Delta > 0 ? 1 : -1); e.Handled = true; };
            if (isPrimary)
            {
                var at = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                at.Tick += (_, __) => AutoPickSource();
                at.Start();
                Closed += (_, __) => at.Stop();
            }
            volTimer.Tick += (_, __) => { volTimer.Stop(); VolBadge.Visibility = Visibility.Collapsed; };
            InfoPanel.MouseEnter += (_, __) => StartMarquee();
            InfoPanel.MouseLeave += (_, __) => ResetTitle();
        }

        void MoveProgress(bool toVertical)
        {
            (Progress.Parent as Panel)?.Children.Remove(Progress);
            if (toVertical)
            {
                Layout.Children.Add(Progress);
                Grid.SetRow(Progress, 2); Grid.SetColumn(Progress, 0);
                Progress.Width = 30; Progress.HorizontalAlignment = HorizontalAlignment.Center; Progress.Margin = new Thickness(0, 3, 0, 0);
            }
            else
            {
                InfoPanel.Children.Add(Progress);
                Progress.ClearValue(WidthProperty); Progress.HorizontalAlignment = HorizontalAlignment.Stretch; Progress.Margin = new Thickness(0, 2, 0, 0);
            }
        }

        void UpdateProgress()
        {
            try
            {
                if (session == null || App.HidePlayer || !IsVisible) { Progress.Visibility = Visibility.Collapsed; return; }
                var tl = session.GetTimelineProperties();
                double dur = (tl.EndTime - tl.StartTime).TotalSeconds;
                if (dur < 1) { Progress.Visibility = Visibility.Collapsed; return; } // sem duração (ex.: ao vivo)
                double pos = (tl.Position - tl.StartTime).TotalSeconds;
                if (isPlaying) pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds; // o Windows só informa a posição de tempos em tempos
                Progress.Visibility = Visibility.Visible;
                ProgFill.Width = Math.Max(0, Progress.ActualWidth * Math.Clamp(pos / dur, 0, 1));
            }
            catch { Progress.Visibility = Visibility.Collapsed; }
        }

        // Volume do app que está tocando (ou geral, se o app não for encontrado no mixer)
        static readonly MMDeviceEnumerator devices = new();
        readonly DispatcherTimer volTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
        void ChangeVolume(int dir)
        {
            try
            {
                using var dev = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                string app = SourceName(session).ToLowerInvariant();
                float? v = null;
                if (app.Length > 1)
                {
                    var list = dev.AudioSessionManager.Sessions;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var sc = list[i];
                        string pn;
                        try { pn = System.Diagnostics.Process.GetProcessById((int)sc.GetProcessID).ProcessName.ToLowerInvariant(); } catch { continue; }
                        if (pn.Length < 2 || !(pn.Contains(app) || app.Contains(pn))) continue;
                        var nv = Math.Clamp(sc.SimpleAudioVolume.Volume + dir * 0.02f, 0f, 1f);
                        sc.SimpleAudioVolume.Volume = nv; v = nv;
                    }
                }
                if (v == null)
                {
                    var ev = dev.AudioEndpointVolume;
                    v = Math.Clamp(ev.MasterVolumeLevelScalar + dir * 0.02f, 0f, 1f);
                    ev.MasterVolumeLevelScalar = v.Value;
                }
                VolText.Text = ((int)Math.Round(v.Value * 100)).ToString();
                VolBadge.Visibility = Visibility.Visible;
                volTimer.Stop(); volTimer.Start();
            }
            catch { }
        }

        // Título: largura fixa com "…"; ao passar o mouse, se não couber, rola em loop infinito para a esquerda
        const double InfoW = 150;
        string curTitle = "";
        readonly TranslateTransform titleShift = new();
        void SetTitle(string t) { if (t == curTitle && TitleText.Text.Length > 0) return; curTitle = t ?? ""; ResetTitle(); if (InfoPanel.IsMouseOver) StartMarquee(); }
        void ResetTitle()
        {
            titleShift.BeginAnimation(TranslateTransform.XProperty, null); titleShift.X = 0;
            TitleText.RenderTransform = titleShift;
            TitleText.Text = curTitle; TitleText.Width = InfoW; TitleText.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        double TextWidth(string t)
        {
            var probe = new TextBlock { Text = t, FontSize = TitleText.FontSize, FontWeight = TitleText.FontWeight, FontFamily = TitleText.FontFamily };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        }
        void StartMarquee()
        {
            if (InfoPanel.Visibility != Visibility.Visible || TextWidth(curTitle) <= InfoW) return;
            const string gap = "      •      ";
            double cycle = TextWidth(curTitle + gap);                  // distância até a 2ª cópia ocupar o lugar da 1ª
            TitleText.TextTrimming = TextTrimming.None; TitleText.ClearValue(WidthProperty);
            TitleText.Text = curTitle + gap + curTitle;                 // duas cópias: o loop fica contínuo, sem "pulo"
            var a = new DoubleAnimation(0, -cycle, TimeSpan.FromSeconds(cycle / 60)) { RepeatBehavior = RepeatBehavior.Forever }; // ~60 px/s
            titleShift.BeginAnimation(TranslateTransform.XProperty, a);
        }

        // Dica ao passar o mouse na bateria: nome, %, carregando ou tempo restante estimado
        static string BatteryTip(BtBattery b)
        {
            if (b.Percent < 0) return $"{b.Name}: {L.S("nobatt")}";
            var tip = $"{b.Name}: {b.Percent}%";
            if (b.Charging) return tip + $" ({L.S("charging")})";
            var r = App.Remaining(b);
            if (r == null) return tip + $"\n{L.S("estimating")}";
            var tl = r.Value;
            string txt = tl.TotalHours >= 1 ? $"~{(int)tl.TotalHours} h {tl.Minutes:00} min" : $"~{Math.Max(1, (int)tl.TotalMinutes)} min";
            return tip + $"\n{txt} {L.S("left")}";
        }

        // Menu do botão direito: as mesmas opções do ícone da bandeja (ações centralizadas no App)
        void ShowMenu()
        {
            var m = AppMenu.ToWpf(AppMenu.Build(this));
            m.Opened += (_, __) => { menuOpen = true; HookMouse(); };
            m.Closed += (_, __) => { menuOpen = false; UnhookMouse(); };
            openMenu = m; menuOpen = true; m.IsOpen = true;
        }

        // Ícone + % de cada dispositivo Bluetooth com bateria
        void UpdateBatteries() => Dispatcher.Invoke(() =>
        {
            BatteryPanel.Children.Clear();
            if (!App.ShowBattery) return;
            foreach (var b in App.Batteries)
            {
                // coluna 1: desenho + % (centralizados); coluna 2: raio, centralizado na altura, colado
                var col = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
                col.Children.Add(new System.Windows.Shapes.Path { Data = IconGeometry(b.Glyph), Stroke = fg, StrokeThickness = 1.4,
                    StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Width = 22, Height = 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true });
                col.Children.Add(new TextBlock { Text = b.Percent < 0 ? "--" : b.Percent + "%", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = b.Percent >= 0 && b.Percent <= 20 && !b.Charging ? Brushes.OrangeRed : fg });
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = vertical ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent,
                    ToolTip = b.Name };
                var bb = b;
                sp.ToolTipOpening += (_, __) => sp.ToolTip = BatteryTip(bb);
                sp.Children.Add(col);
                if (b.Charging)
                {
                    var bolt = new System.Windows.Shapes.Path { Data = BoltGeometry, Fill = fg,
                        Width = 6, Height = 10, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                    if (vertical)
                    {
                        // na barra vertical: raio "espremido" à direita sem ocupar espaço (ícone e % continuam centralizados)
                        var holder = new Canvas { Width = 0, Height = 10, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
                        Canvas.SetLeft(bolt, 0); bolt.Margin = new Thickness(1, 0, 0, 0);
                        holder.Children.Add(bolt);
                        sp.Children.Add(holder);
                    }
                    else sp.Children.Add(bolt);
                }
                BatteryPanel.Children.Add(sp);
            }
        });

        // Desenhos vetoriais dos dispositivos (24x24); cada controle com o seu formato
        const string Body = "M7,6 C4.8,6 3.4,7.6 2.9,10 L1.6,16.6 C1.3,18.6 2.5,20 4,20 C5.2,20 6,19.2 6.8,18 L8.2,16 H15.8 L17.2,18 C18,19.2 18.8,20 20,20 C21.5,20 22.7,18.6 22.4,16.6 L21.1,10 C20.6,7.6 19.2,6 17,6 Z ";
        static string Circ(double x, double y, double r) => FormattableString.Invariant($"M{x - r},{y} a{r},{r} 0 1 0 {2 * r},0 a{r},{r} 0 1 0 {-2 * r},0 ");
        // joystick genérico: corpo com pegadores, direcional, 4 botões e 2 analógicos
        static readonly string Pad = Body + "M4.8,11 H7.8 M6.3,9.5 V12.5 " + Circ(17.3, 9.6, 0.55) + Circ(18.9, 11.1, 0.55) + Circ(15.7, 11.1, 0.55) + Circ(17.3, 12.6, 0.55) + Circ(9.4, 14.2, 1.3) + Circ(14.6, 14.2, 1.3);
        static readonly System.Collections.Generic.Dictionary<string, string> Icons = new()
        {
            ["pad"] = Pad, ["ds5"] = Pad, ["ds4"] = Pad, ["xbox"] = Pad,
            ["head"] = "M4,15 V12 A8,8 0 0 1 20,12 V15 M3,14 H6.5 V20 H4.5 A1.5,1.5 0 0 1 3,18.5 Z M21,14 H17.5 V20 H19.5 A1.5,1.5 0 0 0 21,18.5 Z",
            ["buds"] = Circ(7, 9, 3) + "M8.5,11.6 V19 H6.5 V11.9 " + Circ(17, 9, 3) + "M15.5,11.6 V19 H17.5 V11.9",
            ["spk"] = "M6,3 H18 A1.5,1.5 0 0 1 19.5,4.5 V19.5 A1.5,1.5 0 0 1 18,21 H6 A1.5,1.5 0 0 1 4.5,19.5 V4.5 A1.5,1.5 0 0 1 6,3 Z " + Circ(12, 14.5, 3.5) + Circ(12, 7.5, 1.3),
            ["kbd"] = "M3,6 H21 A1.5,1.5 0 0 1 22.5,7.5 V16.5 A1.5,1.5 0 0 1 21,18 H3 A1.5,1.5 0 0 1 1.5,16.5 V7.5 A1.5,1.5 0 0 1 3,6 Z M5,9.5 H6 M8,9.5 H9 M11,9.5 H12 M14,9.5 H15 M17,9.5 H19 M5,12.5 H6.5 M9,12.5 H10 M12,12.5 H13 M15.5,12.5 H19 M7.5,15 H16.5",
            ["mouse"] = "M12,2.5 C8,2.5 6,5.5 6,9 V15 C6,18.8 8.6,21.5 12,21.5 C15.4,21.5 18,18.8 18,15 V9 C18,5.5 16,2.5 12,2.5 Z M6,10 H18 M12,2.5 V10 M12,5 V7.5",
            ["bt"] = "M7,7.5 L17,16.5 L12,21 V3 L17,7.5 L7,16.5",
        };

        // Desenhos convertidos uma única vez e compartilhados (sem recriar a cada atualização)
        static readonly System.Collections.Generic.Dictionary<string, Geometry> geoCache = new();
        static readonly Geometry BoltGeometry = Frozen("M6,0 L1,7 H4.5 L3,12 L9,4.5 H5.5 Z");
        static Geometry Frozen(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }
        static Geometry IconGeometry(string kind)
        {
            if (!Icons.ContainsKey(kind)) kind = "bt";
            if (!geoCache.TryGetValue(kind, out var g)) geoCache[kind] = g = Frozen(Icons[kind]);
            return g;
        }

        // Barras do espectro atrás dos botões, na cor predominante da capa
        readonly SolidColorBrush accent = new(Colors.White);
        readonly float[] shown = new float[App.Bands];
        void SetupSpectrum()
        {
            // barras escaladas por transformação (não recalcula layout a cada quadro)
            var scales = new ScaleTransform[App.Bands];
            for (int i = 0; i < App.Bands; i++)
            {
                scales[i] = new ScaleTransform(1, 0);
                Spectrum.Children.Add(new Rectangle { Fill = App.CurrentTheme.Spectrum != null ? B(App.CurrentTheme.Spectrum) : accent, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1, RadiusY = 1,
                    RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scales[i] });
            }
            if (vertical) for (int i = 1; i < Spectrum.Children.Count; i += 2) Spectrum.Children[i].Visibility = Visibility.Collapsed;
            bool idle = true;
            var t = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            t.Tick += (_, __) =>
            {
                if (!IsVisible) return;
                bool live = (DateTime.Now - App.LastAudio).TotalMilliseconds < 200;
                if (!live && idle) return; // silêncio: não redesenha nada
                bool any = false;
                for (int i = 0; i < App.Bands; i++)
                {
                    float target = live ? App.Levels[i] : 0;
                    shown[i] = target > shown[i] ? target : shown[i] * 0.85f; // sobe rápido, desce suave
                    if (shown[i] < 0.005f) shown[i] = 0; else any = true;
                    scales[i].ScaleY = shown[i];
                }
                idle = !live && !any;
            };
            t.Start();
            Closed += (_, __) => t.Stop();
        }

        // Cor predominante (mais saturada) da capa
        // Cor principal (para espectro/tema) e uma 2ª cor (lado oposto da capa) para o degradê
        static (Color main, Color second) AccentPair(BitmapSource src)
        {
            try
            {
                var small = new FormatConvertedBitmap(new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight)), PixelFormats.Bgra32, null, 0);
                int w = small.PixelWidth, h = small.PixelHeight;
                var px = new byte[w * h * 4]; small.CopyPixels(px, w * 4, 0);
                var sum = new double[3, 4]; // [todos, esquerda, direita] x [r,g,b,peso]
                for (int i = 0; i < px.Length; i += 4)
                {
                    int B = px[i], G = px[i + 1], R = px[i + 2], x = (i / 4) % w;
                    int mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    double sat = mx == 0 ? 0 : (mx - mn) / (double)mx, k = 0.02 + sat * sat * (mx / 255.0);
                    foreach (int part in new[] { 0, x < w / 2 ? 1 : 2 }) { sum[part, 0] += R * k; sum[part, 1] += G * k; sum[part, 2] += B * k; sum[part, 3] += k; }
                }
                Color Of(int part)
                {
                    double r = sum[part, 0] / sum[part, 3], g = sum[part, 1] / sum[part, 3], b = sum[part, 2] / sum[part, 3];
                    double max = Math.Max(r, Math.Max(g, b)), f = max < 1 ? 1 : 255 / max; // clareia até o máximo
                    return Color.FromRgb((byte)(r * f), (byte)(g * f), (byte)(b * f));
                }
                Color main = Of(0), l = Of(1), rr = Of(2);
                Color second = Distance(l, main) > Distance(rr, main) ? l : rr;
                if (Distance(second, main) < 40) second = Color.FromRgb((byte)(main.R * 0.55), (byte)(main.G * 0.55), (byte)(main.B * 0.55)); // capa de uma cor só: tom mais escuro
                return (main, second);
            }
            catch { return (Colors.White, Colors.White); }
        }
        static double Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);


        async Task InitMedia()
        {
            // logo após ligar o PC o serviço de mídia do Windows pode ainda não estar pronto: tenta de novo em vez de falhar
            for (int tries = 0; ; tries++)
            {
                try
                {
                    mgr = await Mgr.RequestAsync();
                    mgr.CurrentSessionChanged += (_, __) => Dispatcher.Invoke(Hook);
                    mgr.SessionsChanged += (_, __) => Dispatcher.Invoke(Hook);
                    Hook();
                    return;
                }
                catch { if (tries >= 30) return; await Task.Delay(2000); }
            }
        }

        // Fonte escolhida pelo usuário (vale para os players de todos os monitores). null = automático.
        static string pinnedSource;
        // Fonte detectada automaticamente: a mídia que está de fato tocando/emitindo som
        static string autoSource;
        static readonly System.Collections.Generic.Dictionary<uint, string> procNames = new();

        // A cada 1,5 s (só o player do monitor principal faz a conta, e só com 2+ mídias abertas):
        // 1) a que está em "tocando"; 2) se houver várias, a que está realmente saindo som no mixer do Windows
        void AutoPickSource()
        {
            try
            {
                if (mgr == null || pinnedSource != null) return;
                var all = mgr.GetSessions().ToArray();
                if (all.Length < 2) { autoSource = null; return; }
                var playing = all.Where(x => { try { return x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; } }).ToArray();
                GlobalSystemMediaTransportControlsSession pick = null;
                if (playing.Length == 1) pick = playing[0];
                else if (playing.Length > 1)
                {
                    var peaks = Peaks();
                    float Peak(GlobalSystemMediaTransportControlsSession x)
                    {
                        var app = SourceName(x).ToLowerInvariant(); float m = 0;
                        foreach (var (pn, v) in peaks) if (pn.Length > 1 && (pn.Contains(app) || app.Contains(pn))) m = Math.Max(m, v);
                        return m;
                    }
                    var best = playing.OrderByDescending(Peak).First();
                    if (Peak(best) > 0.001f) pick = best;
                }
                if (pick == null || pick.SourceAppUserModelId == session?.SourceAppUserModelId) return; // nada tocando: mantém o atual
                autoSource = pick.SourceAppUserModelId;
                foreach (var w in Application.Current.Windows.OfType<MainWindow>()) w.Hook();
            }
            catch { }
        }

        // Nível de som atual de cada programa no mixer do Windows
        static System.Collections.Generic.List<(string pn, float v)> Peaks()
        {
            var r = new System.Collections.Generic.List<(string, float)>();
            try
            {
                using var dev = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var list = dev.AudioSessionManager.Sessions;
                for (int i = 0; i < list.Count; i++)
                {
                    var sc = list[i];
                    uint pid = sc.GetProcessID;
                    if (!procNames.TryGetValue(pid, out var pn))
                    {
                        try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { pn = ""; }
                        procNames[pid] = pn;
                    }
                    r.Add((pn, sc.AudioMeterInformation.MasterPeakValue));
                }
            }
            catch { }
            return r;
        }
        void Hook()
        {
            if (mgr == null) return;
            if (session != null) { session.MediaPropertiesChanged -= OnMedia; session.PlaybackInfoChanged -= OnPlay; }
            GlobalSystemMediaTransportControlsSession[] all;
            try { all = mgr.GetSessions().ToArray(); } catch { all = Array.Empty<GlobalSystemMediaTransportControlsSession>(); }
            var pinned = pinnedSource == null ? null : all.FirstOrDefault(x => x.SourceAppUserModelId == pinnedSource);
            if (pinned == null) pinnedSource = null; // a fonte escolhida fechou: volta ao automático
            var auto = autoSource == null ? null : all.FirstOrDefault(x => x.SourceAppUserModelId == autoSource);
            session = pinned ?? auto ?? mgr.GetCurrentSession();
            if (session != null) { session.MediaPropertiesChanged += OnMedia; session.PlaybackInfoChanged += OnPlay; }
            // botão de troca só aparece com mais de uma fonte
            SwapBtn.Visibility = all.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            SwapBtn.ToolTip = $"{L.S("source")} ({SourceName(session)})";
            _ = Refresh();
        }

        static string SourceName(GlobalSystemMediaTransportControlsSession s)
        {
            var id = s?.SourceAppUserModelId ?? "";
            id = id.Split('!')[0];
            if (id.Contains('_')) id = id.Split('_')[0];
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
            int dot = id.LastIndexOf('.'); if (dot >= 0 && dot < id.Length - 1) id = id[(dot + 1)..];
            return id;
        }

        void Swap_Click(object s, RoutedEventArgs e)
        {
            try
            {
                var all = mgr.GetSessions().ToArray();
                if (all.Length < 2) return;
                int i = Array.FindIndex(all, x => x.SourceAppUserModelId == session?.SourceAppUserModelId);
                pinnedSource = all[(i + 1) % all.Length].SourceAppUserModelId;
                foreach (var w in Application.Current.Windows.OfType<MainWindow>()) w.immediateUntil = DateTime.Now.AddSeconds(1);
                foreach (var w in Application.Current.Windows.OfType<MainWindow>()) w.Hook();
            }
            catch { }
        }

        void OnMedia(object s, object e) => Dispatcher.Invoke(() => _ = Refresh());
        void OnPlay(object s, object e) => Dispatcher.Invoke(() => _ = Refresh());

        // Ignora trocas rápidas de mídia (ex.: prévia ao passar o mouse numa miniatura do YouTube):
        // título/capa novos só aparecem se continuarem os mesmos por 2 s. Ações do próprio usuário (avançar,
        // voltar, trocar fonte) e a primeira mídia aparecem na hora.
        string shownKey, pendingKey;
        DateTime immediateUntil;
        readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromSeconds(2) };

        async Task Refresh(bool fromDebounce = false)
        {
            if (session == null) { shownKey = pendingKey = null; isPlaying = false; Progress.Visibility = Visibility.Collapsed; SetTitle(L.S("nothing")); ArtistText.Text = ""; Art.Source = null; NoArt.Visibility = Visibility.Visible; PlayBtn.Content = ""; return; }
            try
            {
                var p = await session.TryGetMediaPropertiesAsync();
                var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                isPlaying = playing;
                UpdateProgress();
                PlayBtn.Content = playing ? "" : "";
                string baseKey = (p.Title ?? "") + "\n" + (p.Artist ?? "");
                string key = baseKey + (p.Thumbnail != null ? "\n+capa" : "");
                if (key == shownKey) { pendingKey = null; return; } // mesma mídia (ex.: só pausou/voltou ao original)
                bool sameSong = shownKey != null && shownKey.StartsWith(baseKey + "\n") || shownKey == baseKey; // só chegou a capa/dados da mesma música
                bool now = shownKey == null || sameSong || DateTime.Now < immediateUntil || (fromDebounce && key == pendingKey);
                if (!now)
                {
                    if (key != pendingKey) { pendingKey = key; debounce.Stop(); debounce.Start(); } // espera 2 s estável
                    return;
                }
                shownKey = key; pendingKey = null; debounce.Stop();
                SetTitle(string.IsNullOrEmpty(p.Title) ? L.S("untitled") : p.Title);
                ArtistText.Text = p.Artist;
                Art.Source = null; NoArt.Visibility = Visibility.Visible; accent.Color = Colors.White;
                if (p.Thumbnail != null)
                {
                    using var ws = await p.Thumbnail.OpenReadAsync();
                    using var st = ws.AsStreamForRead();
                    using var ms = new MemoryStream(); await st.CopyToAsync(ms); ms.Position = 0;
                    var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 160; /* nítido até ~500% de escala */ bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    Art.Source = bmp; NoArt.Visibility = Visibility.Collapsed;
                    (var c1, accent2) = AccentPair(bmp); accent.Color = c1;
                    if (App.Theme == App.DynamicTheme) ApplyDynamic(true);
                }
                if (p.Thumbnail == null && App.Theme == App.DynamicTheme) ApplyDynamic(true);
            }
            catch { }
        }

        async void Prev_Click(object s, RoutedEventArgs e) { immediateUntil = DateTime.Now.AddSeconds(5); if (session != null) await session.TrySkipPreviousAsync(); }
        async void Play_Click(object s, RoutedEventArgs e) { if (session != null) await session.TryTogglePlayPauseAsync(); }
        async void Next_Click(object s, RoutedEventArgs e) { immediateUntil = DateTime.Now.AddSeconds(5); if (session != null) await session.TrySkipNextAsync(); }
    }
}
