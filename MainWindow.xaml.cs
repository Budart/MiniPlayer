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
    // Player de uma barra de tarefas. Partes (arquivos MainWindow.*.cs):
    //  Placement = posição, camada e layout · Look = temas e espectro · Tiles = baterias e monitor · Media = mídia, volume, progresso e título
    public partial class MainWindow : Window
    {
        readonly IntPtr bar; readonly bool isPrimary;
        Mgr mgr; GlobalSystemMediaTransportControlsSession session;
        bool dragging, pending;

        public MainWindow(IntPtr taskbar, bool primary)
        {
            bar = taskbar; isPrimary = primary;
            InitializeComponent();
            SetTitle(L.S("nothing"));
            // antes de exibir: verifica a orientação da barra e já monta o layout certo (vertical/horizontal)
            if (!Settings.Floating && GetWindowRect(bar, out var br) && (br.B - br.T) > (br.R - br.L))
            {
                double dpi = GetDpiForWindow(bar); double sc = dpi > 0 ? dpi / 96.0 : 1;
                SetVertical(true, (br.R - br.L) / sc);
                Left = br.L / sc; Top = br.T / sc + 8;
            }
            Loaded += async (_, __) => { Setup(); SetupSpectrum(); SetupExtras(); ApplyLook(); await InitMedia(); };
            // avisos dos serviços (todos desligados ao fechar: a janela pode ser recriada ao trocar fixo ↔ solto)
            BatteryState.Changed += UpdateBatteries;
            BatteryState.Changed += UpdateSeparator;
            SystemMonitor.Updated += OnSysUpdated;
            Settings.LookChanged += ApplyLook;
            Closed += (_, __) =>
            {
                BatteryState.Changed -= UpdateBatteries;
                BatteryState.Changed -= UpdateSeparator;
                SystemMonitor.Updated -= OnSysUpdated;
                Settings.LookChanged -= ApplyLook;
                UnhookMouse(); // nunca deixa o gancho de mouse para trás
            };
            Root.MouseRightButtonUp += (_, __) => ShowMenu();
            MouseEnter += (_, __) => ClearTaskbarHover();
            MouseMove += (_, __) => ClearTaskbarHover();
            Root.MouseLeftButtonDown += (s, e) =>
            {
                if (!Settings.Floating || Settings.FloatLocked || e.OriginalSource is Button) return; // travado: não arrasta
                dragging = true; DragMove(); dragging = false; KeepInsideMonitor(); Settings.FloatX = Left; Settings.FloatY = Top; Settings.Save();
            };
        }

        // todos os players abertos (um por barra de tarefas)
        static System.Collections.Generic.IEnumerable<MainWindow> Players => Application.Current.Windows.OfType<MainWindow>();

        // Menu do botão direito: as mesmas opções do ícone da bandeja (ações centralizadas no App)
        void ShowMenu()
        {
            var m = AppMenu.ToWpf(AppMenu.Build(this));
            m.Opened += (_, __) => { menuOpen = true; HookMouse(); };
            m.Closed += (_, __) => { menuOpen = false; UnhookMouse(); };
            openMenu = m; menuOpen = true; m.IsOpen = true;
        }
    }
}
