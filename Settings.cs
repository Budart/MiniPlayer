using System;
using System.Globalization;
using System.IO;

namespace MiniPlayer
{
    // Opções do usuário: estado, regras e gravação em %AppData%\MiniPlayer.txt (formato antigo mantido: valores separados por ";")
    static class Settings
    {
        static readonly string File_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniPlayer.txt");

        public static bool Floating, ShowBattery = true, FloatBehind, HidePlayer, FloatVertical, FloatLocked, VolumeHintDone, ShowSystem;
        public static double FloatX = 200, FloatY = 200;
        public static int Theme, PlayerSize = 1;                           // tamanho: 0 pequeno, 1 médio, 2 grande
        public static readonly double[] SizeScales = { 0.85, 1.0, 1.2 };
        public static int SysItems = SysMetric.Ram | SysMetric.Cpu | SysMetric.Gpu; // itens do monitor (bits, ver SysMetric)

        public static event Action LookChanged;     // algo visível mudou (tema, tamanho, grupos, itens do monitor)
        public static event Action ModeChanged;     // fixo ↔ solto (quais barras recebem player)

        public static bool SysOn(int bit) => (SysItems & bit) != 0;
        // grupos visíveis (player, bateria, monitor): pelo menos um precisa ficar ligado
        public static int VisibleGroups => (HidePlayer ? 0 : 1) + (ShowBattery ? 1 : 0) + (ShowSystem ? 1 : 0);

        static void Changed() { Save(); LookChanged?.Invoke(); }
        public static void SetTheme(int i) { Theme = i; Changed(); }
        public static void SetSize(int i) { PlayerSize = Math.Clamp(i, 0, 2); Changed(); }
        public static void SetHidePlayer(bool on) { if (on && VisibleGroups <= 1) return; HidePlayer = on; Changed(); }
        public static void SetBattery(bool on) { if (!on && VisibleGroups <= 1) return; ShowBattery = on; Changed(); }
        public static void SetSystem(bool on) { if (!on && VisibleGroups <= 1) return; ShowSystem = on; Changed(); }
        public static void SetSysItem(int bit, bool on)
        {
            int next = on ? SysItems | bit : SysItems & ~bit;
            if (next == 0) return; // pelo menos um item do monitor
            SysItems = next; Changed();
        }
        public static void SetFloating(bool on, double x, double y)
        {
            Floating = on;
            if (on) { FloatX = x; FloatY = y; }
            Save(); ModeChanged?.Invoke();
        }
        // lidos continuamente pelo posicionamento: não precisam de aviso
        public static void SetBehind(bool on) { FloatBehind = on; Save(); }
        public static void SetFloatVertical(bool on) { FloatVertical = on; Save(); }
        public static void SetFloatLocked(bool on) { FloatLocked = on; Save(); }

        public static void Load()
        {
            try
            {
                var p = File.ReadAllText(File_).Split(';');
                bool B(int i, bool def) => p.Length > i ? p[i] == "1" : def;
                int I(int i, int def) => p.Length > i && int.TryParse(p[i], out var v) ? v : def;
                Floating = B(0, false);
                FloatX = double.Parse(p[1], CultureInfo.InvariantCulture); FloatY = double.Parse(p[2], CultureInfo.InvariantCulture);
                ShowBattery = B(3, true); FloatBehind = B(4, false); HidePlayer = B(5, false);
                Theme = Math.Clamp(I(6, 0), 0, Themes.All.Length - 1);
                FloatVertical = B(7, false); FloatLocked = B(8, false);
                PlayerSize = Math.Clamp(I(9, 1), 0, 2);
                VolumeHintDone = B(10, false); ShowSystem = B(11, false);
                if (I(12, 0) is var si && si > 0 && si <= SysMetric.AllBits) SysItems = si;
            }
            catch { }
        }

        public static void Save()
        {
            static int N(bool b) => b ? 1 : 0;
            try
            {
                File.WriteAllText(File_, string.Create(CultureInfo.InvariantCulture,
                    $"{N(Floating)};{FloatX};{FloatY};{N(ShowBattery)};{N(FloatBehind)};{N(HidePlayer)};{Theme};{N(FloatVertical)};{N(FloatLocked)};{PlayerSize};{N(VolumeHintDone)};{N(ShowSystem)};{SysItems}"));
            }
            catch { }
        }
    }

    // Temas: fundo, texto principal, texto secundário, cor fixa do espectro (null = cor da capa), Bg→Bg2 = degradê
    public record ThemeDef(string Name, string Bg, string Fg, string Fg2, string Spectrum, string Bg2 = null);
    static class Themes
    {
        public const int Light = 2, Dynamic = 10;
        public static readonly ThemeDef[] All =
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
            new("Dinâmico",              "#E6202020", "#FFFFFFFF", "#BBFFFFFF", null), // cores calculadas da capa (MainWindow.ApplyDynamic)
        };
        public static ThemeDef Current => All[Math.Clamp(Settings.Theme, 0, All.Length - 1)];
    }
}
