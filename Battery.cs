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
        const string HwKey = "System.Devices.HardwareIds";

        public static async Task<List<BtBattery>> ReadAll()
        {
            var res = new List<BtBattery>();
            var seen = new HashSet<string>();
            try
            {
                var devs = await DeviceInformation.FindAllAsync("", new[] { BattKey, ContainerKey, HwKey }, DeviceInformationKind.Device);
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
                    var ids = d.Properties.TryGetValue(HwKey, out var h) && h is string[] arr ? string.Join(";", arr).ToLowerInvariant() : "";
                    string kind = KindFromIds(ids) ?? Kind(name, "bt");
                    // controle PlayStation já lido direto (mais preciso e em tempo real): não repete
                    if ((kind == "ds5" || kind == "ds4") && PsActive(kind)) continue;
                    res.Add(new BtBattery(name, Math.Clamp(pct, 0, 100), kind));
                }
            }
            catch { }
            try { res.AddRange(ReadPlayStation()); } catch { }
            try { res.AddRange(Protocols.ReadAll()); } catch { }
            return res.GroupBy(x => x.Name.ToLowerInvariant()).Select(g => g.First()).ToList();
        }

        // Controles PlayStation: uma leitura contínua por controle (tempo real), iniciada assim que o controle aparece
        sealed class PsReader { public bool Bt; public volatile BtBattery Last; }
        static readonly Dictionary<string, PsReader> readers = new();
        static bool watching;

        public static void StartWatching()
        {
            if (watching) return; watching = true;
            DeviceList.Local.Changed += (_, __) => { EnsureReaders(); App.Kick(); };
            // conexão/desconexão Bluetooth instantânea
            foreach (var sel in new[] { Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                                        Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
            {
                var w = DeviceInformation.CreateWatcher(sel, new[] { "System.Devices.Aep.IsConnected" }, DeviceInformationKind.AssociationEndpoint);
                w.Updated += (_, __) => App.Kick();
                w.Removed += (_, __) => App.Kick();
                w.Start();
            }
            EnsureReaders();
        }

        static void EnsureReaders()
        {
            try
            {
                foreach (var dev in DeviceList.Local.GetHidDevices(0x054C))
                {
                    int pid = dev.ProductID;
                    bool ds5 = pid == 0x0CE6 || pid == 0x0DF2, ds4 = pid == 0x05C4 || pid == 0x09CC;
                    if (!ds5 && !ds4) continue;
                    lock (readers) { if (readers.ContainsKey(dev.DevicePath)) continue; }
                    var r = new PsReader();
                    lock (readers) readers[dev.DevicePath] = r;
                    new System.Threading.Thread(() => ReadLoop(dev, ds5, r)) { IsBackground = true }.Start();
                }
            }
            catch { }
        }

        static void ReadLoop(HidDevice dev, bool ds5, PsReader r)
        {
            try
            {
                int max = dev.GetMaxInputReportLength();
                r.Bt = max > 64;
                if (!dev.TryOpen(out var s)) return;
                using (s)
                {
                    s.ReadTimeout = System.Threading.Timeout.Infinite;
                    if (r.Bt) try { var f = new byte[ds5 ? 41 : 37]; f[0] = (byte)(ds5 ? 0x05 : 0x02); s.GetFeature(f); } catch { } // ativa relatório completo
                    var buf = new byte[max];
                    while (true)
                    {
                        int n = s.Read(buf, 0, buf.Length), idx;
                        if (ds5) idx = buf[0] == 0x31 ? 54 : (buf[0] == 0x01 && !r.Bt ? 53 : -1);
                        else idx = buf[0] == 0x11 ? 32 : (buf[0] == 0x01 && !r.Bt ? 30 : -1);
                        if (idx < 0 || idx >= n) continue;
                        int v = buf[idx], lvl = v & 0x0F, pct;
                        bool chg;
                        if (ds5)
                        {
                            // o controle só informa 11 níveis (0-10); usa o meio de cada faixa, como a Steam (0 → 5%)
                            int st = v >> 4; chg = st == 1 || st == 2;
                            pct = st == 2 ? 100 : Math.Min(100, lvl * 10 + 5);
                        }
                        else
                        {
                            chg = (v & 0x10) != 0;
                            pct = chg ? Math.Min(100, lvl * 100 / 11) : Math.Min(100, lvl * 10 + 5);
                        }
                        var nb = new BtBattery(ds5 ? "DualSense" : "DualShock 4", pct, "", chg || !r.Bt);
                        if (r.Last == null || r.Last.Percent != nb.Percent || r.Last.Charging != nb.Charging) { r.Last = nb; App.Kick(); }
                    }
                }
            }
            catch { }
            finally { lock (readers) readers.Remove(dev.DevicePath); App.Kick(); }
        }

        static IEnumerable<BtBattery> ReadPlayStation()
        {
            List<(BtBattery b, bool bt)> list;
            lock (readers) list = readers.Values.Where(r => r.Last != null).Select(r => (r.Last, r.Bt)).ToList();
            // mesmo controle no Bluetooth e no cabo: para cada um via USB, descarta um via Bluetooth do mesmo modelo
            foreach (var usb in list.Where(x => !x.bt).ToList())
            {
                var dup = list.FirstOrDefault(x => x.bt && x.b.Name == usb.b.Name);
                if (dup.b != null) list.Remove(dup);
            }
            return list.Select(x => x.b);
        }

        static bool PsActive(string kind)
        {
            lock (readers) return readers.Values.Any(r => r.Last != null && r.Last.Glyph == kind);
        }

        // Modelo pelo ID de hardware (VID/PID), independente do nome/idioma
        static string KindFromIds(string ids)
        {
            if (ids.Contains("054c"))
                return ids.Contains("0ce6") || ids.Contains("0df2") ? "ds5" : ids.Contains("05c4") || ids.Contains("09cc") ? "ds4" : "pad";
            if (ids.Contains("057e")) return "pad";
            if (ids.Contains("045e") && new[] { "02e0", "02fd", "0b05", "0b12", "0b13", "0b20", "0b22" }.Any(ids.Contains)) return "xbox";
            return null;
        }

        // Tipo de ícone pelo nome do dispositivo (PT e EN)
        public static string Kind(string n, string fallback)
        {
            var k = Glyph(n); return k == "bt" ? fallback : k;
        }

        static string Glyph(string n)
        {
            n = (n ?? "").ToLowerInvariant();
            if (n.Contains("dualsense")) return "ds5";
            if (n.Contains("dualshock") || n.Contains("wireless controller")) return "ds4";
            if (n.Contains("xbox")) return "xbox";
            if (new[] { "controller", "controle", "controlador", "gamepad", "playstation", "joy-con", "8bitdo", "pro controller" }.Any(n.Contains)) return "pad";
            if (new[] { "buds", "airpods", "tws", "earbud", "freebuds", "wf-", "galaxy buds", "soundcore liberty" }.Any(n.Contains)) return "buds";
            if (new[] { "head", "fone", "wh-", "jbl", "headset", "soundcore", "audio", "qc", "beats", "ear" }.Any(n.Contains)) return "head";
            if (new[] { "speaker", "caixa", "soundlink", "flip", "charge" }.Any(n.Contains)) return "spk";
            if (new[] { "keyboard", "teclado", "keys", "k380", "k780" }.Any(n.Contains)) return "kbd";
            if (new[] { "mouse", "mx master", "mx anywhere", "trackpad", "m720", "m590" }.Any(n.Contains)) return "mouse";
            return "bt";
        }
    }
}
