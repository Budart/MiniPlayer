using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Mgr = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace MiniPlayer
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string w);
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
            Loaded += async (_, __) => { Setup(); await InitMedia(); };
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
            t.Tick += (_, __) => { if (!pending && ++n % 3 != 0) return; pending = false; CheckFullscreen(); if (!dragging) Place(); };
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
                var hh = Math.Min(48, (r.B - r.T) / s); if (Math.Abs(Height - hh) > 0.5) Height = hh;
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
            if (!menuOpen && !fullscreen)
                SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // Monitor sem bandeja: acha (via acessibilidade) o item mais à esquerda do grupo da direita (relógio etc.)
        int cachedAnchor; DateTime anchorTime;
        int RightItemsLeft(RECT r, double s)
        {
            if ((DateTime.Now - anchorTime).TotalSeconds < 2 && cachedAnchor > 0) return cachedAnchor;
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
            SetWindowPos(me, new IntPtr(fs ? -2 : -1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            if (fs) SetWindowPos(me, fg, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // logo atrás da janela cheia
            Visibility = fs ? Visibility.Hidden : Visibility.Visible;
        }

        void ShowMenu()
        {
            var m = new ContextMenu();
            var startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MiniPlayer.lnk");
            var auto = new MenuItem { Header = "Iniciar com o Windows", IsCheckable = true, IsChecked = File.Exists(startup) };
            auto.Click += (_, __) => ToggleStartup(startup, auto.IsChecked);
            var free = new MenuItem { Header = "Soltar (arrastar livremente)", IsCheckable = true, IsChecked = App.Floating };
            free.Click += (_, __) => { App.Floating = free.IsChecked; if (App.Floating) { App.FloatX = Left; App.FloatY = Top - 60; } App.Save(); };
            m.Items.Add(free);
            var quit = new MenuItem { Header = "Fechar" };
            quit.Click += (_, __) => Application.Current.Shutdown();
            m.Items.Add(auto); m.Items.Add(quit);
            m.Opened += (_, __) => menuOpen = true;
            m.Closed += (_, __) => menuOpen = false;
            menuOpen = true; m.IsOpen = true;
        }

        static void ToggleStartup(string lnk, bool on)
        {
            try
            {
                if (!on) { File.Delete(lnk); return; }
                var exe = Environment.ProcessPath;
                var sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = sh.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                sc.TargetPath = exe; sc.WorkingDirectory = Path.GetDirectoryName(exe); sc.Save();
            }
            catch { }
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
                Art.Source = null; NoArt.Visibility = Visibility.Visible;
                if (p.Thumbnail != null)
                {
                    using var ws = await p.Thumbnail.OpenReadAsync();
                    using var st = ws.AsStreamForRead();
                    var ms = new MemoryStream(); await st.CopyToAsync(ms); ms.Position = 0;
                    var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    Art.Source = bmp; NoArt.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        async void Prev_Click(object s, RoutedEventArgs e) { if (session != null) await session.TrySkipPreviousAsync(); }
        async void Play_Click(object s, RoutedEventArgs e) { if (session != null) await session.TryTogglePlayPauseAsync(); }
        async void Next_Click(object s, RoutedEventArgs e) { if (session != null) await session.TrySkipNextAsync(); }
    }
}
