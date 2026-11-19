using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniPlayer
{
    // Lista de baterias publicada para a interface (só avisa quando algo mudou) + estimativa de tempo restante
    static class BatteryState
    {
        public static List<BtBattery> Items = new();
        public static event Action Changed;

        // histórico por aparelho (só em memória): base para estimar o tempo restante
        static readonly Dictionary<string, List<(DateTime t, int p)>> hist = new();

        public static void Refresh()
        {
            var l = Settings.ShowBattery ? BatteryReader.Snapshot() : new List<BtBattery>();
            Record(l);
            if (l.SequenceEqual(Items)) return;
            Items = l; Changed?.Invoke();
        }

        static void Record(List<BtBattery> l)
        {
            foreach (var b in l)
            {
                if (b.Percent < 0) continue;
                if (b.Charging) { hist.Remove(b.Name); continue; }
                if (!hist.TryGetValue(b.Name, out var h)) hist[b.Name] = h = new();
                if (h.Count > 0 && b.Percent > h[^1].p) h.Clear();                 // carregou: recomeça
                if (h.Count == 0 || h[^1].p != b.Percent) h.Add((DateTime.Now, b.Percent));
                if (h.Count > 60) h.RemoveAt(0);
            }
        }

        // null = ainda sem dados suficientes
        public static TimeSpan? Remaining(BtBattery b)
        {
            if (b.Charging || b.Percent < 0 || !hist.TryGetValue(b.Name, out var h) || h.Count < 2) return null;
            var (t0, p0) = h[0]; var (t1, p1) = h[^1];
            double drop = p0 - p1, mins = (t1 - t0).TotalMinutes;
            if (drop < 2 || mins < 5) return null;
            double left = b.Percent / (drop / mins) - (DateTime.Now - t1).TotalMinutes;
            return TimeSpan.FromMinutes(Math.Max(1, left));
        }

        // Dica ao passar o mouse: nome, %, carregando ou tempo restante estimado
        public static string Tip(BtBattery b)
        {
            if (b.Percent < 0) return $"{b.Name}: {L.S("nobatt")}";
            var tip = $"{b.Name}: {b.Percent}%";
            if (b.Charging) return tip + $" ({L.S("charging")})";
            var r = Remaining(b);
            if (r == null) return tip + $"\n{L.S("estimating")}";
            var tl = r.Value;
            string txt = tl.TotalHours >= 1 ? $"~{(int)tl.TotalHours} h {tl.Minutes:00} min" : $"~{Math.Max(1, (int)tl.TotalMinutes)} min";
            return tip + $"\n{txt} {L.S("left")}";
        }
    }
}
