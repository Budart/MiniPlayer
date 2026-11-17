using System;
using System.IO;
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
        void HookMouse()
        {
            mouseProc = (code, wp, lp) =>
            {
                int msg = (int)wp;
                if (code >= 0 && (msg == 0x201 || msg == 0x204 || msg == 0x207))
                {
                    int x = Marshal.ReadInt32(lp), y = Marshal.ReadInt32(lp, 4);
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (openMenu == null || !openMenu.IsOpen) return;
                        try
                        {
                            var a = openMenu.PointToScreen(new Point(0, 0));
                            var b = openMenu.PointToScreen(new Point(openMenu.ActualWidth, openMenu.ActualHeight));
                            if (x < a.X || x > b.X || y < a.Y || y > b.Y) openMenu.IsOpen = false;
                        }
                        catch { openMenu.IsOpen = false; }
                    });
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

        public MainWindow(IntPtr taskbar, bool primary)
        {
            bar = taskbar; isPrimary = primary;
            InitializeComponent();
            Loaded += async (_, __) => { Setup(); SetupSpectrum(); UpdateBatteries(); await InitMedia(); };
            App.BatteriesChanged += UpdateBatteries;
            Closed += (_, __) => App.BatteriesChanged -= UpdateBatteries;
            Root.MouseRightButtonUp += (_, __) => ShowMenu();
            Root.MouseLeftButtonDown += (s, e) =>
            {
                if (!App.Floating || e.OriginalSource is Button) return;
                dragging = true; DragMove(); dragging = false; App.FloatX = Left; App.FloatY = Top; App.Save();
            };
        }

        void Setup()
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); // toolwindow + noactivate
            Place();
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
                if (Math.Abs(Left - App.FloatX) > 1 || Math.Abs(Top - App.FloatY) > 1) { Left = App.FloatX; Top = App.FloatY; }
            }
            else if (GetWindowRect(bar, out var r) && GetWindowRect(me, out var w))
            {
                var src = PresentationSource.FromVisual(this);
                double s = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
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

        // Menu do botão direito: as mesmas opções do ícone da bandeja (ações centralizadas no App)
        void ShowMenu()
        {
            var m = new ContextMenu();
            MenuItem Item(string text, bool check, bool enabled, Action<bool> act)
            {
                var i = new MenuItem { Header = text, IsCheckable = true, IsChecked = check, IsEnabled = enabled };
                i.Click += (_, __) => act(i.IsChecked);
                return i;
            }
            m.Items.Add(Item("Soltar (arrastar livremente)", App.Floating, true, on => App.SetFloating(on, Left, Top - 60)));
            m.Items.Add(Item("Manter atrás das janelas", App.FloatBehind, App.Floating, App.SetBehind));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Mostrar bateria", App.ShowBattery, true, App.SetBattery));
            m.Items.Add(Item("Iniciar com o Windows", App.StartsWithWindows, true, App.SetStartup));
            m.Items.Add(new Separator());
            var quit = new MenuItem { Header = "Fechar" };
            quit.Click += (_, __) => App.Quit();
            m.Items.Add(quit);
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
                col.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(Icons.TryGetValue(b.Glyph, out var g) ? g : Icons["bt"]), Stroke = Brushes.White, StrokeThickness = 1.4,
                    StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    Width = 22, Height = 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true });
                col.Children.Add(new TextBlock { Text = b.Percent + "%", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = b.Percent <= 20 && !b.Charging ? Brushes.OrangeRed : Brushes.White });
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
                    ToolTip = $"{b.Name}: {b.Percent}%" + (b.Charging ? " (carregando)" : "") };
                sp.Children.Add(col);
                if (b.Charging)
                    sp.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M6,0 L1,7 H4.5 L3,12 L9,4.5 H5.5 Z"), Fill = Brushes.White,
                        Width = 6, Height = 10, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
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
                Spectrum.Children.Add(new Rectangle { Fill = accent, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1, RadiusY = 1,
                    RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scales[i] });
            }
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
        static Color Accent(BitmapSource src)
        {
            try
            {
                var small = new FormatConvertedBitmap(new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight)), PixelFormats.Bgra32, null, 0);
                int w = small.PixelWidth, h = small.PixelHeight;
                var px = new byte[w * h * 4]; small.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, wt = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    int B = px[i], G = px[i + 1], R = px[i + 2];
                    int mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    double sat = mx == 0 ? 0 : (mx - mn) / (double)mx, k = 0.02 + sat * sat * (mx / 255.0);
                    r += R * k; g += G * k; b += B * k; wt += k;
                }
                r /= wt; g /= wt; b /= wt;
                double max = Math.Max(r, Math.Max(g, b)), f = max < 1 ? 1 : 255 / max; // clareia até o máximo
                return Color.FromRgb((byte)(r * f), (byte)(g * f), (byte)(b * f));
            }
            catch { return Colors.White; }
        }


        async Task InitMedia()
        {
            mgr = await Mgr.RequestAsync();
            mgr.CurrentSessionChanged += (_, __) => Dispatcher.Invoke(Hook);
            Hook();
        }

        void Hook()
        {
            if (session != null) { session.MediaPropertiesChanged -= OnMedia; session.PlaybackInfoChanged -= OnPlay; }
            session = mgr.GetCurrentSession();
            if (session != null) { session.MediaPropertiesChanged += OnMedia; session.PlaybackInfoChanged += OnPlay; }
            _ = Refresh();
        }

        void OnMedia(object s, object e) => Dispatcher.Invoke(() => _ = Refresh());
        void OnPlay(object s, object e) => Dispatcher.Invoke(() => _ = Refresh());

        async Task Refresh()
        {
            if (session == null) { TitleText.Text = "Nada tocando"; ArtistText.Text = ""; Art.Source = null; NoArt.Visibility = Visibility.Visible; PlayBtn.Content = ""; return; }
            try
            {
                var p = await session.TryGetMediaPropertiesAsync();
                TitleText.Text = string.IsNullOrEmpty(p.Title) ? "Sem título" : p.Title;
                ArtistText.Text = p.Artist;
                var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                PlayBtn.Content = playing ? "" : "";
                Art.Source = null; NoArt.Visibility = Visibility.Visible; accent.Color = Colors.White;
                if (p.Thumbnail != null)
                {
                    using var ws = await p.Thumbnail.OpenReadAsync();
                    using var st = ws.AsStreamForRead();
                    var ms = new MemoryStream(); await st.CopyToAsync(ms); ms.Position = 0;
                    var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    Art.Source = bmp; NoArt.Visibility = Visibility.Collapsed;
                    accent.Color = Accent(bmp);
                }
            }
            catch { }
        }

        async void Prev_Click(object s, RoutedEventArgs e) { if (session != null) await session.TrySkipPreviousAsync(); }
        async void Play_Click(object s, RoutedEventArgs e) { if (session != null) await session.TryTogglePlayPauseAsync(); }
        async void Next_Click(object s, RoutedEventArgs e) { if (session != null) await session.TrySkipNextAsync(); }
    }
}
