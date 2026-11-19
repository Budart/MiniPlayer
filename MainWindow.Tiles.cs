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
        Thickness TileMargin => vertical ? new Thickness(0, 6, 0, 0) : new Thickness(5, 0, 0, 0);

        // separador sutil entre grupos (mídia | periféricos | monitor)
        Border GroupSeparator() => new()
        {
            Background = fg, Opacity = 0.3, Width = vertical ? 24 : 1, Height = vertical ? 1 : 24, Margin = TileMargin,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        System.Windows.Shapes.Path TileIcon(string kind, double thickness, double scale = 1) => new()
        {
            Data = IconGeometry(kind), Stroke = fg, StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 22 * scale, Height = 16 * scale, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true
        };
        static TextBlock TileText(string text, Brush color) => new() { Text = text, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = color };

        // ---- Monitor do sistema: ladrilhos criados uma vez (ao mudar opções/tema/layout); a cada 2 s só os valores mudam.
        // Mostra até 3 por vez; os demais aparecem rolando o mouse sobre eles (ou no carrossel automático).
        // Clique abre o Gerenciador de Tarefas; arrastar um item muda a ordem (salva).
        sealed class SysTile { public SysMetric Metric; public System.Windows.Shapes.Path Icon; public string IconKey; public TextBlock Value; public StackPanel View; }
        const int SysVisibleMax = 3;
        readonly System.Collections.Generic.List<SysTile> sysTiles = new();
        StackPanel sysHost;
        Border sysSep;
        int sysOffset, sysVisible = SysVisibleMax;
        DispatcherTimer carousel;
        const double SysTileW = 36;
        TextBlock sysPrev, sysNext; // setas: há itens escondidos daquele lado (clique ou rolagem mostram)

        void RebuildSystem()
        {
            SysPanel.Children.Clear(); sysTiles.Clear(); sysSep = null; sysHost = null; sysPrev = sysNext = null;
            InvalidateFit();
            SysPanel.Visibility = Settings.ShowSystem ? Visibility.Visible : Visibility.Collapsed;
            if (!Settings.ShowSystem) { UpdateCarousel(); return; }
            sysSep = GroupSeparator();
            sysHost = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center };
            sysHost.MouseWheel += (_, e) => { ScrollSys(e.Delta > 0 ? -1 : 1); e.Handled = true; }; // aqui a rolagem passa os itens (no resto do player é volume)
            sysPrev = SysArrow(-1); sysNext = SysArrow(1);
            SysPanel.Children.Add(sysSep);
            SysPanel.Children.Add(sysPrev);
            SysPanel.Children.Add(sysHost);
            SysPanel.Children.Add(sysNext);
            foreach (var m in SysMetric.Ordered())
            {
                if (!Settings.SysOn(m.Bit)) continue;
                var key = m.Icon();
                var tile = new SysTile { Metric = m, IconKey = key, Icon = TileIcon(key, 1.15, m.IconScale), Value = TileText("--", fg) };
                string name = L.S(m.Key);
                tile.View = new StackPanel
                {
                    Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = TileMargin, Width = SysTileW, Height = 30, // tamanho fixo e igual: o player não muda de largura ao rolar nem quando o número muda
                    Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name
                };
                tile.Icon.Margin = new Thickness(0, 16 * (1 - m.IconScale) / 2, 0, 16 * (1 - m.IconScale) / 2); // desenho menor, mesma altura
                tile.View.Children.Add(tile.Icon); tile.View.Children.Add(tile.Value);
                tile.View.ToolTipOpening += (_, __) => tile.View.ToolTip = $"{name}: {m.Detail() ?? L.S("na")}";
                HookDrag(tile);
                sysHost.Children.Add(tile.View);
                sysTiles.Add(tile);
            }
            sysVisible = SysVisibleFor(compact);
            sysOffset = Math.Clamp(sysOffset, 0, sysTiles.Count);
            ShowSysWindow(false);
            UpdateSeparator();
            UpdateSystemValues();
        }

        // mostra só os itens da "janela" atual (sysOffset … sysOffset + sysVisible)
        void ShowSysWindow(bool animate)
        {
            if (sysHost == null) return;
            int n = sysTiles.Count, vis = Math.Min(sysVisible, n);
            sysOffset = Math.Clamp(sysOffset, 0, n - vis);
            for (int i = 0; i < n; i++)
            {
                var v = sysTiles[i].View;
                bool show = i >= sysOffset && i < sysOffset + vis;
                var want = show ? Visibility.Visible : Visibility.Collapsed;
                if (v.Visibility == want) continue;
                v.Visibility = want;
                if (show && animate) v.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
            }
            UpdateSystemValues();
            // setas só existem se houver o que rolar; ocupam sempre o mesmo espaço (apagadas quando não há mais itens daquele lado)
            var arrows = n > vis ? Visibility.Visible : Visibility.Collapsed;
            sysPrev.Visibility = sysNext.Visibility = arrows;
            sysPrev.Opacity = sysOffset > 0 ? 0.75 : 0.15;
            sysNext.Opacity = sysOffset + vis < n ? 0.75 : 0.15;
            UpdateCarousel();
        }

        TextBlock SysArrow(int dir)
        {
            var a = new TextBlock
            {
                Text = vertical ? (dir < 0 ? "\uE70E" : "\uE70D") : (dir < 0 ? "\uE76B" : "\uE76C"), // chevrons ▲▼ / ◀▶
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 8, Foreground = fg, Cursor = Cursors.Hand,
                Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, Width = vertical ? 24 : 10, Margin = vertical ? new Thickness(0, 3, 0, 0) : new Thickness(3, 0, 0, 0)
            };
            a.MouseLeftButtonDown += (_, e) => { e.Handled = true; ScrollSys(dir); }; // não arrasta a janela solta
            a.MouseWheel += (_, e) => { ScrollSys(e.Delta > 0 ? -1 : 1); e.Handled = true; };
            return a;
        }

        void ScrollSys(int dir)
        {
            int before = sysOffset;
            sysOffset += dir;
            ShowSysWindow(true);
            if (sysOffset != before && carousel != null) { carousel.Stop(); carousel.Start(); } // rolou na mão: carrossel espera
        }

        // carrossel automático: só existe/roda se ligado e se houver itens escondidos; pausa com o mouse em cima ou arrastando
        void UpdateCarousel()
        {
            bool need = Settings.SysCarousel && sysHost != null && sysTiles.Count > sysVisible;
            if (!need) { carousel?.Stop(); return; }
            if (carousel == null)
            {
                carousel = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                carousel.Tick += (_, __) =>
                {
                    if (sysHost == null || sysHost.IsMouseOver || dragTile != null || !IsVisible) return;
                    sysOffset = sysOffset + sysVisible >= sysTiles.Count ? 0 : sysOffset + 1; // no fim, volta ao começo
                    ShowSysWindow(true);
                };
                Closed += (_, __) => carousel.Stop();
            }
            if (!carousel.IsEnabled) carousel.Start();
        }

        // ---- arrastar para reordenar (o item arrastado fica no lugar; os vizinhos é que trocam de posição) ----
        SysTile dragTile; Point dragStart; bool dragMoved; DateTime lastEdgeStep;
        void HookDrag(SysTile tile)
        {
            var v = tile.View;
            v.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true; // não arrasta a janela solta
                dragTile = tile; dragMoved = false; dragStart = e.GetPosition(sysHost);
                v.CaptureMouse();
            };
            v.MouseMove += (_, e) =>
            {
                if (dragTile != tile || !v.IsMouseCaptured) return;
                var p = e.GetPosition(sysHost);
                if (!dragMoved)
                {
                    if ((p - dragStart).Length < 5) return;
                    dragMoved = true; v.Opacity = 0.55;
                }
                DragTo(vertical ? p.Y : p.X);
            };
            v.MouseLeftButtonUp += (_, e) =>
            {
                if (dragTile != tile) return;
                e.Handled = true;
                bool moved = dragMoved;
                v.ReleaseMouseCapture(); // → EndDrag
                if (!moved) OpenTaskManager();
            };
            v.LostMouseCapture += (_, __) => EndDrag();
        }

        void DragTo(double pos)
        {
            int i = sysTiles.IndexOf(dragTile);
            double len = vertical ? sysHost.ActualHeight : sysHost.ActualWidth;
            // além da borda com itens escondidos: rola um item por vez, levando o arrastado junto
            if ((pos < 0 && i == sysOffset && sysOffset > 0) || (pos > len && i == sysOffset + Math.Min(sysVisible, sysTiles.Count) - 1 && i < sysTiles.Count - 1))
            {
                if ((DateTime.Now - lastEdgeStep).TotalMilliseconds < 400) return;
                lastEdgeStep = DateTime.Now;
                int dir = pos < 0 ? -1 : 1;
                SwapNeighbor(i, i + dir);
                sysOffset += dir;
                ShowSysWindow(false);
                return;
            }
            // dentro: troca com o vizinho cuja área o mouse alcançou (passo a passo)
            for (int guard = 0; guard < sysTiles.Count; guard++)
            {
                i = sysTiles.IndexOf(dragTile);
                var me = dragTile.View;
                double start = vertical ? me.TranslatePoint(new Point(), sysHost).Y : me.TranslatePoint(new Point(), sysHost).X;
                double size = vertical ? me.ActualHeight : me.ActualWidth;
                int dir = pos < start ? -1 : pos > start + size ? 1 : 0;
                int j = i + dir;
                if (dir == 0 || j < sysOffset || j >= sysOffset + sysVisible || j < 0 || j >= sysTiles.Count) return;
                var other = sysTiles[j].View;
                double oStart = vertical ? other.TranslatePoint(new Point(), sysHost).Y : other.TranslatePoint(new Point(), sysHost).X;
                double oMid = oStart + (vertical ? other.ActualHeight : other.ActualWidth) / 2;
                if (dir < 0 ? pos > oMid : pos < oMid) return; // só troca depois de passar da metade do vizinho
                SwapNeighbor(i, j);
                sysHost.UpdateLayout();
            }
        }

        // move o vizinho j para a posição i (o item arrastado continua na árvore e não perde o mouse)
        void SwapNeighbor(int i, int j)
        {
            var t = sysTiles[j];
            sysTiles.RemoveAt(j); sysTiles.Insert(i, t);
            sysHost.Children.Remove(t.View); sysHost.Children.Insert(i, t.View);
        }

        void EndDrag()
        {
            if (dragTile == null) return;
            dragTile.View.Opacity = 1;
            if (dragMoved)
            {
                // ordem nova: itens ligados na ordem da tela + os desligados na ordem que já tinham
                var order = new System.Collections.Generic.List<int>();
                foreach (var t in sysTiles) order.Add(t.Metric.Bit);
                foreach (var m in SysMetric.Ordered()) if (!order.Contains(m.Bit)) order.Add(m.Bit);
                Settings.SetSysOrder(order.ToArray());
            }
            dragTile = null; dragMoved = false;
        }

        void OnSysUpdated() => Dispatcher.BeginInvoke(new Action(UpdateSystemValues));
        void UpdateSystemValues()
        {
            foreach (var t in sysTiles)
            {
                if (t.View.Visibility != Visibility.Visible) continue; // escondido na rolagem: atualiza quando aparecer
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

        static void OpenTaskManager()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { }
        }

        // ---- Baterias: ícone + % de cada dispositivo (raio ao lado quando carregando) ----
        void UpdateBatteries() => Dispatcher.Invoke(() =>
        {
            BatteryPanel.Children.Clear();
            InvalidateFit();
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
