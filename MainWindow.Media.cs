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
    // Mídia do Windows: fonte, título/capa, controles, progresso, volume e letreiro
    public partial class MainWindow
    {
        // ======== Barra de progresso, volume pela rolagem, letreiro e dicas de bateria ========
        bool isPlaying;

        void SetupExtras()
        {
            // progresso: atualiza 2x por segundo, só se estiver visível
            var pt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            pt.Tick += (_, __) => UpdateProgress();
            pt.Start();
            Closed += (_, __) => pt.Stop();
            Progress.MouseLeftButtonDown += async (_, e) =>
            {
                e.Handled = true; // não inicia o arraste do player solto
                try
                {
                    var tl = session?.GetTimelineProperties(); if (tl == null) return;
                    double r = Math.Clamp(e.GetPosition(Progress).X / Math.Max(1, Progress.ActualWidth), 0, 1);
                    var target = tl.StartTime + TimeSpan.FromTicks((long)((tl.EndTime - tl.StartTime).Ticks * r));
                    await session.TryChangePlaybackPositionAsync(target.Ticks);
                }
                catch { }
            };
            // volume: rolagem do mouse em qualquer parte do player
            debounce.Tick += (_, __) => { debounce.Stop(); _ = Refresh(true); };
            Root.MouseWheel += (_, e) => { ChangeVolume(e.Delta > 0 ? 1 : -1); e.Handled = true; };
            // sobre o título/artista e no botão ⇅: a rolagem troca a mídia (com 2+ abertas); na capa e nos botões continua sendo volume
            MouseWheelEventHandler switchWheel = (_, e) =>
            {
                if (!multiMedia) return;
                e.Handled = true;
                SwitchSource(e.Delta < 0 ? 1 : -1); // rolar para baixo = próxima (entra por baixo)
            };
            InfoPanel.MouseWheel += switchWheel;
            SwapBtn.MouseWheel += switchWheel;
            if (isPrimary)
            {
                var at = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                at.Tick += (_, __) => AutoPickSource();
                at.Start();
                Closed += (_, __) => at.Stop();
            }
            volTimer.Tick += (_, __) => { volTimer.Stop(); if (!ArtBox.IsMouseOver) VolBadge.Visibility = Visibility.Collapsed; else VolHint.Visibility = Visibility.Visible; };
            // passar o mouse na capa mostra o volume atual com ▲▼, convidando a rolar
            ArtBox.MouseEnter += (_, __) => { VolHint.Visibility = Visibility.Visible; ChangeVolume(0); };
            ArtBox.MouseLeave += (_, __) => { if (!volTimer.IsEnabled) VolBadge.Visibility = Visibility.Collapsed; };
            // dica de primeiro uso: até a pessoa usar a rolagem uma vez
            if (!Settings.VolumeHintDone) Root.ToolTip = L.S("volume");
            InfoPanel.MouseEnter += (_, __) => StartMarquee();
            InfoPanel.MouseLeave += (_, __) => ResetTitle();
        }

        void UpdateProgress()
        {
            try
            {
                if (session == null || Settings.HidePlayer || !IsVisible || !ShowProgress) { Progress.Visibility = Visibility.Collapsed; return; }
                var tl = session.GetTimelineProperties();
                double dur = (tl.EndTime - tl.StartTime).TotalSeconds;
                if (dur < 1) { Progress.Visibility = Visibility.Collapsed; return; } // sem duração (ex.: ao vivo)
                double pos = (tl.Position - tl.StartTime).TotalSeconds;
                if (isPlaying) pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds; // o Windows só informa a posição de tempos em tempos
                Progress.Visibility = Visibility.Visible;
                ProgFill.Width = Math.Max(0, Progress.ActualWidth * Math.Clamp(pos / dur, 0, 1));
            }
            catch { Progress.Visibility = Visibility.Collapsed; }
        }

        // Volume do app que está tocando (ou geral, se o app não for encontrado no mixer)
        readonly DispatcherTimer volTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
        void ChangeVolume(int dir)
        {
            if (AudioMixer.Volume(session, dir) is not int pct) return;
            VolText.Text = pct.ToString();
            VolIcon.Text = pct == 0 ? "\uE74F" : pct < 34 ? "\uE993" : pct < 67 ? "\uE994" : "\uE995"; // mudo / baixo / médio / alto
            VolBadge.Visibility = Visibility.Visible;
            if (dir == 0) return; // só mostrando (mouse sobre a capa)
            VolHint.Visibility = Visibility.Collapsed;
            volTimer.Stop(); volTimer.Start();
            if (!Settings.VolumeHintDone) { Settings.VolumeHintDone = true; Settings.Save(); foreach (var w in Players) w.Root.ClearValue(ToolTipProperty); }
        }

        // Título: largura fixa com "…"; ao passar o mouse, se não couber, rola em loop infinito para a esquerda
        double InfoW => InfoPanel.Width;
        // campo de texto: 128 px; com o botão ⇅ visível (2+ mídias), os 28 px da direita são dele — a largura do player não muda
        void UpdateInfoWidth()
        {
            bool swap = multiMedia && !vertical;
            double w = swap ? 100 : 128;
            if (InfoPanel.Width == w) return;
            InfoPanel.Width = w;
            InfoPanel.Margin = new Thickness(8, 0, swap ? 36 : 8, 0);
            ResetTitle();
        }
        string curTitle = "";
        readonly TranslateTransform titleShift = new();
        void SetTitle(string t) { if (t == curTitle && TitleText.Text.Length > 0) return; curTitle = t ?? ""; ResetTitle(); if (InfoPanel.IsMouseOver) StartMarquee(); }
        void ResetTitle()
        {
            titleShift.BeginAnimation(TranslateTransform.XProperty, null); titleShift.X = 0;
            TitleText.RenderTransform = titleShift;
            TitleText.Text = curTitle; TitleText.Width = InfoW; TitleText.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        double TextWidth(string t)
        {
            var probe = new TextBlock { Text = t, FontSize = TitleText.FontSize, FontWeight = TitleText.FontWeight, FontFamily = TitleText.FontFamily };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        }
        void StartMarquee()
        {
            if (InfoPanel.Visibility != Visibility.Visible || TextWidth(curTitle) <= InfoW) return;
            const string gap = "      •      ";
            double cycle = TextWidth(curTitle + gap);                  // distância até a 2ª cópia ocupar o lugar da 1ª
            TitleText.TextTrimming = TextTrimming.None; TitleText.ClearValue(WidthProperty);
            TitleText.Text = curTitle + gap + curTitle;                 // duas cópias: o loop fica contínuo, sem "pulo"
            var a = new DoubleAnimation(0, -cycle, TimeSpan.FromSeconds(cycle / 60)) { RepeatBehavior = RepeatBehavior.Forever }; // ~60 px/s
            titleShift.BeginAnimation(TranslateTransform.XProperty, a);
        }

        async Task InitMedia()
        {
            // logo após ligar o PC o serviço de mídia do Windows pode ainda não estar pronto: tenta de novo em vez de falhar
            for (int tries = 0; ; tries++)
            {
                try
                {
                    mgr = await Mgr.RequestAsync();
                    mgr.CurrentSessionChanged += (_, __) => Dispatcher.Invoke(Hook);
                    mgr.SessionsChanged += (_, __) => Dispatcher.Invoke(Hook);
                    Hook();
                    return;
                }
                catch { if (tries >= 30) return; await Task.Delay(2000); }
            }
        }

        // Fonte escolhida pelo usuário (vale para os players de todos os monitores). null = automático.
        static string pinnedSource;
        // Fonte detectada automaticamente: a mídia que está de fato tocando/emitindo som
        static string autoSource;

        // A cada 1,5 s (só o player do monitor principal faz a conta, e só com 2+ mídias abertas):
        // 1) a que está em "tocando"; 2) se houver várias, a que está realmente saindo som no mixer do Windows
        void AutoPickSource()
        {
            try
            {
                if (mgr == null || pinnedSource != null) return;
                var all = mgr.GetSessions().ToArray();
                if (all.Length < 2) { autoSource = null; return; }
                var playing = all.Where(x => { try { return x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; } }).ToArray();
                GlobalSystemMediaTransportControlsSession pick = null;
                if (playing.Length == 1) pick = playing[0];
                else if (playing.Length > 1)
                {
                    var peaks = AudioMixer.Peaks();
                    float best = 0.001f;
                    foreach (var x in playing) { float p = AudioMixer.AppPeak(x, peaks); if (p > best) { best = p; pick = x; } }
                }
                if (pick == null || pick.SourceAppUserModelId == session?.SourceAppUserModelId) return; // nada tocando: mantém o atual
                autoSource = pick.SourceAppUserModelId;
                foreach (var w in Players) w.Hook();
            }
            catch { }
        }

        void Hook()
        {
            if (mgr == null) return;
            if (session != null) { session.MediaPropertiesChanged -= OnMedia; session.PlaybackInfoChanged -= OnPlay; }
            GlobalSystemMediaTransportControlsSession[] all;
            try { all = mgr.GetSessions().ToArray(); } catch { all = Array.Empty<GlobalSystemMediaTransportControlsSession>(); }
            var pinned = pinnedSource == null ? null : all.FirstOrDefault(x => x.SourceAppUserModelId == pinnedSource);
            if (pinned == null) pinnedSource = null; // a fonte escolhida fechou: volta ao automático
            var auto = autoSource == null ? null : all.FirstOrDefault(x => x.SourceAppUserModelId == autoSource);
            session = pinned ?? auto ?? mgr.GetCurrentSession();
            if (session != null) { session.MediaPropertiesChanged += OnMedia; session.PlaybackInfoChanged += OnPlay; }
            // botão de troca só aparece com mais de uma fonte
            multiMedia = all.Length > 1;
            SwapBtn.Visibility = multiMedia && !Settings.HidePlayer ? Visibility.Visible : Visibility.Collapsed;
            SwapBtn.ToolTip = $"{L.S("source")} ({SourceName(session)})";
            UpdateInfoWidth();
            refreshing = Refresh();
        }

        void Swap_Click(object s, RoutedEventArgs e) => SwitchSource(1);

        // Troca a mídia (dir: +1 próxima, -1 anterior) em todos os players, com a animação de "roleta"
        bool multiMedia;
        Task refreshing = Task.CompletedTask;
        void SwitchSource(int dir)
        {
            try
            {
                if (sliding) return; // uma troca por vez (rolagem rápida não empilha animações)
                var all = mgr.GetSessions().ToArray();
                if (all.Length < 2) return;
                int i = Array.FindIndex(all, x => x.SourceAppUserModelId == session?.SourceAppUserModelId);
                pinnedSource = all[((i + dir) % all.Length + all.Length) % all.Length].SourceAppUserModelId;
                foreach (var w in Players) { w.immediateUntil = DateTime.Now.AddSeconds(1); _ = w.SlideSwitch(dir); }
            }
            catch { }
        }

        // ---- Animação: a mídia atual sai por cima e a nova entra por baixo (dir = -1: o contrário) ----
        // Só mexe em transformações (placa de vídeo), dura ~¼ s e não guarda nada depois.
        readonly TranslateTransform artSlide = new(), infoSlide = new();
        bool sliding, slideReady;
        async Task SlideSwitch(int dir)
        {
            if (!slideReady)
            {
                slideReady = true;
                Art.RenderTransform = NoArt.RenderTransform = artSlide;               // dentro do recorte da capa
                TitleHost.RenderTransform = ArtistText.RenderTransform = infoSlide;   // dentro do campo de texto (recortado)
            }
            sliding = true;
            try
            {
                double ah = ArtBox.ActualHeight, ih = InfoPanel.ActualHeight > 0 ? InfoPanel.ActualHeight : 40;
                await Task.WhenAll(Slide(artSlide, 0, -dir * ah, 110, EasingMode.EaseIn), Slide(infoSlide, 0, -dir * ih, 110, EasingMode.EaseIn));
                Hook();                                                         // troca de verdade
                await Task.WhenAny(refreshing, Task.Delay(300));                // espera o título/capa novos (no máximo 0,3 s)
                await Task.WhenAll(Slide(artSlide, dir * ah, 0, 170, EasingMode.EaseOut), Slide(infoSlide, dir * ih, 0, 170, EasingMode.EaseOut));
            }
            finally
            {
                artSlide.BeginAnimation(TranslateTransform.YProperty, null); infoSlide.BeginAnimation(TranslateTransform.YProperty, null);
                artSlide.Y = infoSlide.Y = 0;
                sliding = false;
            }
        }
        static Task Slide(TranslateTransform t, double from, double to, int ms, EasingMode ease)
        {
            var done = new TaskCompletionSource<bool>();
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase { EasingMode = ease } };
            a.Completed += (_, __) => done.TrySetResult(true);
            t.BeginAnimation(TranslateTransform.YProperty, a);
            return done.Task;
        }

        void OnMedia(object s, object e) => Dispatcher.Invoke(() => _ = Refresh());
        void OnPlay(object s, object e) => Dispatcher.Invoke(() => _ = Refresh());

        // Ignora trocas rápidas de mídia (ex.: prévia ao passar o mouse numa miniatura do YouTube):
        // título/capa novos só aparecem se continuarem os mesmos por 2 s. Ações do próprio usuário (avançar,
        // voltar, trocar fonte) e a primeira mídia aparecem na hora.
        string shownKey, pendingKey;
        DateTime immediateUntil;
        readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromSeconds(2) };

        async Task Refresh(bool fromDebounce = false)
        {
            if (session == null) { shownKey = pendingKey = null; isPlaying = false; Progress.Visibility = Visibility.Collapsed; SetTitle(L.S("nothing")); ArtistText.Text = ""; Art.Source = null; NoArt.Visibility = Visibility.Visible; SetPlayIcon(false); return; }
            try
            {
                var p = await session.TryGetMediaPropertiesAsync();
                var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                isPlaying = playing;
                UpdateProgress();
                SetPlayIcon(playing);
                string baseKey = (p.Title ?? "") + "\n" + (p.Artist ?? "");
                string key = baseKey + (p.Thumbnail != null ? "\n+capa" : "");
                if (key == shownKey) { pendingKey = null; return; } // mesma mídia (ex.: só pausou/voltou ao original)
                bool sameSong = shownKey != null && shownKey.StartsWith(baseKey + "\n") || shownKey == baseKey; // só chegou a capa/dados da mesma música
                bool stable = fromDebounce && key == pendingKey;
                // navegadores (prévias do YouTube ao passar o mouse): só aceita se estiver tocando e saindo som
                if (stable && IsBrowser(session) && !(playing && AppPeak(session) > 0.005f)) { debounce.Stop(); debounce.Start(); return; }
                bool now = shownKey == null || sameSong || DateTime.Now < immediateUntil || stable;
                if (!now)
                {
                    if (key != pendingKey) { pendingKey = key; debounce.Stop(); debounce.Start(); } // espera 2 s estável
                    return;
                }
                shownKey = key; pendingKey = null; debounce.Stop();
                SetTitle(string.IsNullOrEmpty(p.Title) ? L.S("untitled") : p.Title);
                ArtistText.Text = p.Artist;
                Art.Source = null; NoArt.Visibility = Visibility.Visible; accent.Color = Colors.White; accentDeep.Color = Color.FromRgb(0x30, 0x34, 0x3C);
                BitmapSource cover = null;
                if (p.Thumbnail != null)
                {
                    using var ws = await p.Thumbnail.OpenReadAsync();
                    using var st = ws.AsStreamForRead();
                    using var ms = new MemoryStream(); await st.CopyToAsync(ms); ms.Position = 0;
                    var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 160; /* nítido até ~500% de escala */ bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                    // navegador sem capa manda o próprio ícone (Chrome/Edge): descarta
                    cover = IsBrowser(session) && IsAppIcon(bmp) ? null : bmp;
                }
                // sem capa: imagem padrão do streaming (Netflix, Prime Video...) ou, em último caso, a nota musical
                cover ??= ServiceCover(session?.SourceAppUserModelId, p.Title, p.Artist, p.AlbumTitle);
                if (cover != null)
                {
                    Art.Source = cover; NoArt.Visibility = Visibility.Collapsed;
                    (var c1, accent2) = AccentPair(cover); accent.Color = c1;
                    accentDeep.Color = Color.FromRgb((byte)(c1.R * 0.55), (byte)(c1.G * 0.55), (byte)(c1.B * 0.55));
                }
                if (Settings.Theme == Themes.Dynamic) ApplyDynamic(true);
            }
            catch { }
        }

        // textos que dependem do idioma (o resto é montado na hora, já no idioma escolhido)
        void ApplyTexts()
        {
            if (session == null) SetTitle(L.S("nothing"));
            if (multiMedia) SwapBtn.ToolTip = $"{L.S("source")} ({SourceName(session)})";
            if (!Settings.VolumeHintDone) Root.ToolTip = L.S("volume");
        }

        // ícones de mídia desenhados (mesmo traço dos demais ícones do app)
        static readonly Geometry PlayGeo = Geometry.Parse("M5,3 L13,8 L5,13 Z"), PauseGeo = Geometry.Parse("M4.5,3 H6 A1,1 0 0 1 7,4 V12 A1,1 0 0 1 6,13 H4.5 A1,1 0 0 1 3.5,12 V4 A1,1 0 0 1 4.5,3 Z M10,3 H11.5 A1,1 0 0 1 12.5,4 V12 A1,1 0 0 1 11.5,13 H10 A1,1 0 0 1 9,12 V4 A1,1 0 0 1 10,3 Z");
        void SetPlayIcon(bool playing) { var g = playing ? PauseGeo : PlayGeo; if (PlayIcon.Data != g) PlayIcon.Data = g; }

        async void Prev_Click(object s, RoutedEventArgs e) { immediateUntil = DateTime.Now.AddSeconds(5); if (session != null) await session.TrySkipPreviousAsync(); }
        async void Play_Click(object s, RoutedEventArgs e) { if (session != null) await session.TryTogglePlayPauseAsync(); }
        async void Next_Click(object s, RoutedEventArgs e) { immediateUntil = DateTime.Now.AddSeconds(5); if (session != null) await session.TrySkipNextAsync(); }
    }
}
