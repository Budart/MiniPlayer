using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;

namespace MiniPlayer
{
    // Gerencia um player por barra de tarefas, conforme o alinhamento do Windows
    public partial class App : Application
    {
        delegate bool EnumProc(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string w);

        static readonly string Cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniPlayer.txt");
        public static bool Floating, ShowBattery = true, FloatBehind, HidePlayer, FloatVertical, FloatLocked;
        public static bool VolumeHintDone;
        public static int PlayerSize = 1;                                  // 0 pequeno, 1 médio, 2 grande
        public static readonly double[] SizeScales = { 0.85, 1.0, 1.2 };
        public static void SetSize(int i) { PlayerSize = Math.Clamp(i, 0, 2); Save(); LookChanged?.Invoke(); }
        public static int Theme;
        public const int DynamicTheme = 10;
        public static event Action LookChanged; // tema ou "ocultar player" mudou

        // Temas: fundo, texto principal, texto secundário, cor fixa do espectro (null = cor da capa)
        public record ThemeDef(string Name, string Bg, string Fg, string Fg2, string Spectrum, string Bg2 = null); // Bg→Bg2 = degradê
        public static readonly ThemeDef[] Themes =
        {
            new("Padrão (transparente)", "#01000000", "#FFFFFFFF", "#BBFFFFFF", null),
            new("Escuro",                "#E61B1D22", "#FFFFFFFF", "#AAFFFFFF", null, "#E62E323C"),
            new("Claro",                 "#EEF4F5F7", "#FF1B1D22", "#991B1D22", null, "#EED9E1EC"),
            new("Neon",                  "#E60B0F1A", "#FF00E5FF", "#AAFF4FD8", "#FF00E5FF", "#E6260A3A"),
            new("Pôr do sol",            "#E62A1410", "#FFFFC27A", "#BBFF8A65", "#FFFF7A45", "#E64A1630"),
            new("Oceano",                "#E60A2540", "#FF7FDBFF", "#AAB3E5FC", "#FF29B6F6", "#E6054A4E"),
            new("Floresta",              "#E60F2418", "#FFB9F6CA", "#AA81C784", "#FF66BB6A", "#E6243A10"),
            new("Púrpura",               "#E62A1240", "#FFE1BEE7", "#AACE93D8", "#FFBA68C8", "#E64A0E36"),
            new("Rubi",                  "#E63A0E14", "#FFFFCDD2", "#AAEF9A9A", "#FFEF5350", "#E62C0A2E"),
            new("Alto contraste",        "#FF000000", "#FFFFFF00", "#FFFFFFFF", "#FFFFFF00", "#FF1C1C1C"),
            new("Dinâmico",              "#E6202020", "#FFFFFFFF", "#BBFFFFFF", null), // cores calculadas da capa (ver MainWindow.ApplyDynamic)
        };
        public static ThemeDef CurrentTheme => Themes[Math.Clamp(Theme, 0, Themes.Length - 1)];
        public static void SetTheme(int i) { Theme = i; Save(); LookChanged?.Invoke(); }
        public static void SetHidePlayer(bool on) { if (on && !ShowBattery) return; HidePlayer = on; Save(); LookChanged?.Invoke(); }
        public static double FloatX = 200, FloatY = 200;
        public static List<BtBattery> Batteries = new();
        public static event Action BatteriesChanged;
        // Publica a lista de baterias na interface só quando algo mudou
        // Histórico de bateria por aparelho (só em memória): base para estimar o tempo restante
        static readonly Dictionary<string, List<(DateTime t, int p)>> hist = new();
        static void Record(List<BtBattery> l)
        {
            foreach (var b in l)
            {
                if (b.Percent < 0) continue;
                if (b.Charging) { hist.Remove(b.Name); continue; }
                if (!hist.TryGetValue(b.Name, out var h)) hist[b.Name] = h = new();
                if (h.Count > 0 && b.Percent > h[^1].p) h.Clear();                 // carregou: recomeça
                if (h.Count == 0 || h[^1].p != b.Percent) h.Add((DateTime.Now, b.Percent));
                if (h.Count > 60) h.RemoveAt(0);
            }
        }
        // null = ainda sem dados suficientes
        public static TimeSpan? Remaining(BtBattery b)
        {
            if (b.Charging || b.Percent < 0 || !hist.TryGetValue(b.Name, out var h) || h.Count < 2) return null;
            var (t0, p0) = h[0]; var (t1, p1) = h[^1];
            double drop = p0 - p1, mins = (t1 - t0).TotalMinutes;
            if (drop < 2 || mins < 5) return null;
            double left = b.Percent / (drop / mins) - (DateTime.Now - t1).TotalMinutes;
            return TimeSpan.FromMinutes(Math.Max(1, left));
        }

        public static void RefreshBatteries()
        {
            var l = ShowBattery ? BatteryReader.Snapshot() : new List<BtBattery>();
            Record(l);
            if (l.SequenceEqual(Batteries)) return;
            Batteries = l; BatteriesChanged?.Invoke();
        }
        public static bool Centered => (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1) as int? ?? 1) != 0;

