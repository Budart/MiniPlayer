using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MiniPlayer
{
    // Monitor do sistema (sem administrador e sem drivers):
    //  - RAM e CPU: APIs do Windows (GlobalMemoryStatusEx / GetSystemTimes)
    //  - Uso de GPU: contador do Windows "GPU Engine" (o mesmo do Gerenciador de Tarefas)
    //  - Temperatura da GPU: informação do driver de vídeo (a mesma do Gerenciador de Tarefas; GPUs dedicadas)
    //  - Temperatura da CPU: zonas térmicas ACPI do Windows (aproximada; nem todo PC informa)
    // Lê a cada 2 s, só enquanto o monitor estiver visível. Valor -1 = indisponível neste PC.
    static class SystemMonitor
    {
        public static int Ram = -1, Cpu = -1, Gpu = -1, CpuTemp = -1, GpuTemp = -1;
        public static event Action Updated;
        static Timer timer;

        public static void Run(bool on)
        {
            if (on && timer == null) timer = new Timer(_ => Sample(), null, 0, 2000);
            else if (!on && timer != null) { timer.Dispose(); timer = null; }
        }

        static int busy;
        static void Sample()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                Ram = ReadRam(); Cpu = ReadCpu(); Gpu = ReadPdhSum(ref gpuQ, ref gpuC, @"\GPU Engine(*engtype_3D)\Utilization Percentage", 100);
                GpuTemp = ReadGpuTemp(); CpuTemp = ReadCpuTemp();
                Updated?.Invoke();
            }
            catch { }
            finally { busy = 0; }
        }

        // ---- RAM ----
        [StructLayout(LayoutKind.Sequential)] struct MEMSTATUS { public uint len, load; public ulong total, avail, tpf, apf, tv, av, aev; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTATUS m);
        static int ReadRam() { var m = new MEMSTATUS { len = (uint)Marshal.SizeOf<MEMSTATUS>() }; return GlobalMemoryStatusEx(ref m) ? (int)m.load : -1; }

        // ---- CPU ----
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static long pIdle, pTotal;
        static int ReadCpu()
        {
            if (!GetSystemTimes(out var idle, out var k, out var u)) return -1;
            long total = k + u, dIdle = idle - pIdle, dTotal = total - pTotal;
            pIdle = idle; pTotal = total;
            return dTotal <= 0 ? Math.Max(Cpu, 0) : (int)Math.Clamp(Math.Round(100.0 * (dTotal - dIdle) / dTotal), 0, 100);
        }

        // ---- contadores do Windows (PDH) ----
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
        const uint PDH_FMT_DOUBLE = 0x200, PDH_MORE_DATA = 0x800007D2;
        static IntPtr gpuQ, gpuC, tzQ, tzC;

        // soma (ou máximo) dos valores de todas as instâncias de um contador com curinga
        static int ReadPdhSum(ref IntPtr q, ref IntPtr c, string path, int cap, bool max = false, Func<double, double> conv = null)
        {
            if (q == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out q) != 0 || PdhAddEnglishCounter(q, path, IntPtr.Zero, out c) != 0) { q = (IntPtr)(-1); return -1; }
                PdhCollectQueryData(q);
                return -1; // a 1ª leitura só prepara a base
            }
            if (q == (IntPtr)(-1) || PdhCollectQueryData(q) != 0) return -1;
            uint size = 0;
            if ((uint)PdhGetFormattedCounterArray(c, PDH_FMT_DOUBLE, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA || size == 0) return -1;
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(c, PDH_FMT_DOUBLE, ref size, out var n, buf) != 0) return -1;
                // PDH_FMT_COUNTERVALUE_ITEM_W (x64): ponteiro do nome (8) + status (4) + alinhamento (4) + double (8) = 24 bytes
                double acc = 0; bool any = false;
                for (int i = 0; i < n; i++)
                {
                    var p = buf + i * 24;
                    if (Marshal.ReadInt32(p, 8) != 0) continue; // status
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, 16));
                    if (conv != null) v = conv(v);
                    acc = max ? Math.Max(acc, v) : acc + v; any = true;
                }
                return any ? (int)Math.Clamp(Math.Round(acc), 0, cap) : -1;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // ---- temperatura da CPU: zonas térmicas ACPI (Kelvin) ----
        static int ReadCpuTemp()
        {
            int t = ReadPdhSum(ref tzQ, ref tzC, @"\Thermal Zone Information(*)\Temperature", 150, true, k => k - 273.15);
            return t is > 5 and < 125 ? t : -1;
        }

        // ---- temperatura da GPU: dados de desempenho do driver (D3DKMT), sem administrador ----
        [StructLayout(LayoutKind.Sequential)] struct ADAPTERINFO { public uint hAdapter; public uint luidLow; public int luidHigh; public uint numSources; public int precise; }
        [StructLayout(LayoutKind.Sequential)] struct ENUMADAPTERS2 { public uint count; public IntPtr adapters; }
        [StructLayout(LayoutKind.Sequential)] struct QUERYINFO { public uint hAdapter; public int type; public IntPtr data; public uint size; }
        [StructLayout(LayoutKind.Sequential)] struct PERFDATA { public uint index; public ulong memFreq, maxMemFreq, maxMemFreqOC, memBw, pcieBw; public uint fanRpm, power, temperature; public byte powerOverride; }
        [StructLayout(LayoutKind.Sequential)] struct CLOSEADAPTER { public uint hAdapter; }
        [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref ENUMADAPTERS2 e);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref QUERYINFO q);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref CLOSEADAPTER c);
        static bool gpuTempUnsupported;
        static int ReadGpuTemp()
        {
            if (gpuTempUnsupported) return -1;
            try
            {
                var e = new ENUMADAPTERS2();
                if (D3DKMTEnumAdapters2(ref e) != 0 || e.count == 0) { gpuTempUnsupported = true; return -1; }
                int sz = Marshal.SizeOf<ADAPTERINFO>();
                e.adapters = Marshal.AllocHGlobal(sz * (int)e.count);
                int best = -1;
                try
                {
                    if (D3DKMTEnumAdapters2(ref e) != 0) return -1;
                    for (int i = 0; i < e.count; i++)
                    {
                        var a = Marshal.PtrToStructure<ADAPTERINFO>(e.adapters + i * sz);
                        var perf = new PERFDATA();
                        var h = GCHandle.Alloc(perf, GCHandleType.Pinned);
                        try
                        {
                            var q = new QUERYINFO { hAdapter = a.hAdapter, type = 62 /*KMTQAITYPE_ADAPTERPERFDATA*/, data = h.AddrOfPinnedObject(), size = (uint)Marshal.SizeOf<PERFDATA>() };
                            if (D3DKMTQueryAdapterInfo(ref q) == 0)
                            {
                                perf = Marshal.PtrToStructure<PERFDATA>(h.AddrOfPinnedObject());
                                int c = (int)(perf.temperature / 10); // décimos de °C
                                if (c is > 5 and < 125) best = Math.Max(best, c);
                            }
                        }
                        finally { h.Free(); var ca = new CLOSEADAPTER { hAdapter = a.hAdapter }; D3DKMTCloseAdapter(ref ca); }
                    }
                }
                finally { Marshal.FreeHGlobal(e.adapters); }
                return best;
            }
            catch { gpuTempUnsupported = true; return -1; }
        }
    }
}
