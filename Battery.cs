using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HidSharp;
using Windows.Devices.Enumeration;

namespace MiniPlayer
{
    public record BtBattery(string Name, int Percent, string Glyph, bool Charging = false);

    // Serviço de bateria orientado a eventos: o Windows avisa cada mudança de bateria/conexão,
    // controles PlayStation são lidos continuamente e protocolos de fabricantes (dongles) são consultados periodicamente.
    // Snapshot() só monta a lista a partir do que já está em memória (sem E/S), então é barato.
    static class BatteryReader
    {
        const string BattKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
        const string ContainerKey = "System.Devices.ContainerId";
        const string HwKey = "System.Devices.HardwareIds";
        const string AepConnected = "System.Devices.Aep.IsConnected", AepContainer = "System.Devices.Aep.ContainerId";
        // classe do aparelho Bluetooth: é o que o próprio Windows usa para escolher o ícone nas Configurações
        const string CodMajor = "System.Devices.Aep.Bluetooth.Cod.Major", CodMinor = "System.Devices.Aep.Bluetooth.Cod.Minor",
                     LeAppearance = "System.Devices.Aep.Bluetooth.Le.Appearance";

        public static event Action Changed;
        static readonly object gate = new();
        static readonly Dictionary<string, DeviceInformation> winDevs = new();        // nós com bateria informada pelo Windows
        static readonly Dictionary<string, DeviceInformation> aeps = new();           // dispositivos Bluetooth pareados
        static readonly Dictionary<string, (string name, string cat)> containers = new();
        static Dictionary<string, string> hidKinds = new();
        static List<BtBattery> protoList = new();
        static System.Threading.Timer debounce;
        static TaskCompletionSource<bool> protoKick = new();
        static bool started;

        static void Notify() => debounce?.Change(120, System.Threading.Timeout.Infinite); // agrupa rajadas de eventos

