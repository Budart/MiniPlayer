using System;
using System.Windows.Media;

namespace MiniPlayer
{
    // Desenhos vetoriais (24x24) de dispositivos e do monitor do sistema, convertidos uma única vez e compartilhados
    static class Icons
    {
        // Desenhos vetoriais dos dispositivos (24x24); cada controle com o seu formato
        const string Body = "M7,6 C4.8,6 3.4,7.6 2.9,10 L1.6,16.6 C1.3,18.6 2.5,20 4,20 C5.2,20 6,19.2 6.8,18 L8.2,16 H15.8 L17.2,18 C18,19.2 18.8,20 20,20 C21.5,20 22.7,18.6 22.4,16.6 L21.1,10 C20.6,7.6 19.2,6 17,6 Z ";
        static string Circ(double x, double y, double r) => FormattableString.Invariant($"M{x - r},{y} a{r},{r} 0 1 0 {2 * r},0 a{r},{r} 0 1 0 {-2 * r},0 ");
        // joystick genérico: corpo com pegadores, direcional, 4 botões e 2 analógicos
        static readonly string Pad = Body + "M4.8,11 H7.8 M6.3,9.5 V12.5 " + Circ(17.3, 9.6, 0.55) + Circ(18.9, 11.1, 0.55) + Circ(15.7, 11.1, 0.55) + Circ(17.3, 12.6, 0.55) + Circ(9.4, 14.2, 1.3) + Circ(14.6, 14.2, 1.3);
        const string Arrows = "M5,4 V18 M2,15 L5,18 L8,15 M12,18 V4 M9,7 L12,4 L15,7 ";
        static readonly System.Collections.Generic.Dictionary<string, string> Paths = new()
        {
            ["pad"] = Pad, ["ds5"] = Pad, ["ds4"] = Pad, ["xbox"] = Pad,
            ["head"] = "M4,15 V12 A8,8 0 0 1 20,12 V15 M3,14 H6.5 V20 H4.5 A1.5,1.5 0 0 1 3,18.5 Z M21,14 H17.5 V20 H19.5 A1.5,1.5 0 0 0 21,18.5 Z",
            ["buds"] = Circ(7, 9, 3) + "M8.5,11.6 V19 H6.5 V11.9 " + Circ(17, 9, 3) + "M15.5,11.6 V19 H17.5 V11.9",
            ["spk"] = "M6,3 H18 A1.5,1.5 0 0 1 19.5,4.5 V19.5 A1.5,1.5 0 0 1 18,21 H6 A1.5,1.5 0 0 1 4.5,19.5 V4.5 A1.5,1.5 0 0 1 6,3 Z " + Circ(12, 14.5, 3.5) + Circ(12, 7.5, 1.3),
            ["kbd"] = "M3,6 H21 A1.5,1.5 0 0 1 22.5,7.5 V16.5 A1.5,1.5 0 0 1 21,18 H3 A1.5,1.5 0 0 1 1.5,16.5 V7.5 A1.5,1.5 0 0 1 3,6 Z M5,9.5 H6 M8,9.5 H9 M11,9.5 H12 M14,9.5 H15 M17,9.5 H19 M5,12.5 H6.5 M9,12.5 H10 M12,12.5 H13 M15.5,12.5 H19 M7.5,15 H16.5",
            ["mouse"] = "M12,2.5 C8,2.5 6,5.5 6,9 V15 C6,18.8 8.6,21.5 12,21.5 C15.4,21.5 18,18.8 18,15 V9 C18,5.5 16,2.5 12,2.5 Z M6,10 H18 M12,2.5 V10 M12,5 V7.5",
            // monitor do sistema: traço fino, desenhos próximos das peças reais
            ["ram"] = "M8,2.5 H16 V21.5 H8 Z M10.3,5.5 H13.7 V8.5 H10.3 Z M10.3,10.5 H13.7 V13.5 H10.3 Z M10.3,15.5 H13.7 V18.5 H10.3 Z", // pente de memória na vertical
            ["cpu"] = "M7.5,6 H16.5 A1.5,1.5 0 0 1 18,7.5 V16.5 A1.5,1.5 0 0 1 16.5,18 H7.5 A1.5,1.5 0 0 1 6,16.5 V7.5 A1.5,1.5 0 0 1 7.5,6 Z M9.5,9.5 H14.5 V14.5 H9.5 Z M9,3.6 V6 M12,3.6 V6 M15,3.6 V6 M9,18 V20.4 M12,18 V20.4 M15,18 V20.4 M3.6,9 H6 M3.6,12 H6 M3.6,15 H6 M18,9 H20.4 M18,12 H20.4 M18,15 H20.4",
            ["gpu"] = "M2.5,5.5 V19.5 M4,7 H21 A1,1 0 0 1 22,8 V15 A1,1 0 0 1 21,16 H4 Z M7,16 V18 H15 V16 " + Circ(9, 11.5, 2.9) + Circ(16.5, 11.5, 2.9) + Circ(9, 11.5, 0.7) + Circ(16.5, 11.5, 0.7),
            ["temp"] = "M10.5,4.8 A1.6,1.6 0 0 1 13.7,4.8 V13.9 A3.3,3.3 0 1 1 10.5,13.9 Z M12.1,8 V15.8 M15.8,6 H17.4 M15.8,8.6 H17.4 M15.8,11.2 H17.4",
            ["net"] = "M8,4 V18 M4.5,14.5 L8,18 L11.5,14.5 M16,20 V6 M12.5,9.5 L16,6 L19.5,9.5", // ↓ download / ↑ upload
            // rede + tipo de conexão colado à direita das setas: Wi-Fi (ondas) ou cabo (porta RJ45)
            ["net-wifi"] = Arrows + "M18.55,14.05 A7,7 0 0 1 28.45,14.05 M20.25,15.75 A4.6,4.6 0 0 1 26.75,15.75 M21.94,17.44 A2.2,2.2 0 0 1 25.06,17.44 " + Circ(23.5, 19.2, 0.45),
            ["net-wired"] = Arrows + "M18,11 H29 V17.5 H26.5 V20 H20.5 V17.5 H18 Z M20.3,13.2 V15 M22.4,13.2 V15 M24.6,13.2 V15 M26.7,13.2 V15",
            ["ping"] = "M4,17 A8,8 0 1 1 20,17 M12,15 L16.5,9.5 " + Circ(12, 15, 1.2), // velocímetro (latência)
            ["bt"] = "M7,7.5 L17,16.5 L12,21 V3 L17,7.5 L7,16.5",
        };

        // Desenhos convertidos uma única vez e compartilhados (sem recriar a cada atualização)
        static readonly System.Collections.Generic.Dictionary<string, Geometry> geoCache = new();
        public static readonly Geometry BoltGeometry = Frozen("M6,0 L1,7 H4.5 L3,12 L9,4.5 H5.5 Z");
        static Geometry Frozen(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }
        public static Geometry IconGeometry(string kind)
        {
            if (!Paths.ContainsKey(kind)) kind = "bt";
            if (!geoCache.TryGetValue(kind, out var g)) geoCache[kind] = g = Frozen(Paths[kind]);
            return g;
        }
    }
}
