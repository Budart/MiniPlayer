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
        public static bool Floating, ShowBattery = true, FloatBehind;
        public static double FloatX = 200, FloatY = 200;
        public static List<BtBattery> Batteries = new();
        public static event Action BatteriesChanged;
        // Publica a lista de baterias na interface só quando algo mudou
        public static void RefreshBatteries()
        {
            var l = ShowBattery ? BatteryReader.Snapshot() : new List<BtBattery>();
            if (l.SequenceEqual(Batteries)) return;
            Batteries = l; BatteriesChanged?.Invoke();
        }
        public static bool Centered => (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1) as int? ?? 1) != 0;

        readonly Dictionary<IntPtr, MainWindow> players = new();

        void OnStartup(object s, StartupEventArgs e)
        {
            try { var p = File.ReadAllText(Cfg).Split(';'); Floating = p[0] == "1"; FloatX = double.Parse(p[1], CultureInfo.InvariantCulture); FloatY = double.Parse(p[2], CultureInfo.InvariantCulture); if (p.Length > 3) ShowBattery = p[3] == "1"; if (p.Length > 4) FloatBehind = p[4] == "1"; } catch { }
            Sync();
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
                cap.RecordingStopped += (s, e) => { try { cap.Dispose(); } catch { } Task.Delay(2000).ContinueWith(_ => StartSpectrum()); };
                cap.StartRecording();
            }
            catch { }
        }

        static void Analyze(int sr)
        {
            int N = ring.Length;
            var c = new Complex[N];
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

        public static void Save() { try { File.WriteAllText(Cfg, string.Create(CultureInfo.InvariantCulture, $"{(Floating ? 1 : 0)};{FloatX};{FloatY};{(ShowBattery ? 1 : 0)};{(FloatBehind ? 1 : 0)}")); } catch { } }

        // Decide quais barras recebem player: fixo = todas as barras; solto = só a principal
        void Sync()
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
