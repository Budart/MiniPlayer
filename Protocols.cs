using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using HidSharp;
using HidSharp.Reports;

namespace MiniPlayer
{
    // Protocolos de bateria de fabricantes (dongles USB e cabos): Logitech HID++, Razer, Nintendo
    // e o padrão HID de bateria (usado por alguns mouses/teclados 2.4 GHz genéricos)
    static class Protocols
    {
        public static List<BtBattery> ReadAll()
        {
            var res = new List<BtBattery>();
            void Try(Func<IEnumerable<BtBattery>> f) { try { res.AddRange(f()); } catch { } }
            Try(Logitech); Try(Razer); Try(Nintendo); Try(GenericHid);
            return res;
        }

        // ---------- Logitech HID++ 2.0 (Lightspeed, Unifying, Bolt, Bluetooth) ----------
        static readonly HashSet<int> LogiReceivers = new() { 0xC52B, 0xC532, 0xC534, 0xC539, 0xC53A, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC548, 0xC54D };

        static IEnumerable<BtBattery> Logitech()
        {
            var list = new List<BtBattery>();
            foreach (var dev in DeviceList.Local.GetHidDevices(0x046D))
            {
                try
                {
                    if (dev.GetMaxOutputReportLength() < 20 || dev.GetMaxInputReportLength() < 20) continue; // coleção "longa" do HID++
                    if (!dev.TryOpen(out var s)) continue;
                    using (s)
                    {
                        s.ReadTimeout = 250;
                        var idxs = LogiReceivers.Contains(dev.ProductID) ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
                        foreach (var d in idxs)
                        {
                            try
                            {
                                byte fi = Feature(s, d, 0x1004); bool unified = fi != 0;
                                if (!unified) fi = Feature(s, d, 0x1000);
                                if (fi == 0) continue;
                                var r = Call(s, d, fi, unified ? 1 : 0, null);
                                if (r == null) continue;
                                int pct = r[0], st = r[2];
                                if (unified && pct == 0) { int lv = r[1]; pct = (lv & 8) != 0 ? 100 : (lv & 4) != 0 ? 50 : (lv & 2) != 0 ? 20 : (lv & 1) != 0 ? 5 : 0; }
                                bool chg = st >= 1 && st <= 3;
                                string name = LogiName(s, d) ?? SafeName(dev, "Logitech");
                                int type = LogiType(s, d);
                                string icon = type is 0 or 2 ? "kbd" : type is 3 or 4 or 5 ? "mouse" : type == 8 ? "head" : type == 12 ? "pad" : BatteryReader.Kind(name, "mouse");
                                list.Add(new BtBattery(name, Math.Clamp(pct, 0, 100), icon, chg));
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        static byte[] Call(HidStream s, byte dev, byte feat, int fn, byte[] args)
        {
            var b = new byte[20];
            b[0] = 0x11; b[1] = dev; b[2] = feat; b[3] = (byte)((fn << 4) | 0x0A);
            if (args != null) Array.Copy(args, 0, b, 4, Math.Min(16, args.Length));
            s.Write(b);
            var rb = new byte[s.Device.GetMaxInputReportLength()];
            var end = DateTime.Now.AddMilliseconds(500);
            while (DateTime.Now < end)
            {
                int n;
                try { n = s.Read(rb, 0, rb.Length); } catch (TimeoutException) { break; }
                if (n < 7 || rb[1] != dev) continue;
                if (rb[2] == 0xFF && rb[3] == feat) return null;   // erro HID++ 2.0
                if (rb[0] == 0x10 && rb[2] == 0x8F) return null;   // dispositivo desligado / HID++ 1.0
                if (rb[0] == 0x11 && rb[2] == feat && rb[3] == b[3]) { var p = new byte[16]; Array.Copy(rb, 4, p, 0, Math.Min(16, n - 4)); return p; }
            }
            return null;
        }

        static byte Feature(HidStream s, byte dev, ushort id) { var r = Call(s, dev, 0, 0, new[] { (byte)(id >> 8), (byte)id }); return r == null ? (byte)0 : r[0]; }

        static string LogiName(HidStream s, byte d)
        {
            byte fi = Feature(s, d, 0x0005); if (fi == 0) return null;
            var c = Call(s, d, fi, 0, null); if (c == null) return null;
            int len = c[0]; var sb = new StringBuilder();
            for (int k = 0; k < 6 && sb.Length < len; k++)
            {
                var ch = Call(s, d, fi, 1, new[] { (byte)sb.Length }); if (ch == null) break;
                int before = sb.Length;
                foreach (var x in ch) { if (sb.Length >= len || x == 0) break; sb.Append((char)x); }
                if (sb.Length == before) break;
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        static int LogiType(HidStream s, byte d)
        {
            byte fi = Feature(s, d, 0x0005); if (fi == 0) return -1;
            var t = Call(s, d, fi, 2, null); return t == null ? -1 : t[0];
        }

        // ---------- Razer (mouses, teclados e headsets sem fio) ----------
        static IEnumerable<BtBattery> Razer()
        {
            var list = new List<BtBattery>(); var seen = new HashSet<string>();
            foreach (var dev in DeviceList.Local.GetHidDevices(0x1532))
            {
                try
                {
                    if (dev.GetMaxFeatureReportLength() < 91) continue;
                    string name = SafeName(dev, "Razer");
                    if (seen.Contains(name) || !dev.TryOpen(out var s)) continue;
                    using (s)
                        foreach (byte tid in new byte[] { 0x1F, 0x3F, 0xFF, 0x9F })
                        {
                            var bat = RazerCmd(s, tid, 0x80); if (bat == null) continue;
                            var chg = RazerCmd(s, tid, 0x84);
                            list.Add(new BtBattery(name, (int)Math.Round(bat.Value * 100 / 255.0), BatteryReader.Kind(name, "mouse"), chg == 1));
                            seen.Add(name); break;
                        }
                }
                catch { }
            }
            return list;
        }

        static int? RazerCmd(HidStream s, byte tid, byte cmd)
        {
            var b = new byte[91];
            b[2] = tid; b[6] = 0x02; b[7] = 0x07; b[8] = cmd;
            byte crc = 0; for (int i = 3; i < 89; i++) crc ^= b[i]; b[89] = crc;
            s.SetFeature(b); Thread.Sleep(60);
            var r = new byte[91]; s.GetFeature(r);
            if (r[1] != 0x02 || r[7] != 0x07 || r[8] != cmd) return null;
            return r[10];
        }

        // ---------- Nintendo Switch Pro Controller / Joy-Con ----------
        static IEnumerable<BtBattery> Nintendo()
        {
            var list = new List<BtBattery>();
            foreach (var dev in DeviceList.Local.GetHidDevices(0x057E))
            {
                string name = dev.ProductID switch { 0x2009 => "Pro Controller", 0x2006 => "Joy-Con (E)", 0x2007 => "Joy-Con (D)", _ => null };
                if (name == null) continue;
                try
                {
                    if (!dev.TryOpen(out var s)) continue;
                    using (s)
                    {
                        s.ReadTimeout = 300;
                        var buf = new byte[dev.GetMaxInputReportLength()];
                        for (int i = 0; i < 10; i++)
                        {
                            int n; try { n = s.Read(buf, 0, buf.Length); } catch (TimeoutException) { break; }
                            if (n < 3 || (buf[0] != 0x30 && buf[0] != 0x21)) continue;
                            int v = buf[2] >> 4; // nível 0-8 (passos de 2) + bit de carga
                            list.Add(new BtBattery(name, Math.Min(100, (v >> 1) * 25), "pad", (v & 1) != 0));
                            break;
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        // ---------- Padrão HID de bateria (Battery Strength / Remaining Capacity) ----------
        static readonly HashSet<int> Handled = new() { 0x054C, 0x046D, 0x1532, 0x057E };
        static bool IsBattery(uint u) => u == 0x00060020u || u == 0x00850066u;

        static IEnumerable<BtBattery> GenericHid()
        {
            var list = new List<BtBattery>(); var seen = new HashSet<string>();
            foreach (var dev in DeviceList.Local.GetHidDevices())
            {
                if (Handled.Contains(dev.VendorID)) continue;
                try
                {
                    var rd = dev.GetReportDescriptor();
                    var item = rd.DeviceItems.FirstOrDefault(di => di.InputReports.Any(r => r.DataItems.Any(x => x.Usages.GetAllValues().Any(IsBattery))));
                    if (item == null) continue;
                    string name = SafeName(dev, "Dispositivo USB");
                    if (seen.Contains(name) || !dev.TryOpen(out var s)) continue;
                    using (s)
                    {
                        var parser = item.CreateDeviceItemInputParser();
                        var rcv = rd.CreateHidDeviceInputReceiver();
                        rcv.Start(s);
                        var buf = new byte[dev.GetMaxInputReportLength()];
                        var end = DateTime.Now.AddMilliseconds(800);
                        int? pct = null;
                        while (pct == null && DateTime.Now < end)
                        {
                            if (!rcv.WaitHandle.WaitOne(200)) continue;
                            while (pct == null && rcv.TryRead(buf, 0, out var rep))
                            {
                                if (!parser.TryParseReport(buf, 0, rep)) continue;
                                while (parser.HasChanged)
                                {
                                    var dv = parser.GetValue(parser.GetNextChangedIndex());
                                    if (!dv.Usages.Any(IsBattery)) continue;
                                    var it = dv.DataItem; int span = Math.Max(1, it.LogicalMaximum - it.LogicalMinimum);
                                    pct = (int)Math.Round((dv.GetLogicalValue() - it.LogicalMinimum) * 100.0 / span);
                                }
                            }
                        }
                        if (pct == null) continue;
                        var top = rd.DeviceItems.SelectMany(di => di.Usages.GetAllValues()).ToList();
                        string icon = top.Contains(0x00010002u) ? "mouse" : top.Contains(0x00010006u) ? "kbd" : top.Contains(0x00010005u) ? "pad" : BatteryReader.Kind(name, "bt");
                        list.Add(new BtBattery(name, Math.Clamp(pct.Value, 0, 100), icon));
                        seen.Add(name);
                    }
                }
                catch { }
            }
            return list;
        }

        static string SafeName(HidDevice d, string fallback) { try { var n = d.GetProductName(); return string.IsNullOrWhiteSpace(n) ? fallback : n.Trim(); } catch { return fallback; } }
    }
}
