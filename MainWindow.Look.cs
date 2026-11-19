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
    // Temas (fixos e dinâmico) e espectro de áudio
    public partial class MainWindow
    {
        // Tema e "ocultar player"
        Brush fg = Brushes.White;
        // degradê no sentido do comprimento do player (horizontal: esquerda→direita; vertical: cima→baixo)
        LinearGradientBrush Grad(string a, string b)
        {
            var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), vertical ? 90 : 0);
            g.Freeze(); return g;
        }
        readonly SolidColorBrush accentDeep = new(Color.FromRgb(0x30, 0x34, 0x3C));
        static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        void ApplyLook()
        {
            var th = Themes.Current;
            bool dyn = Settings.Theme == Themes.Dynamic;
            fg = dyn ? dynFg : B(th.Fg);
            Root.Background = dyn ? DynBg : th.Bg2 == null ? B(th.Bg) : Grad(th.Bg, th.Bg2);
            // temas com fundo: leve recuo (3 px) para o fundo parecer "dentro" da barra, sem encostar nas bordas dela
            Root.Margin = Settings.Theme == 0 ? new Thickness(0) : new Thickness(3);
            TitleText.Foreground = fg; ArtistText.Foreground = dyn ? dynFg2 : B(th.Fg2); NoArt.Foreground = fg;
            PrevBtn.Foreground = PlayBtn.Foreground = NextBtn.Foreground = SwapBtn.Foreground = fg; SwapIcon.Stroke = fg;
            ProgFill.Background = ProgTrack.Background = fg;
            SizeScale.ScaleX = SizeScale.ScaleY = Settings.SizeScales[Settings.PlayerSize];
            RebuildSystem();
            // tema Claro: espectro na cor da capa escurecida e mais opaco (no fundo claro a cor original fica apagada)
            bool light = Settings.Theme == Themes.Light;
            Spectrum.Opacity = light ? 0.6 : 0.28;
            Brush spec = th.Spectrum != null ? B(th.Spectrum) : light ? accentDeep : accent; // um pincel para todas as barras
            foreach (Rectangle r in Spectrum.Children) r.Fill = spec;
            ApplyVisibility();
            InvalidateFit();
            UpdateBatteries();
            if (dyn) ApplyDynamic(false);
        }

        // Partes da mídia visíveis: ocultar player, barra lateral (sem textos) e modo compacto (sem título / sem capa)
        void ApplyVisibility()
        {
            bool media = !Settings.HidePlayer;
            static Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
            Controls.Visibility = Spectrum.Visibility = V(media);
            ArtBox.Visibility = V(media && compact < 4);
            InfoPanel.Visibility = V(media && !vertical && compact < 3);
        }

        // ---- Tema dinâmico: usa a cor da capa (já calculada para o espectro, numa miniatura de 16x16).
        // Custo: 3 pincéis reaproveitados e uma transição curta de cor na troca de faixa — sem memória extra relevante.
        // fundo em degradê: 2 cores da capa, no sentido do comprimento do player (horizontal ou vertical)
        readonly GradientStop dynStop1 = new(Color.FromArgb(0xE6, 0x20, 0x20, 0x20), 0), dynStop2 = new(Color.FromArgb(0xE6, 0x20, 0x20, 0x20), 1);
        LinearGradientBrush dynBgGrad;
        LinearGradientBrush DynBg
        {
            get
            {
                dynBgGrad ??= new LinearGradientBrush { GradientStops = { dynStop1, dynStop2 } };
                dynBgGrad.StartPoint = new Point(0, 0); dynBgGrad.EndPoint = vertical ? new Point(0, 1) : new Point(1, 0);
                return dynBgGrad;
            }
        }
        Color accent2 = Colors.White;
        readonly SolidColorBrush dynFg = new(Colors.White), dynFg2 = new(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF));
        Color lastDyn = Color.FromArgb(0, 0, 0, 1);
        void ApplyDynamic(bool animate)
        {
            var a = accent.Color;
            if (animate && a == lastDyn) return; // mesma capa (ex.: só pausou): nada a fazer
            lastDyn = a;
            bool none = a == Colors.White; // sem capa: cinza neutro
            // fundo: cor da capa escurecida (legível com texto claro), quase opaca
            Color Dark(Color c) => Color.FromArgb(0xE6, (byte)(c.R * 0.32), (byte)(c.G * 0.32), (byte)(c.B * 0.32));
            Color bg1 = none ? Color.FromArgb(0xE6, 0x22, 0x24, 0x28) : Dark(a);
            Color bg2 = none ? Color.FromArgb(0xE6, 0x16, 0x17, 0x1A) : Dark(accent2);
            // texto: branco levemente tingido; secundário: a própria cor da capa clareada
            Color f1 = none ? Colors.White : Mix(a, Colors.White, 0.85);
            Color f2 = none ? Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF) : WithA(Mix(a, Colors.White, 0.45), 0xDD);
            Anim(dynStop1, GradientStop.ColorProperty, bg1); Anim(dynStop2, GradientStop.ColorProperty, bg2);
            Anim(dynFg, SolidColorBrush.ColorProperty, f1); Anim(dynFg2, SolidColorBrush.ColorProperty, f2);
            void Anim(Animatable o, DependencyProperty p, Color c)
            {
                if (!animate) { o.BeginAnimation(p, null); o.SetValue(p, c); return; }
                o.BeginAnimation(p, new ColorAnimation(c, TimeSpan.FromMilliseconds(450)));
            }
        }
        static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        static Color WithA(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        // Barras do espectro atrás dos botões, na cor predominante da capa
        readonly SolidColorBrush accent = new(Colors.White);
        readonly float[] shown = new float[AudioSpectrum.Bands];
        void SetupSpectrum()
        {
            // barras escaladas por transformação (não recalcula layout a cada quadro)
            var scales = new ScaleTransform[AudioSpectrum.Bands];
            for (int i = 0; i < AudioSpectrum.Bands; i++)
            {
                scales[i] = new ScaleTransform(1, 0);
                Spectrum.Children.Add(new Rectangle { Fill = Themes.Current.Spectrum != null ? B(Themes.Current.Spectrum) : accent, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1, RadiusY = 1,
                    RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scales[i] });
            }
            if (vertical) for (int i = 1; i < Spectrum.Children.Count; i += 2) Spectrum.Children[i].Visibility = Visibility.Collapsed;
            bool idle = true;
            var t = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            t.Tick += (_, __) =>
            {
                if (!IsVisible) return;
                bool live = (DateTime.Now - AudioSpectrum.LastAudio).TotalMilliseconds < 200;
                if (!live && idle) return; // silêncio: não redesenha nada
                bool any = false;
                for (int i = 0; i < AudioSpectrum.Bands; i++)
                {
                    float target = live ? AudioSpectrum.Levels[i] : 0;
                    shown[i] = target > shown[i] ? target : shown[i] * 0.85f; // sobe rápido, desce suave
                    if (shown[i] < 0.005f) shown[i] = 0; else any = true;
                    scales[i].ScaleY = shown[i];
                }
                idle = !live && !any;
            };
            t.Start();
            Closed += (_, __) => t.Stop();
        }
    }
}
