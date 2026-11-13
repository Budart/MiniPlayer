using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

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
        public static bool Floating; public static double FloatX = 200, FloatY = 200;
        public static bool Centered => (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1) as int? ?? 1) != 0;

        readonly Dictionary<IntPtr, MainWindow> players = new();

        void OnStartup(object s, StartupEventArgs e)
        {
            try { var p = File.ReadAllText(Cfg).Split(';'); Floating = p[0] == "1"; FloatX = double.Parse(p[1]); FloatY = double.Parse(p[2]); } catch { }
            Sync();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, __) => Sync();
            t.Start();
        }

        public static void Save() { try { File.WriteAllText(Cfg, $"{(Floating ? 1 : 0)};{FloatX};{FloatY}"); } catch { } }

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
