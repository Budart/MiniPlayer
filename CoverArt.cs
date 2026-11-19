using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniPlayer
{
    // Análise e geração de capas: ícone x capa real, capas padrão de streamings, cores principais para espectro/tema
    static class CoverArt
    {
        // Ícone de programa (fundo transparente) x capa de verdade (opaca)
        public static bool IsAppIcon(BitmapSource src)
        {
            try
            {
                var small = new FormatConvertedBitmap(new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight)), PixelFormats.Bgra32, null, 0);
                var px = new byte[small.PixelWidth * small.PixelHeight * 4]; small.CopyPixels(px, small.PixelWidth * 4, 0);
                int transp = 0; for (int i = 3; i < px.Length; i += 4) if (px[i] < 200) transp++;
                return transp > px.Length / 4 / 10; // >10% transparente = ícone
            }
            catch { return false; }
        }

        // Capas padrão para streamings que não enviam capa (arte genérica: nome do serviço sobre degradê; sem logotipos oficiais)
        static readonly (string[] keys, string name, string c1, string c2)[] Services =
        {
            (new[] { "netflix" }, "NETFLIX", "#E50914", "#2A0204"),
            (new[] { "primevideo", "prime video", "amazonvideo" }, "Prime Video", "#1A98FF", "#0F171E"),
            (new[] { "disney" }, "Disney+", "#1F4FD8", "#040714"),
            (new[] { "hbomax", "hbo", " max" }, "Max", "#3B2BFF", "#0A0A2A"),
            (new[] { "globoplay" }, "Globoplay", "#FB3A3A", "#2A0808"),
            (new[] { "crunchyroll" }, "Crunchyroll", "#F47521", "#2A1405"),
            (new[] { "paramount" }, "Paramount+", "#0064FF", "#00102E"),
            (new[] { "appletv", "apple tv" }, "Apple TV", "#5A5A5F", "#0B0B0D"),
            (new[] { "twitch" }, "Twitch", "#9146FF", "#18082E"),
            (new[] { "youtube" }, "YouTube", "#FF2020", "#2A0000"),
        };
        static readonly System.Collections.Generic.Dictionary<string, BitmapSource> serviceArt = new();
        public static BitmapSource ServiceCover(params string[] texts)
        {
            var all = string.Join(" | ", texts.Where(t => !string.IsNullOrEmpty(t))).ToLowerInvariant();
            foreach (var sv in Services)
            {
                if (!sv.keys.Any(k => all.Contains(k))) continue;
                if (serviceArt.TryGetValue(sv.name, out var cached)) return cached;
                const int S = 96;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var bg = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(sv.c1), (Color)ColorConverter.ConvertFromString(sv.c2), 60);
                    dc.DrawRectangle(bg, null, new Rect(0, 0, S, S));
                    var ft = new FormattedText(sv.name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), sv.name.Length > 8 ? 13 : 16, Brushes.White, 1.0)
                        { MaxTextWidth = S - 10, TextAlignment = TextAlignment.Center };
                    dc.DrawText(ft, new Point(5, (S - ft.Height) / 2));
                }
                var rtb = new RenderTargetBitmap(S, S, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv); rtb.Freeze();
                serviceArt[sv.name] = rtb;
                return rtb;
            }
            return null;
        }

        // Cor principal (para espectro/tema) e uma 2ª cor (lado oposto da capa) para o degradê
        public static (Color main, Color second) AccentPair(BitmapSource src)
        {
            try
            {
                var small = new FormatConvertedBitmap(new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight)), PixelFormats.Bgra32, null, 0);
                int w = small.PixelWidth, h = small.PixelHeight;
                var px = new byte[w * h * 4]; small.CopyPixels(px, w * 4, 0);
                var sum = new double[3, 4]; // [todos, esquerda, direita] x [r,g,b,peso]
                for (int i = 0; i < px.Length; i += 4)
                {
                    int B = px[i], G = px[i + 1], R = px[i + 2], x = (i / 4) % w;
                    int mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    double sat = mx == 0 ? 0 : (mx - mn) / (double)mx, k = 0.02 + sat * sat * (mx / 255.0);
                    int side = x < w / 2 ? 1 : 2;
                    sum[0, 0] += R * k; sum[0, 1] += G * k; sum[0, 2] += B * k; sum[0, 3] += k;
                    sum[side, 0] += R * k; sum[side, 1] += G * k; sum[side, 2] += B * k; sum[side, 3] += k;
                }
                Color Of(int part)
                {
                    double r = sum[part, 0] / sum[part, 3], g = sum[part, 1] / sum[part, 3], b = sum[part, 2] / sum[part, 3];
                    double max = Math.Max(r, Math.Max(g, b)), f = max < 1 ? 1 : 255 / max; // clareia até o máximo
                    return Color.FromRgb((byte)(r * f), (byte)(g * f), (byte)(b * f));
                }
                Color main = Of(0), l = Of(1), rr = Of(2);
                Color second = Distance(l, main) > Distance(rr, main) ? l : rr;
                if (Distance(second, main) < 40) second = Color.FromRgb((byte)(main.R * 0.55), (byte)(main.G * 0.55), (byte)(main.B * 0.55)); // capa de uma cor só: tom mais escuro
                return (main, second);
            }
            catch { return (Colors.White, Colors.White); }
        }
        static double Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }
}
