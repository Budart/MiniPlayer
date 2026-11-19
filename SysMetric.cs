using System;

namespace MiniPlayer
{
    // Itens do monitor do sistema: cada um diz seu ícone, texto, detalhe e alerta.
    // Para criar um item novo basta acrescentar uma linha em All (menu, interface e gravação usam esta lista).
    sealed class SysMetric
    {
        public const int Ram = 1, Cpu = 2, CpuTemp = 4, Gpu = 8, GpuTemp = 16, Net = 32, Ping = 64, AllBits = 127;

        public int Bit; public string Key;
        public Func<string> Icon;        // chave do desenho em Icons (pode mudar, ex.: Wi-Fi ↔ cabo)
        public Func<string> Text;        // valor curto embaixo do ícone
        public Func<string> Detail;      // dica ao passar o mouse; null = indisponível neste PC
        public Func<bool> Hot = () => false;
        public double IconWidth = 22;

        public static readonly SysMetric[] All =
        {
            Percent(Ram, "ram", "ram", () => SystemMonitor.Ram),
            Percent(Cpu, "cpu", "cpu", () => SystemMonitor.Cpu),
            Celsius(CpuTemp, "cputemp", () => SystemMonitor.CpuTemp),
            Percent(Gpu, "gpu", "gpu", () => SystemMonitor.Gpu),
            Celsius(GpuTemp, "gputemp", () => SystemMonitor.GpuTemp),
            new()
            {
                Bit = Net, Key = "net", IconWidth = 27,
                Icon = () => NetworkStatus.Kind switch { NetworkStatus.Link.Wifi => "net-wifi", NetworkStatus.Link.Wired => "net-wired", _ => "net" },
                Text = () => SystemMonitor.NetDown < 0 ? "--" : "↓" + Mbps(SystemMonitor.NetDown),
                Detail = () => SystemMonitor.NetDown < 0 ? null
                    : $"↓ {Mbps(SystemMonitor.NetDown)}/s   ↑ {Mbps(SystemMonitor.NetUp)}/s" + NetworkStatus.Kind switch
                      { NetworkStatus.Link.Wifi => "\nWi-Fi", NetworkStatus.Link.Wired => "\n" + L.S("wired"), _ => "" },
            },
            new()
            {
                Bit = Ping, Key = "ping", Icon = () => "ping",
                Text = () => SystemMonitor.PingMs < 0 ? "--" : SystemMonitor.PingMs + "ms",
                Detail = () => SystemMonitor.PingMs switch { -2 => L.S("noreply"), < 0 => "…", var v => $"{v} ms ({SystemMonitor.PingHost})" },
                Hot = () => SystemMonitor.PingMs >= 150,
            },
        };

        // itens na ordem escolhida pelo usuário (os que não estão na ordem salva vão ao fim, na ordem padrão)
        public static System.Collections.Generic.IEnumerable<SysMetric> Ordered()
        {
            foreach (var bit in Settings.SysOrder) foreach (var m in All) if (m.Bit == bit) yield return m;
            foreach (var m in All) if (Array.IndexOf(Settings.SysOrder, m.Bit) < 0) yield return m;
        }

        static SysMetric Percent(int bit, string key, string icon, Func<int> v) => new()
        {
            Bit = bit, Key = key, Icon = () => icon,
            Text = () => v() < 0 ? "--" : v() + "%",
            Detail = () => v() < 0 ? null : v() + "%",
            Hot = () => v() >= 90,                                         // alerta: uso ≥ 90%
        };
        static SysMetric Celsius(int bit, string key, Func<int> v) => new()
        {
            Bit = bit, Key = key, Icon = () => "temp",
            Text = () => v() < 0 ? "--" : v() + "°C",
            Detail = () => v() < 0 ? null : v() + "°C",
            Hot = () => v() >= 85,                                         // alerta: ≥ 85 °C
        };

        // velocidade em megabits/s (como os planos de internet): 0,8 Mb · 54 Mb · 1,2 Gb
        static string Mbps(double bytesPerSec)
        {
            double mb = bytesPerSec * 8 / 1_000_000;
            return mb >= 1000 ? (mb / 1000).ToString(mb >= 10000 ? "0" : "0.0") + " Gb" : mb >= 10 ? mb.ToString("0") + " Mb" : mb.ToString("0.0") + " Mb";
        }
    }
}
