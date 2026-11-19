using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using static MiniPlayer.Native;

namespace MiniPlayer
{
    // Ciclo de vida do app: instância única, serviços em segundo plano, bandeja e um player por barra de tarefas
    public partial class App : Application
    {
        static System.Threading.Mutex single;
        static System.Windows.Forms.NotifyIcon tray;
        readonly Dictionary<IntPtr, MainWindow> players = new();

        public static bool Centered => (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1) as int? ?? 1) != 0;

        void OnStartup(object s, StartupEventArgs e)
        {
            // uma única instância (evita dois players/ícones se for aberto duas vezes)
            single = new System.Threading.Mutex(true, @"Local\MiniPlayer.SingleInstance", out bool first);
            if (!first) { Shutdown(); return; }
            // nenhum erro inesperado derruba o app (ex.: serviços do Windows ainda iniciando logo após ligar o PC)
            DispatcherUnhandledException += (_, ev) => ev.Handled = true;
            TaskScheduler.UnobservedTaskException += (_, ev) => ev.SetObserved();

            StartupEntry.Fix();
            Settings.Load();
            Settings.ModeChanged += Sync;
            Settings.LookChanged += () => { SystemMonitor.Run(Settings.ShowSystem); BatteryState.Refresh(); };

            Sync();
            SetupTray();
            SystemMonitor.Run(Settings.ShowSystem);
            TrimMemory();
            BatteryReader.Changed += () => Dispatcher.BeginInvoke(new Action(BatteryState.Refresh));
            try { BatteryReader.Start(); } catch { }
            AudioSpectrum.Start();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, __) => Sync();
            t.Start();
        }

        // Encerramento limpo: fecha janelas (solta ganchos), para a captura de áudio e garante que o processo termina
        public static void Quit()
        {
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            AudioSpectrum.Stop();
            SystemMonitor.Run(false);
            foreach (Window w in Current.Windows) try { w.Close(); } catch { }
            Current.Shutdown();
            Environment.Exit(0);
        }

        // ---- Ícone na bandeja do sistema (perto do relógio) ----
        // no tamanho exato da escala atual (o .ico tem 16 a 256 px)
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
                menu.Closed += (_, __) => TrimSoon();
                tray = new System.Windows.Forms.NotifyIcon { Icon = TrayIcon(), Text = "MiniPlayer", ContextMenuStrip = menu, Visible = true };
                Exit += (_, __) => { tray.Visible = false; tray.Dispose(); };
            }
            catch { }
        }

        // Memória: devolve ao Windows o que não está em uso, mantendo o app abaixo de ~80 MB no Gerenciador de Tarefas.
        // Confere a cada 15 s (consulta barata) e só age se passou do limite; também logo após picos conhecidos
        // (inicialização, menu aberto, troca de tema/opções). Páginas devolvidas voltam sozinhas se forem usadas de novo.
        const long TrimAboveBytes = 80L * 1024 * 1024;
        static long lastTrim;
        public static void TrimSoon() => (Current as App)?.trimDue.Start();
        readonly DispatcherTimer trimDue = new(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(3) };
        void TrimMemory()
        {
            trimDue.Tick += (_, __) => { trimDue.Stop(); Trim(); };
            var t = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(15) };
            t.Tick += (_, __) => { if (WorkingSet() > TrimAboveBytes) Trim(); };
            t.Start();
            TrimSoon(); // logo após abrir: o que só a inicialização usou
            Settings.LookChanged += TrimSoon;
        }
        static void Trim()
        {
            if (Environment.TickCount64 - lastTrim < 2000) return;
            lastTrim = Environment.TickCount64;
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true); // compacta: o que sobrar livre pode ser devolvido
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(new IntPtr(-1) /*este processo*/, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        // Decide quais barras recebem player: fixo = todas as barras; solto = só a principal (listas reaproveitadas: roda 1x/s)
        readonly List<IntPtr> bars = new(), gone = new();
        EnumProc collectBars;
        void Sync()
        {
            var primary = FindWindow("Shell_TrayWnd", null);
            bars.Clear();
            if (primary != IntPtr.Zero) bars.Add(primary);
            if (!Settings.Floating)
            {
                collectBars ??= (h, _) => { if (ClassName(h) == "Shell_SecondaryTrayWnd") bars.Add(h); return true; };
                EnumWindows(collectBars, IntPtr.Zero);
            }
            gone.Clear();
            foreach (var h in players.Keys) if (!bars.Contains(h)) gone.Add(h);
            foreach (var h in gone) { players[h].Close(); players.Remove(h); }
            foreach (var h in bars) if (!players.ContainsKey(h)) { var w = new MainWindow(h, h == primary); players[h] = w; w.Show(); }
        }
    }
}
