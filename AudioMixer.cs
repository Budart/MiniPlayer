using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace MiniPlayer
{
    // Mixer de volume do Windows: nível de som e volume por programa, ligados às fontes de mídia (Spotify, navegadores...)
    static class AudioMixer
    {
        static readonly MMDeviceEnumerator devices = new();
        static readonly Dictionary<uint, string> procNames = new();   // pid → nome do processo (cache; limpo se crescer)
        static readonly List<(string pn, float v)> peaks = new();     // reaproveitada a cada leitura
        static readonly string[] Browsers = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "arc", "308046b0af4a39cb" };

        // Nome curto do app de uma fonte de mídia ("Spotify.exe" / "SpotifyAB.SpotifyMusic_xyz!Spotify" → "spotify")
        public static string SourceName(GlobalSystemMediaTransportControlsSession s)
        {
            var id = s?.SourceAppUserModelId ?? "";
            id = id.Split('!')[0];
            if (id.Contains('_')) id = id.Split('_')[0];
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
            int dot = id.LastIndexOf('.'); if (dot >= 0 && dot < id.Length - 1) id = id[(dot + 1)..];
            return id;
        }
        static string AppKey(GlobalSystemMediaTransportControlsSession s)
        {
            var app = SourceName(s).ToLowerInvariant();
            return app.Contains("308046b0af4a39cb") ? "firefox" : app; // id do Firefox no Windows
        }
        static bool Matches(string pn, string app) => pn.Length > 1 && app.Length > 1 && (pn.Contains(app) || app.Contains(pn));

        public static bool IsBrowser(GlobalSystemMediaTransportControlsSession s)
        {
            var n = SourceName(s).ToLowerInvariant();
            foreach (var b in Browsers) if (n.Contains(b)) return true;
            return false;
        }

        static string ProcName(uint pid)
        {
            if (procNames.TryGetValue(pid, out var pn)) return pn;
            if (procNames.Count > 256) procNames.Clear();
            try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { pn = ""; }
            return procNames[pid] = pn;
        }

        // Nível de som atual de cada programa no mixer do Windows (lista reaproveitada; usar antes da próxima chamada)
        public static List<(string pn, float v)> Peaks()
        {
            peaks.Clear();
            try
            {
                using var dev = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var list = dev.AudioSessionManager.Sessions;
                for (int i = 0; i < list.Count; i++)
                {
                    var sc = list[i];
                    peaks.Add((ProcName(sc.GetProcessID), sc.AudioMeterInformation.MasterPeakValue));
                }
            }
            catch { }
            return peaks;
        }

        public static float AppPeak(GlobalSystemMediaTransportControlsSession s, List<(string pn, float v)> levels = null)
        {
            var app = AppKey(s); float m = 0;
            foreach (var (pn, v) in levels ?? Peaks()) if (Matches(pn, app)) m = Math.Max(m, v);
            return m;
        }

        // Muda (dir = ±1, passos de 2%) ou só lê (dir = 0) o volume do app da fonte; sem o app no mixer, usa o volume geral.
        // Retorna o volume em %, ou null se não houver dispositivo de som.
        public static int? Volume(GlobalSystemMediaTransportControlsSession s, int dir)
        {
            try
            {
                using var dev = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                string app = AppKey(s);
                float? v = null;
                var list = dev.AudioSessionManager.Sessions;
                for (int i = 0; i < list.Count; i++)
                {
                    var sc = list[i];
                    if (!Matches(ProcName(sc.GetProcessID), app)) continue;
                    var nv = Math.Clamp(sc.SimpleAudioVolume.Volume + dir * 0.02f, 0f, 1f);
                    if (dir != 0) sc.SimpleAudioVolume.Volume = nv;
                    v = Math.Max(v ?? 0, nv);
                }
                if (v == null)
                {
                    var ev = dev.AudioEndpointVolume;
                    v = Math.Clamp(ev.MasterVolumeLevelScalar + dir * 0.02f, 0f, 1f);
                    if (dir != 0) ev.MasterVolumeLevelScalar = v.Value;
                }
                return (int)Math.Round(v.Value * 100);
            }
            catch { return null; }
        }
    }
}
