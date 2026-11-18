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
                App.SetFloating(on, w?.Left ?? 200, (w?.Top ?? 260) - 60);
            }

            var temas = new List<MenuNode>();
            for (int i = 0; i < App.Themes.Length; i++)
            {
                int idx = i;
                temas.Add(new() { Text = L.S("t" + i), Checked = () => App.Theme == idx, Click = () => App.SetTheme(idx) });
            }

            return new()
            {
                new() { Text = L.S("pos"), Children = new()
                {
                    new() { Text = L.S("fixed"), Checked = () => !App.Floating, Click = () => Float(false) },
                    new() { Text = L.S("float"), Checked = () => App.Floating,  Click = () => Float(true) },
                    MenuNode.Sep,
                    new() { Text = L.S("vert"),     Checked = () => App.FloatVertical, Enabled = () => App.Floating, Click = () => App.SetFloatVertical(!App.FloatVertical) },
                    new() { Text = L.S("lock"),     Checked = () => App.FloatLocked,   Enabled = () => App.Floating, Click = () => App.SetFloatLocked(!App.FloatLocked) },
                    new() { Text = L.S("behind"),   Checked = () => App.FloatBehind,   Enabled = () => App.Floating, Click = () => App.SetBehind(!App.FloatBehind) },
                }},
                new() { Text = L.S("show"), Children = new()
                {
                    // um dos dois sempre fica visível: o que estiver sozinho não pode ser desmarcado
                    new() { Text = L.S("media"), Checked = () => !App.HidePlayer, Enabled = () => App.HidePlayer || App.ShowBattery, Click = () => App.SetHidePlayer(!App.HidePlayer) },
                    new() { Text = L.S("battery"), Checked = () => App.ShowBattery, Enabled = () => !App.ShowBattery || !App.HidePlayer, Click = () => App.SetBattery(!App.ShowBattery) },
                }},
                new() { Text = L.S("theme"), Children = temas },
                MenuNode.Sep,
                new() { Text = L.S("startup"), Checked = () => App.StartsWithWindows, Click = () => App.SetStartup(!App.StartsWithWindows) },
                MenuNode.Sep,
                new() { Text = L.S("quit"), Click = App.Quit },
            };
        }

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