        public static void Start()
        {
            if (started) return; started = true;
            debounce = new System.Threading.Timer(_ => Changed?.Invoke());

            // bateria informada pelo Windows (fones, Xbox, teclados/mouses BLE...): atualizações chegam em tempo real
            var w = DeviceInformation.CreateWatcher("", new[] { BattKey, ContainerKey, HwKey }, DeviceInformationKind.Device);
            w.Added += (_, d) => { if (HasBatt(d)) Track(d); };
            w.Updated += (snd, u) =>
            {
                lock (gate)
                    if (winDevs.TryGetValue(u.Id, out var d)) { d.Update(u); if (!HasBatt(d)) winDevs.Remove(u.Id); Notify(); return; }
                if (u.Properties.ContainsKey(BattKey)) _ = AddById(u.Id);
            };
            w.Removed += (_, u) => { bool r; lock (gate) r = winDevs.Remove(u.Id); if (r) Notify(); };
            w.Start();

            // conexão/desconexão Bluetooth instantânea
            foreach (var sel in new[] { Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                                        Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
            {
                DeviceWatcher aw;
                try { aw = DeviceInformation.CreateWatcher(sel, new[] { AepConnected, AepContainer, CodMajor, CodMinor, LeAppearance }, DeviceInformationKind.AssociationEndpoint); }
                catch { aw = DeviceInformation.CreateWatcher(sel, new[] { AepConnected, AepContainer }, DeviceInformationKind.AssociationEndpoint); }
                aw.Added += (_, d) => { lock (gate) aeps[d.Id] = d; Notify(); };
                aw.Updated += (snd, u) => { lock (gate) if (aeps.TryGetValue(u.Id, out var d)) d.Update(u); Notify(); ProtoKick(); };
                aw.Removed += (_, u) => { lock (gate) aeps.Remove(u.Id); Notify(); };
                aw.Start();
            }

            // USB/HID: controles PlayStation, tipos de dispositivo e protocolos de fabricantes
            DeviceList.Local.Changed += (snd, ev) => { EnsureReaders(); _ = RefreshHidKinds(); ProtoKick(); };
            EnsureReaders();
            _ = RefreshHidKinds();
            Task.Run(ProtocolLoop);
        }

        static bool HasBatt(DeviceInformation d) => d.Properties.TryGetValue(BattKey, out var b) && b != null;

        static void Track(DeviceInformation d)
        {
            lock (gate) winDevs[d.Id] = d;
            _ = EnsureContainer(Cid(d));
            Notify();
        }

        static async Task AddById(string id)
        {
            try { var d = await DeviceInformation.CreateFromIdAsync(id, new[] { BattKey, ContainerKey, HwKey }, DeviceInformationKind.Device); if (HasBatt(d)) Track(d); } catch { }
        }

        static string Cid(DeviceInformation d) => d.Properties.TryGetValue(ContainerKey, out var c) && c != null ? c.ToString() : d.Id;

        // nome e categoria do "aparelho" (container), buscados uma vez e guardados
        static async Task EnsureContainer(string cid)
        {
            lock (gate) if (containers.ContainsKey(cid)) return;
            string name = null, cat = "";
            try
            {
                var ct = await DeviceInformation.CreateFromIdAsync("{" + cid + "}", new[] { "System.Devices.Category" }, DeviceInformationKind.DeviceContainer);
                name = ct.Name;
                if (ct.Properties.TryGetValue("System.Devices.Category", out var c) && c is string[] cats) cat = string.Join(";", cats).ToLowerInvariant();
            }
            catch { }
            lock (gate) containers[cid] = (name, cat);
            Notify();
        }

        // Mapa ContainerId -> tipo, pelas interfaces HID que o Windows classifica como joystick/gamepad, teclado ou mouse
        static async Task RefreshHidKinds()
        {
            var map = new Dictionary<string, string>();
            var kinds = new (ushort page, ushort id, string kind)[] { (0x01, 0x06, "kbd"), (0x01, 0x02, "mouse"), (0x01, 0x04, "pad"), (0x01, 0x05, "pad"), (0x01, 0x08, "pad") };
            foreach (var (page, id, kind) in kinds)
            {
                try
                {
                    var sel = Windows.Devices.HumanInterfaceDevice.HidDevice.GetDeviceSelector(page, id);
                    foreach (var di in await DeviceInformation.FindAllAsync(sel, new[] { ContainerKey }))
                        if (di.Properties.TryGetValue(ContainerKey, out var c) && c != null) {
                            var key = c.ToString();
                            if (map.TryGetValue(key, out var prev) && prev != kind && prev != "pad" && kind != "pad") map[key] = "?"; // teclado e mouse juntos: ambíguo
                            else if (!map.ContainsKey(key) || kind == "pad") map[key] = kind;
                        }
                }
                catch { }
            }
            hidKinds = map;
            Notify();
        }

        static void ProtoKick() => protoKick.TrySetResult(true);

        static async Task ProtocolLoop()
        {
            while (true)
            {
                protoKick = new();
                if (App.ShowBattery)
                {
                    try
                    {
                        var l = Protocols.ReadAll();
                        if (!l.SequenceEqual(protoList)) { protoList = l; Notify(); }
                    }
                    catch { }
                }
                await Task.WhenAny(Task.Delay(30000), protoKick.Task);
                await Task.Delay(500);
            }
        }

        // Monta a lista atual a partir da memória
        public static List<BtBattery> Snapshot()
        {
            var res = new List<BtBattery>();
            var seen = new HashSet<string>();
            var hk = hidKinds;
            List<DeviceInformation> devs;
            // um mesmo aparelho pode ter 2 registros Bluetooth (clássico + LE, comum em fones Sony, JBL...):
            // ele só está desconectado se NENHUM dos registros estiver conectado
            var known = new HashSet<string>(); var online = new Dictionary<string, DeviceInformation>();
            var btKinds = new Dictionary<string, string>();
            lock (gate)
            {
                devs = winDevs.Values.ToList();
                foreach (var a in aeps.Values)
                {
                    if (!a.Properties.TryGetValue(AepContainer, out var c) || c == null) continue;
                    var cid = c.ToString();
                    known.Add(cid);
                    if (a.Properties.TryGetValue(AepConnected, out var on) && on is bool b && b) online.TryAdd(cid, a);
                    var k = KindFromBluetooth(a);
                    if (k != null && !btKinds.ContainsKey(cid)) btKinds[cid] = k;
                }
            }
            var offline = new HashSet<string>(known.Where(c => !online.ContainsKey(c)));
            foreach (var d in devs)
            {
                try
                {
                    var cid = Cid(d);
                    if (offline.Contains(cid) || !seen.Add(cid)) continue;
                    int pct = Math.Clamp(Convert.ToInt32(d.Properties[BattKey]), 0, 100);
                    (string name, string cat) ct;
                    lock (gate) containers.TryGetValue(cid, out ct);
                    string name = string.IsNullOrEmpty(ct.name) ? d.Name : ct.name, cat = ct.cat ?? "";
                    var ids = d.Properties.TryGetValue(HwKey, out var h) && h is string[] arr ? string.Join(";", arr).ToLowerInvariant() : "";
                    // tipo: 1) classe Bluetooth (mesma das Configurações do Windows)  2) VID/PID  3) nome  4) classificação HID  5) categoria
                    string kind = (btKinds.TryGetValue(cid, out var bk) ? bk : null) ?? KindFromIds(ids) ?? Kind(name, null)
                                  ?? (hk.TryGetValue(cid, out var k) && k != "?" ? k : null) ?? KindFromCategory(cat) ?? "bt";
                    if ((kind == "ds5" || kind == "ds4") && PsActive(kind)) continue; // já lido direto, com mais precisão
                    res.Add(new BtBattery(name, pct, kind));
                }
                catch { }
            }
            try { res.AddRange(ReadPlayStation()); } catch { }
            res.AddRange(protoList);
            // aparelhos Bluetooth conectados que não informam bateria ao Windows também aparecem (com "--")
            foreach (var (cid, a) in online)
            {
                if (seen.Contains(cid)) continue;
                try
                {
                    string name = a.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string kind = (btKinds.TryGetValue(cid, out var bk) ? bk : null) ?? Kind(name, null) ?? (hk.TryGetValue(cid, out var k) && k != "?" ? k : null) ?? "bt";
                    if ((kind == "ds5" || kind == "ds4") && PsActive(kind)) continue;
                    seen.Add(cid);
                    res.Add(new BtBattery(name, -1, kind));
                }
                catch { }
            }
            // mesmo nome vindo de fontes diferentes: fica a que tem bateria
            return res.GroupBy(x => x.Name.ToLowerInvariant()).Select(g => g.OrderByDescending(x => x.Percent).First()).OrderBy(x => x.Name).ToList();
        }

        // Bluetooth clássico: Class of Device. Bluetooth LE: Appearance. Ambos definidos pelo fabricante no próprio aparelho.
        static string KindFromBluetooth(DeviceInformation a)
        {
            try
            {
                if (a.Properties.TryGetValue(LeAppearance, out var ap) && ap != null)
                {
                    int v = Convert.ToInt32(ap);
                    switch (v)
                    {
                        case 0x03C1: return "kbd";
                        case 0x03C2: return "mouse";
                        case 0x03C3: case 0x03C4: return "pad";
                    }
                    int cat = v >> 6;
                    if (cat == 0x21 || cat == 0x25) return "head";            // áudio (fone/headset), aparelho auditivo
                    if (cat == 0x0F) return null;                             // HID genérico: decide por outras fontes
                }
                if (a.Properties.TryGetValue(CodMajor, out var ma) && ma != null && a.Properties.TryGetValue(CodMinor, out var mi) && mi != null)
                {
                    int major = Convert.ToInt32(ma), minor = Convert.ToInt32(mi);
                    if (major == 5) // periférico
                    {
                        int sub = minor & 0x0F, kb = minor & 0x30;
                        if (sub == 1 || sub == 2) return "pad";
                        if (kb == 0x20) return "mouse";
                        if (kb == 0x10 || kb == 0x30) return "kbd";
                    }
                    if (major == 4) return minor == 5 ? "spk" : "head"; // áudio/vídeo
                }
            }
            catch { }
            return null;
        }

        static string KindFromCategory(string cat) =>
            cat.Contains("gam") ? "pad" : cat.Contains("headphone") || cat.Contains("headset") || cat.Contains("audio") ? "head"
            : cat.Contains("keyboard") ? "kbd" : cat.Contains("mouse") ? "mouse" : null;

        // Controles PlayStation: uma leitura contínua por controle (tempo real), iniciada assim que o controle aparece
        sealed class PsReader { public bool Bt; public volatile BtBattery Last; }
        static readonly Dictionary<string, PsReader> readers = new();
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
                        var nb = new BtBattery(ds5 ? "DualSense" : "DualShock 4", pct, ds5 ? "ds5" : "ds4", chg || !r.Bt);
                        if (r.Last == null || r.Last.Percent != nb.Percent || r.Last.Charging != nb.Charging) { r.Last = nb; Notify(); }
                    }
                }
            }
            catch { }
            finally { lock (readers) readers.Remove(dev.DevicePath); Notify(); }
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
