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
    // Ladrilhos de bateria dos periféricos e do monitor do sistema
    public partial class MainWindow
    {
        // ======== Ladrilhos (ícone + valor) das baterias e do monitor do sistema ========
        Thickness TileMargin => vertical ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);

        // separador sutil entre grupos (mídia | periféricos | monitor)
        Border GroupSeparator() => new()
        {
            Background = fg, Opacity = 0.3, Width = vertical ? 24 : 1, Height = vertical ? 1 : 24, Margin = TileMargin,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        System.Windows.Shapes.Path TileIcon(string kind, double thickness, double width = 22) => new()
        {
            Data = IconGeometry(kind), Stroke = fg, StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = width, Height = 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true
        };
        static TextBlock TileText(string text, Brush color) => new() { Text = text, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = color };

        // ---- Monitor do sistema: ladrilhos criados uma vez (ao mudar opções/tema/layout); a cada 2 s só os valores mudam ----
        sealed class SysTile { public SysMetric Metric; public System.Windows.Shapes.Path Icon; public string IconKey; public TextBlock Value; }
        readonly System.Collections.Generic.List<SysTile> sysTiles = new();
        Border sysSep;

        void RebuildSystem()
        {
            SysPanel.Children.Clear(); sysTiles.Clear(); sysSep = null;
            SysPanel.Visibility = Settings.ShowSystem ? Visibility.Visible : Visibility.Collapsed;
            if (!Settings.ShowSystem) return;
            sysSep = GroupSeparator();
            SysPanel.Children.Add(sysSep);
            foreach (var m in SysMetric.All)
            {
                if (!Settings.SysOn(m.Bit)) continue;
                var key = m.Icon();
                var tile = new SysTile { Metric = m, IconKey = key, Icon = TileIcon(key, 1.15, m.IconWidth), Value = TileText("--", fg) };
                string name = L.S(m.Key);
                var col = new StackPanel
                {
                    Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = TileMargin, Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = SysTag, ToolTip = name
                };
                col.Children.Add(tile.Icon); col.Children.Add(tile.Value);
                col.ToolTipOpening += (_, __) => col.ToolTip = $"{name}: {m.Detail() ?? L.S("na")}";
                // clique abre o Gerenciador de Tarefas (solto e destravado: decide depois do arraste — clique sem mover = abrir)
                col.PreviewMouseLeftButtonDown += (_, e) => { if (Settings.Floating && !Settings.FloatLocked) return; e.Handled = true; OpenTaskManager(); };
                SysPanel.Children.Add(col);
                sysTiles.Add(tile);
            }
            UpdateSeparator();
            UpdateSystemValues();
        }

        void OnSysUpdated() => Dispatcher.BeginInvoke(new Action(UpdateSystemValues));
        void UpdateSystemValues()
        {
            foreach (var t in sysTiles)
            {
                var key = t.Metric.Icon();
                if (key != t.IconKey) { t.IconKey = key; t.Icon.Data = IconGeometry(key); } // ex.: trocou de Wi-Fi para cabo
                var text = t.Metric.Text();
                if (t.Value.Text != text) t.Value.Text = text;
                var want = t.Metric.Hot() ? Brushes.OrangeRed : fg;
                if (t.Value.Foreground != want) t.Value.Foreground = want;
            }
        }

        // separador do monitor só aparece se houver algo antes dele
        void UpdateSeparator() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (sysSep == null) return;
            bool before = !Settings.HidePlayer || (Settings.ShowBattery && BatteryPanel.Children.Count > 0);
            sysSep.Visibility = before ? Visibility.Visible : Visibility.Collapsed;
        }));

        const string SysTag = "sys";
        static void OpenTaskManager()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { }
        }
        static bool IsSysTile(object src)
        {
            for (var d = src as DependencyObject; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is FrameworkElement fe && SysTag.Equals(fe.Tag)) return true;
            return false;
        }

        // ---- Baterias: ícone + % de cada dispositivo (raio ao lado quando carregando) ----
        void UpdateBatteries() => Dispatcher.Invoke(() =>
        {
            BatteryPanel.Children.Clear();
            if (!Settings.ShowBattery) return;
            if (!Settings.HidePlayer && BatteryState.Items.Count > 0) BatteryPanel.Children.Add(GroupSeparator()); // mídia | periféricos
            foreach (var b in BatteryState.Items)
            {
                var col = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
                col.Children.Add(TileIcon(b.Glyph, 1.4));
                col.Children.Add(TileText(b.Percent < 0 ? "--" : b.Percent + "%", b.Percent >= 0 && b.Percent <= 20 && !b.Charging ? Brushes.OrangeRed : fg));
                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Margin = TileMargin, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, ToolTip = b.Name
                };
                var bb = b;
                sp.ToolTipOpening += (_, __) => sp.ToolTip = BatteryState.Tip(bb);
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
    }
}
