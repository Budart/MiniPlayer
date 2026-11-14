using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HidSharp;
using Windows.Devices.Enumeration;

namespace MiniPlayer
{
    public record BtBattery(string Name, int Percent, string Glyph, bool Charging = false);

    // Lê bateria de dispositivos Bluetooth: o que o Windows informa (fones, Xbox, teclados...)
    // + leitura direta de controles PlayStation (DualSense / DualShock 4), que o Windows não informa
    static class BatteryReader
    {
        const string BattKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
        const string ContainerKey = "System.Devices.ContainerId";

        public static async Task<List<BtBattery>> ReadAll()
        {
            var res = new List<BtBattery>();
            var seen = new HashSet<string>();
            try
            {
                var devs = await DeviceInformation.FindAllAsync("", new[] { BattKey, ContainerKey }, DeviceInformationKind.Device);
                foreach (var d in devs)
                {
                    if (!d.Properties.TryGetValue(BattKey, out var b) || b == null) continue;
                    int pct = Convert.ToInt32(b);
                    var cid = d.Properties.TryGetValue(ContainerKey, out var c) && c != null ? c.ToString() : d.Id;
                    if (!seen.Add(cid)) continue;
                    string name = d.Name;
                    try
                    {
                        var ct = await DeviceInformation.CreateFromIdAsync("{" + cid + "}", new[] { "System.Devices.Connected" }, DeviceInformationKind.DeviceContainer);
                        if (ct.Properties.TryGetValue("System.Devices.Connected", out var con) && con is bool on && !on) continue;
                        if (!string.IsNullOrEmpty(ct.Name)) name = ct.Name;
                    }
                    catch { }
                    res.Add(new BtBattery(name, Math.Clamp(pct, 0, 100), Glyph(name)));
                }
            }
            catch { }
            try { res.AddRange(ReadPlayStation()); } catch { }
            return res;
        }

        static IEnumerable<BtBattery> ReadPlayStation()
        {
            var list = new List<(BtBattery b, bool bt)>();
            foreach (var dev in DeviceList.Local.GetHidDevices(0x054C))
            {
                int pid = dev.ProductID;
                bool ds5 = pid == 0x0CE6 || pid == 0x0DF2, ds4 = pid == 0x05C4 || pid == 0x09CC;
                if (!ds5 && !ds4) continue;
                try
                {
                    int max = dev.GetMaxInputReportLength();
                    bool bt = max > 64;
                    if (!dev.TryOpen(out var s)) continue;
                    using (s)
                    {
                        s.ReadTimeout = 1000;
                        if (bt) try { var f = new byte[ds5 ? 41 : 37]; f[0] = (byte)(ds5 ? 0x05 : 0x02); s.GetFeature(f); } catch { } // ativa relatório completo
                        var buf = new byte[max];
                        for (int i = 0; i < 20; i++)
                        {
                            int n = s.Read(buf, 0, buf.Length), idx = -1;
                            if (ds5) idx = buf[0] == 0x31 ? 54 : (buf[0] == 0x01 && !bt ? 53 : -1);
                            else idx = buf[0] == 0x11 ? 32 : (buf[0] == 0x01 && !bt ? 30 : -1);
                            if (idx < 0 || idx >= n) continue;
                            int pct = Math.Min(100, (buf[idx] & 0x0F) * 10);
                            list.Add(new BtBattery(ds5 ? "DualSense" : "DualShock 4", pct, ""));
                            break;
                        }
                    }
                }
                catch { }
            }
            // mesmo controle no Bluetooth e no cabo aparece duas vezes: para cada um via USB, descarta um via Bluetooth do mesmo modelo
            foreach (var usb in list.Where(x => !x.bt).ToList())
            {
                var dup = list.FirstOrDefault(x => x.bt && x.b.Name == usb.b.Name);
                if (dup.b != null) list.Remove(dup);
            }
            return list.Select(x => x.b);
        }

        // Ícone pelo nome do dispositivo
        static string Glyph(string n)
        {
            n = (n ?? "").ToLowerInvariant();
            if (new[] { "controller", "dualsense", "dualshock", "xbox", "gamepad", "playstation", "joy-con", "8bitdo" }.Any(n.Contains)) return "";
            if (new[] { "buds", "head", "fone", "airpods", "ear", "wh-", "wf-", "jbl", "headset", "tws", "soundcore", "audio", "qc", "beats" }.Any(n.Contains)) return "";
            if (new[] { "speaker", "caixa", "soundlink" }.Any(n.Contains)) return "";
            if (n.Contains("keyboard") || n.Contains("teclado")) return "";
            if (n.Contains("mouse")) return "";
            if (n.Contains("pen") || n.Contains("caneta")) return "";
            return "";
        }
    }
}
