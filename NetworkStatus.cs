using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MiniPlayer
{
    // Tipo da conexão em uso (Wi-Fi ou cabo). Sem consultas periódicas: só recalcula quando o Windows avisa que a rede mudou.
    static class NetworkStatus
    {
        public enum Link { None, Wifi, Wired, Other }
        public static Link Kind { get; private set; }

        [DllImport("iphlpapi.dll")] static extern int GetBestInterface(uint destAddr, out uint index);
        static volatile bool dirty = true;
        static bool hooked;

        // chamado pelo monitor (timer em segundo plano) só enquanto o item de rede estiver ligado
        public static void Poll()
        {
            if (!hooked)
            {
                hooked = true;
                NetworkChange.NetworkAddressChanged += (_, __) => dirty = true;
                NetworkChange.NetworkAvailabilityChanged += (_, __) => dirty = true;
            }
            if (!dirty) return;
            dirty = false;
            Kind = Detect();
        }

        static Link Detect()
        {
            try
            {
                // placa que o Windows usa para chegar à internet (rota até 1.1.1.1; nada é enviado)
                if (GetBestInterface(0x01010101, out uint idx) != 0) return Link.None;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    int? i = null;
                    try { i = ni.GetIPProperties().GetIPv4Properties()?.Index; } catch { }
                    if (i != idx) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return Link.Wifi;
                    var d = ni.Description.ToLowerInvariant();
                    if (new[] { "wi-fi", "wifi", "wireless", "wlan", "802.11" }.Any(d.Contains)) return Link.Wifi;
                    return ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                        or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit
                        ? Link.Wired : Link.Other;
                }
            }
            catch { }
            return Link.None;
        }
    }
}
