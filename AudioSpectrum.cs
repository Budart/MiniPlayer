using System;
using System.Threading.Tasks;
using NAudio.Dsp;
using NAudio.Wave;

namespace MiniPlayer
{
    // Espectro de áudio: captura o que está tocando no PC (loopback) e divide em faixas de frequência.
    // Buffers fixos (sem alocação por quadro); reinicia sozinho se o dispositivo de som mudar.
    static class AudioSpectrum
    {
        public const int Bands = 14;
        public static readonly float[] Levels = new float[Bands];
        public static DateTime LastAudio;

        const int N = 1024, Log2N = 10;
        static readonly float[] ring = new float[N];
        static readonly Complex[] fft = new Complex[N];
        static readonly float[] window = BuildWindow();
        static WasapiLoopbackCapture cap;
        static int rp;
        static volatile bool stopped;

        static float[] BuildWindow() { var w = new float[N]; for (int i = 0; i < N; i++) w[i] = (float)FastFourierTransform.HannWindow(i, N); return w; }

        public static void Start()
        {
            if (stopped) return;
            try
            {
                cap = new WasapiLoopbackCapture();
                var fmt = cap.WaveFormat;
                if (fmt.Encoding != WaveFormatEncoding.IeeeFloat) return;
                int ch = fmt.Channels, sr = fmt.SampleRate;
                cap.DataAvailable += (s, e) =>
                {
                    var wb = new WaveBuffer(e.Buffer);
                    int n = e.BytesRecorded / 4;
                    for (int i = 0; i + ch - 1 < n; i += ch)
                    {
                        float v = 0; for (int c = 0; c < ch; c++) v += wb.FloatBuffer[i + c];
                        ring[rp++] = v / ch;
                        if (rp == N) { rp = 0; Analyze(sr); }
                    }
                };
                cap.RecordingStopped += (s, e) => { try { cap.Dispose(); } catch { } if (!stopped) Task.Delay(2000).ContinueWith(_ => Start()); };
                cap.StartRecording();
            }
            catch { }
        }

        public static void Stop() { stopped = true; try { cap?.StopRecording(); } catch { } }

        static void Analyze(int sr)
        {
            for (int i = 0; i < N; i++) { fft[i].X = ring[i] * window[i]; fft[i].Y = 0; }
            FastFourierTransform.FFT(true, Log2N, fft);
            const double fMin = 50, fMax = 14000;
            for (int b = 0; b < Bands; b++)
            {
                double f0 = fMin * Math.Pow(fMax / fMin, (double)b / Bands), f1 = fMin * Math.Pow(fMax / fMin, (double)(b + 1) / Bands);
                int i0 = Math.Max(1, (int)(f0 * N / sr)), i1 = Math.Max(i0 + 1, (int)(f1 * N / sr));
                double m = 0;
                for (int i = i0; i < i1 && i < N / 2; i++) m = Math.Max(m, fft[i].X * fft[i].X + fft[i].Y * fft[i].Y);
                Levels[b] = (float)Math.Clamp((10 * Math.Log10(m + 1e-18) + 80) / 60, 0, 1); // 10·log10(m²) = 20·log10(m): sem raiz quadrada
            }
            LastAudio = DateTime.Now;
        }
    }
}
