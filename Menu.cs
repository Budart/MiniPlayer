using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniPlayer
{
    // Menu único (mesmo conteúdo no botão direito do player e no ícone da bandeja).
    // Regra de texto: cada opção liga/desliga diz o que acontece quando está MARCADA (✓ = ativado);
    // escolhas exclusivas (posição, tema) mostram ✓ só na opção atual.
    public sealed class MenuNode
    {
        public string Text;
        public Func<bool> Checked;          // null = não é marcável
        public Func<bool> Enabled;          // null = sempre habilitado
        public Action Click;
        public List<MenuNode> Children;     // não-nulo = submenu
        public bool Separator;
        public static MenuNode Sep => new() { Separator = true };
    }

    static class AppMenu
    {
        // from: player onde o menu foi aberto (para soltar no mesmo lugar); null = bandeja
        public static List<MenuNode> Build(MainWindow from)
        {
            void Float(bool on)
            {
                var w = from ?? Application_FirstPlayer();
                Settings.SetFloating(on, w?.Left ?? 200, (w?.Top ?? 260) - 60);
            }

            var temas = new List<MenuNode>();
            for (int i = 0; i < Themes.All.Length; i++)
            {
                int idx = i;
                temas.Add(new() { Text = L.S("t" + i), Checked = () => Settings.Theme == idx, Click = () => Settings.SetTheme(idx) });
            }

            return new()
            {
                // marcado = fixo na barra; desmarcado = solto na tela (arrastável)
                new() { Text = L.S("fixed"), Checked = () => !Settings.Floating, Click = () => Float(!Settings.Floating) },
                new() { Text = L.S("lock"), Checked = () => Settings.FloatLocked, Enabled = () => Settings.Floating, Click = () => Settings.SetFloatLocked(!Settings.FloatLocked) },
                new() { Text = L.S("pos"), Enabled = () => Settings.Floating, Children = new()
                {
                    new() { Text = L.S("vert"),  Checked = () => Settings.FloatVertical,  Enabled = () => Settings.Floating, Click = () => Settings.SetFloatVertical(true) },
                    new() { Text = L.S("horiz"), Checked = () => !Settings.FloatVertical, Enabled = () => Settings.Floating, Click = () => Settings.SetFloatVertical(false) },
                    MenuNode.Sep,
                    new() { Text = L.S("behind"), Checked = () => Settings.FloatBehind, Enabled = () => Settings.Floating, Click = () => Settings.SetBehind(!Settings.FloatBehind) },
                }},
                MenuNode.Sep,
                new() { Text = L.S("show"), Children = new()
                {
                    // um dos dois sempre fica visível: o que estiver sozinho não pode ser desmarcado
                    new() { Text = L.S("media"), Checked = () => !Settings.HidePlayer, Enabled = () => Settings.HidePlayer || Settings.VisibleGroups > 1, Click = () => Settings.SetHidePlayer(!Settings.HidePlayer) },
                    new() { Text = L.S("battery"), Checked = () => Settings.ShowBattery, Enabled = () => !Settings.ShowBattery || Settings.VisibleGroups > 1, Click = () => Settings.SetBattery(!Settings.ShowBattery) },
                    new() { Text = L.S("system"), Children = new List<MenuNode>
                    {
                        new() { Text = L.S("sysshow"), Checked = () => Settings.ShowSystem, Enabled = () => !Settings.ShowSystem || Settings.VisibleGroups > 1, Click = () => Settings.SetSystem(!Settings.ShowSystem) },
                        new() { Text = L.S("carousel"), Checked = () => Settings.SysCarousel, Enabled = () => Settings.ShowSystem, Click = () => Settings.SetCarousel(!Settings.SysCarousel) },
                        MenuNode.Sep,
                    }.Concat(SysMetric.All.Select(SysItem)).ToList() },
                }},
                new() { Text = L.S("theme"), Children = temas },
                new() { Text = L.S("size"), Children = new()
                {
                    new() { Text = L.S("small"),  Checked = () => Settings.PlayerSize == 0, Click = () => Settings.SetSize(0) },
                    new() { Text = L.S("medium"), Checked = () => Settings.PlayerSize == 1, Click = () => Settings.SetSize(1) },
                    new() { Text = L.S("large"),  Checked = () => Settings.PlayerSize == 2, Click = () => Settings.SetSize(2) },
                }},
                MenuNode.Sep,
                new() { Text = L.S("startup"), Checked = () => StartupEntry.Enabled, Click = () => StartupEntry.Set(!StartupEntry.Enabled) },
                MenuNode.Sep,
                new() { Text = L.S("quit"), Click = App.Quit },
            };
        }

        // item do monitor: só pode desmarcar se sobrar outro marcado
        static MenuNode SysItem(SysMetric m) => new()
        {
            Text = L.S(m.Key), Checked = () => Settings.SysOn(m.Bit),
            Enabled = () => Settings.ShowSystem && (!Settings.SysOn(m.Bit) || (Settings.SysItems & ~m.Bit) != 0),
            Click = () => Settings.SetSysItem(m.Bit, !Settings.SysOn(m.Bit))
        };

        static MainWindow Application_FirstPlayer() => System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

        // ---- WPF (menu do player) ----
        public static System.Windows.Controls.ContextMenu ToWpf(List<MenuNode> nodes)
        {
            var m = new System.Windows.Controls.ContextMenu();
            foreach (var n in nodes) m.Items.Add(WpfItem(n));
            return m;
        }
        static object WpfItem(MenuNode n)
        {
            if (n.Separator) return new System.Windows.Controls.Separator();
            var i = new System.Windows.Controls.MenuItem { Header = n.Text, IsCheckable = false, IsChecked = n.Checked?.Invoke() ?? false, IsEnabled = n.Enabled?.Invoke() ?? true };
            if (n.Children != null) foreach (var c in n.Children) i.Items.Add(WpfItem(c));
            else if (n.Click != null) i.Click += (_, __) => n.Click();
            return i;
        }

        // ---- WinForms (menu da bandeja): reconstruído a cada abertura, sempre com o estado atual ----
        public static void FillTray(System.Windows.Forms.ToolStripItemCollection items, List<MenuNode> nodes)
        {
            items.Clear();
            foreach (var n in nodes)
            {
                if (n.Separator) { items.Add(new System.Windows.Forms.ToolStripSeparator()); continue; }
                var i = new System.Windows.Forms.ToolStripMenuItem(n.Text) { Checked = n.Checked?.Invoke() ?? false, Enabled = n.Enabled?.Invoke() ?? true };
                if (n.Children != null) FillTray(i.DropDownItems, n.Children);
                else if (n.Click != null) i.Click += (_, __) => n.Click();
                items.Add(i);
            }
        }
    }
}