        readonly Dictionary<IntPtr, MainWindow> players = new();

        static System.Threading.Mutex single;
        void OnStartup(object s, StartupEventArgs e)
        {
            // uma única instância (evita dois players/ícones se for aberto duas vezes)
            single = new System.Threading.Mutex(true, @"Local\MiniPlayer.SingleInstance", out bool first);
            if (!first) { Shutdown(); return; }
            // nenhum erro inesperado derruba o app (ex.: serviços do Windows ainda iniciando logo após ligar o PC)
            DispatcherUnhandledException += (_, ev) => ev.Handled = true;
            TaskScheduler.UnobservedTaskException += (_, ev) => ev.SetObserved();
            FixStartupEntry();
            try { var p = File.ReadAllText(Cfg).Split(';'); Floating = p[0] == "1"; FloatX = double.Parse(p[1], CultureInfo.InvariantCulture); FloatY = double.Parse(p[2], CultureInfo.InvariantCulture); if (p.Length > 3) ShowBattery = p[3] == "1"; if (p.Length > 4) FloatBehind = p[4] == "1"; if (p.Length > 5) HidePlayer = p[5] == "1"; if (p.Length > 6) Theme = int.Parse(p[6]); if (p.Length > 7) FloatVertical = p[7] == "1"; if (p.Length > 8) FloatLocked = p[8] == "1"; if (p.Length > 9) PlayerSize = Math.Clamp(int.Parse(p[9]), 0, 2); if (p.Length > 10) VolumeHintDone = p[10] == "1"; } catch { }
            Sync();
            SetupTray();
            TrimMemory();
            BatteryReader.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshBatteries));
            try { BatteryReader.Start(); } catch { }
            StartSpectrum();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, __) => Sync();
            t.Start();
        }

        // Espectro de áudio: captura o que está tocando no PC (loopback) e divide em faixas de frequência
        public const int Bands = 14;
        public static readonly float[] Levels = new float[Bands];
        public static DateTime LastAudio;
        static WasapiLoopbackCapture cap;
        static readonly float[] ring = new float[1024]; static int rp;
        static readonly Complex[] fft = new Complex[1024];

        static void StartSpectrum()
        {
            try
            {
                cap = new WasapiLoopbackCapture();
                var fmt = cap.WaveFormat;
                if (fmt.Encoding != WaveFormatEncoding.IeeeFloat) return;
                int ch = fmt.Channels;
                cap.DataAvailable += (s, e) =>
                {
                    var wb = new WaveBuffer(e.Buffer);
                    int n = e.BytesRecorded / 4;
                    for (int i = 0; i + ch - 1 < n; i += ch)
                    {
                        float v = 0; for (int c = 0; c < ch; c++) v += wb.FloatBuffer[i + c];
                        ring[rp++] = v / ch;
                        if (rp == ring.Length) { rp = 0; Analyze(fmt.SampleRate); }
                    }
                };
                cap.RecordingStopped += (s, e) => { try { cap.Dispose(); } catch { } if (!quitting) Task.Delay(2000).ContinueWith(_ => StartSpectrum()); };
                cap.StartRecording();
            }
            catch { }
        }

        static void Analyze(int sr)
        {
            int N = ring.Length;
            var c = fft;
            for (int i = 0; i < N; i++) { c[i].X = (float)(ring[i] * FastFourierTransform.HannWindow(i, N)); c[i].Y = 0; }
            FastFourierTransform.FFT(true, 10, c);
            double fMin = 50, fMax = 14000;
            for (int b = 0; b < Bands; b++)
            {
                double f0 = fMin * Math.Pow(fMax / fMin, (double)b / Bands), f1 = fMin * Math.Pow(fMax / fMin, (double)(b + 1) / Bands);
                int i0 = Math.Max(1, (int)(f0 * N / sr)), i1 = Math.Max(i0 + 1, (int)(f1 * N / sr));
                double m = 0;
                for (int i = i0; i < i1 && i < N / 2; i++) m = Math.Max(m, Math.Sqrt(c[i].X * c[i].X + c[i].Y * c[i].Y));
                Levels[b] = (float)Math.Clamp((20 * Math.Log10(m + 1e-9) + 80) / 60, 0, 1);
            }
            LastAudio = DateTime.Now;
        }

        // ---- Ações das opções (usadas pelo menu do player e pelo ícone da bandeja) ----
        // Inicialização com o Windows pela chave "Run" do usuário (mais confiável que atalho na pasta Inicializar:
        // não depende do Windows Script Host, que alguns antivírus/empresas bloqueiam, e é atualizada se o exe mudar de lugar)
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string RunName = "MiniPlayer";
        static readonly string StartupLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MiniPlayer.lnk");
        static string RunCommand => "\"" + Environment.ProcessPath + "\"";
        public static bool StartsWithWindows
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                    if (k?.GetValue(RunName) == null) return false;
                    // desativado no Gerenciador de Tarefas (aba Inicializar) também conta como desligado
                    using var a = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                    return !(a?.GetValue(RunName) is byte[] b && b.Length > 0 && (b[0] & 1) == 1);
                }
                catch { return false; }
            }
        }

        public static void SetFloating(bool on, double x, double y)
        {
            Floating = on;
            if (on) { FloatX = x; FloatY = y; }
            Save();
            (Current as App)?.Sync();
        }
        public static void SetBehind(bool on) { FloatBehind = on; Save(); }
        public static void SetFloatVertical(bool on) { FloatVertical = on; Save(); }
        public static void SetFloatLocked(bool on) { FloatLocked = on; Save(); }
        public static void SetBattery(bool on) { if (!on && HidePlayer) return; ShowBattery = on; Save(); RefreshBatteries(); }
        // Encerramento limpo: fecha janelas (solta ganchos), para a captura de áudio e garante que o processo termina
        static bool quitting;
        public static void Quit()
        {
            quitting = true;
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            try { cap?.StopRecording(); } catch { }
            foreach (Window w in Current.Windows) try { w.Close(); } catch { }
            Current.Shutdown();
            Environment.Exit(0);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool DeleteFile(string path);
        public static void SetStartup(bool on)
        {
            try
            {
                try { if (File.Exists(StartupLnk)) File.Delete(StartupLnk); } catch { } // remove o atalho das versões antigas
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                using var a = Registry.CurrentUser.CreateSubKey(ApprovedKey);
                if (!on) { k.DeleteValue(RunName, false); a.DeleteValue(RunName, false); return; }
                k.SetValue(RunName, RunCommand);
                a.SetValue(RunName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary); // "habilitado"
                // exe baixado da internet: tira a marca "veio da internet" para o Windows não pedir confirmação silenciosa no logon
                DeleteFile(Environment.ProcessPath + ":Zone.Identifier");
            }
            catch { }
        }

        // Ao abrir: migra o atalho antigo e corrige o caminho se o exe foi movido/renomeado
        static void FixStartupEntry()
        {
            try
            {
                bool hadLnk = File.Exists(StartupLnk);
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                var cur = k?.GetValue(RunName) as string;
                if (hadLnk || (cur != null && !string.Equals(cur, RunCommand, StringComparison.OrdinalIgnoreCase)))
                    SetStartup(true);
            }
            catch { }
        }

        // ---- Ícone na bandeja do sistema (perto do relógio) ----
        static System.Windows.Forms.NotifyIcon tray;
        // ícone da bandeja no tamanho exato da escala atual (o .ico tem 16 a 256 px)
        static System.Drawing.Icon TrayIcon()
        {
            try
            {
                using var st = GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
                return new System.Drawing.Icon(st, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
            catch { return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
        }
        void SetupTray()
        {
            try
            {
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); // placeholder: o conteúdo é montado ao abrir
                menu.Opening += (_, e) => { AppMenu.FillTray(menu.Items, AppMenu.Build(null)); e.Cancel = false; };
                tray = new System.Windows.Forms.NotifyIcon
                {
                    Icon = TrayIcon(),
                    Text = "MiniPlayer",
                    ContextMenuStrip = menu,
                    Visible = true
                };
                Exit += (_, __) => { tray.Visible = false; tray.Dispose(); };
            }
            catch { }
        }

        // Devolve ao Windows a memória usada só na inicialização (15 s depois) e periodicamente (10 min)
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);
        static void TrimMemory()
        {
            void Trim() { try { GC.Collect(2, GCCollectionMode.Optimized, false); SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1)); } catch { } }
            var t = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(15) };
            t.Tick += (_, __) => { Trim(); t.Interval = TimeSpan.FromMinutes(10); };
            t.Start();
        }

        public static void Save() { try { File.WriteAllText(Cfg, string.Create(CultureInfo.InvariantCulture, $"{(Floating ? 1 : 0)};{FloatX};{FloatY};{(ShowBattery ? 1 : 0)};{(FloatBehind ? 1 : 0)};{(HidePlayer ? 1 : 0)};{Theme};{(FloatVertical ? 1 : 0)};{(FloatLocked ? 1 : 0)};{PlayerSize};{(VolumeHintDone ? 1 : 0)}")); } catch { } }

        // Decide quais barras recebem player: fixo = todas as barras; solto = só a principal
        public void Sync()
        {
            var primary = FindWindow("Shell_TrayWnd", null);
            var bars = new List<IntPtr> { primary };
            if (!Floating)
                EnumWindows((h, _) => { var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, 64); if (sb.ToString() == "Shell_SecondaryTrayWnd") bars.Add(h); return true; }, IntPtr.Zero);
            bars.RemoveAll(h => h == IntPtr.Zero);

            foreach (var h in players.Keys.Except(bars).ToList()) { players[h].Close(); players.Remove(h); }
            foreach (var h in bars.Where(h => !players.ContainsKey(h))) { var w = new MainWindow(h, h == primary); players[h] = w; w.Show(); }
        }
    }
}
