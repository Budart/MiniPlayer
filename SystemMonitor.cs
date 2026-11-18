using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MiniPlayer
{
    // Monitor do sistema — sem administrador e sem drivers, só APIs oficiais do Windows:
    //  - RAM: GlobalMemoryStatusEx (mesma base do Gerenciador de Tarefas)
    //  - CPU: contador "% Processor Utility" (o que o Gerenciador de Tarefas do Windows 11 mostra); reserva: GetSystemTimes
    //  - GPU: contador "GPU Engine" somado por motor de cada placa e pegando o mais ocupado (mesma conta do Gerenciador de Tarefas,
    //         funciona com várias GPUs e qualquer fabricante)
    //  - Temp. GPU: dados do driver de vídeo (D3DKMT), os mesmos do Gerenciador de Tarefas (GPUs dedicadas, driver WDDM 2.4+)
    //  - Temp. CPU: zonas térmicas ACPI do Windows (aproximada; depende da placa-mãe informar)
    // Lê a cada 2 s, só os itens ligados. Valor -1 = indisponível neste PC.
    static class SystemMonitor
    {
        public static int Ram = -1, Cpu = -1, Gpu = -1, CpuTemp = -1, GpuTemp = -1;
        public static event Action Updated;
        static Timer timer;

        public static void Run(bool on)
        {
            if (on && timer == null) timer = new Timer(_ => Sample(), null, 0, 2000);
            else if (!on && timer != null) { timer.Dispose(); timer = null; CloseAll(); }
        }

        static int busy;
        static void Sample()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                // só lê o que está ligado no menu; o que foi desligado libera seus recursos
                Ram = App.SysOn(1) ? ReadRam() : -1;
                if (App.SysOn(2)) Cpu = ReadCpu(); else { Cpu = -1; cpuQ.Close(); }
                if (App.SysOn(4)) CpuTemp = ReadCpuTemp(); else { CpuTemp = -1; tzQ.Close(); }
                if (App.SysOn(8)) Gpu = ReadGpu(); else { Gpu = -1; gpuQ.Close(); }
                if (App.SysOn(16)) GpuTemp = ReadGpuTemp(); else { GpuTemp = -1; CloseAdapters(); }
                Updated?.Invoke();
            }
            catch { }
            finally { busy = 0; }
        }

        static void CloseAll() { cpuQ.Close(); tzQ.Close(); gpuQ.Close(); CloseAdapters(); Ram = Cpu = Gpu = CpuTemp = GpuTemp = -1; }

        // ---- RAM ----
        [StructLayout(LayoutKind.Sequential)] struct MEMSTATUS { public uint len, load; public ulong total, avail, tpf, apf, tv, av, aev; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTATUS m);
        static int ReadRam() { var m = new MEMSTATUS { len = (uint)Marshal.SizeOf<MEMSTATUS>() }; return GlobalMemoryStatusEx(ref m) ? (int)m.load : -1; }

        // ---- CPU ----
        static readonly Pdh cpuQ = new(@"\Processor Information(_Total)\% Processor Utility");
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static long pIdle, pTotal;
        static int ReadCpu()
        {
            // reserva sempre atualizada (precisa de duas leituras para comparar)
            int byTimes = -1;
            if (GetSystemTimes(out var idle, out var k, out var u))
            {
                long total = k + u, dIdle = idle - pIdle, dTotal = total - pTotal;
                if (pTotal != 0 && dTotal > 0) byTimes = (int)Math.Clamp(Math.Round(100.0 * (dTotal - dIdle) / dTotal), 0, 100);
                pIdle = idle; pTotal = total;
            }
            var v = cpuQ.Read();
            return v.Count > 0 ? (int)Math.Clamp(Math.Round(v[0].value), 0, 100) : byTimes;
        }

        // ---- GPU (uso) ----
        static readonly Pdh gpuQ = new(@"\GPU Engine(*)\Utilization Percentage");
        static readonly Dictionary<string, double> perEngine = new();
        static int ReadGpu()
        {
            var v = gpuQ.Read();
            if (v.Count == 0) return -1;
            // instância: pid_1234_luid_0x..._0x..._phys_0_eng_3_engtype_3D → soma por motor (sem o pid), depois o maior
            perEngine.Clear();
            foreach (var (name, val) in v)
            {
                int i = name.IndexOf("luid_", StringComparison.Ordinal), j = name.IndexOf("_engtype_", StringComparison.Ordinal);
                if (i < 0 || j < i) continue;
                var key = name.Substring(i, j - i);
                perEngine[key] = (perEngine.TryGetValue(key, out var acc) ? acc : 0) + val;
            }
            double best = 0; foreach (var x in perEngine.Values) best = Math.Max(best, x);
            return (int)Math.Clamp(Math.Round(best), 0, 100);
        }

        // ---- temperatura da CPU: zonas térmicas ACPI ----
        static readonly Pdh tzQ = new(@"\Thermal Zone Information(*)\High Precision Temperature", @"\Thermal Zone Information(*)\Temperature");
        static int ReadCpuTemp()
        {
            var v = tzQ.Read();
            double best = -1;
            foreach (var (_, val) in v)
            {
                double c = (tzQ.UsingFallback ? val : val / 10.0) - 273.15; // décimos de Kelvin (alta precisão) ou Kelvin
                if (c is > 5 and < 125) best = Math.Max(best, c);
            }
            return best < 0 ? -1 : (int)Math.Round(best);
        }

        // ---- temperatura da GPU: dados de desempenho do driver (D3DKMT) ----
        [StructLayout(LayoutKind.Sequential)] struct ADAPTERINFO { public uint hAdapter; public uint luidLow; public int luidHigh; public uint numSources; public int precise; }
        [StructLayout(LayoutKind.Sequential)] struct ENUMADAPTERS2 { public uint count; public IntPtr adapters; }
        [StructLayout(LayoutKind.Sequential)] struct QUERYINFO { public uint hAdapter; public int type; public IntPtr data; public uint size; }
        [StructLayout(LayoutKind.Sequential)] struct PERFDATA { public uint index; public ulong memFreq, maxMemFreq, maxMemFreqOC, memBw, pcieBw; public uint fanRpm, power, temperature; public byte powerOverride; }
        [StructLayout(LayoutKind.Sequential)] struct CLOSEADAPTER { public uint hAdapter; }
        [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref ENUMADAPTERS2 e);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref QUERYINFO q);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref CLOSEADAPTER c);
        static uint[] adapters;                      // abertos uma vez e reaproveitados
        static IntPtr perfBuf;                       // buffer único para a consulta
        static bool gpuTempUnsupported;
        static void CloseAdapters()
        {
            if (adapters != null) foreach (var h in adapters) { var c = new CLOSEADAPTER { hAdapter = h }; D3DKMTCloseAdapter(ref c); }
            adapters = null;
        }
        static int ReadGpuTemp()
        {
            if (gpuTempUnsupported) return -1;
            try
            {
                if (adapters == null)
                {
                    var e = new ENUMADAPTERS2();
                    if (D3DKMTEnumAdapters2(ref e) != 0 || e.count == 0) { gpuTempUnsupported = true; return -1; }
                    int sz = Marshal.SizeOf<ADAPTERINFO>();
                    e.adapters = Marshal.AllocHGlobal(sz * (int)e.count);
                    try
                    {
                        if (D3DKMTEnumAdapters2(ref e) != 0) return -1;
                        adapters = new uint[e.count];
                        for (int i = 0; i < e.count; i++) adapters[i] = Marshal.PtrToStructure<ADAPTERINFO>(e.adapters + i * sz).hAdapter;
                    }
                    finally { Marshal.FreeHGlobal(e.adapters); }
                    if (perfBuf == IntPtr.Zero) perfBuf = Marshal.AllocHGlobal(Marshal.SizeOf<PERFDATA>());
                }
                int best = -1; bool anyOk = false;
                foreach (var h in adapters)
                {
                    Marshal.StructureToPtr(new PERFDATA(), perfBuf, false);
                    var q = new QUERYINFO { hAdapter = h, type = 62 /*KMTQAITYPE_ADAPTERPERFDATA*/, data = perfBuf, size = (uint)Marshal.SizeOf<PERFDATA>() };
                    if (D3DKMTQueryAdapterInfo(ref q) != 0) continue;
                    anyOk = true;
                    int c = (int)(Marshal.PtrToStructure<PERFDATA>(perfBuf).temperature / 10); // décimos de °C
                    if (c is > 5 and < 125) best = Math.Max(best, c);
                }
                if (!anyOk) { CloseAdapters(); } // placa trocada/driver reiniciado: reabre na próxima leitura
                return best;
            }
            catch { gpuTempUnsupported = true; return -1; }
        }

        // ---- leitor de contador do Windows (PDH), com nomes em inglês (funciona em qualquer idioma) ----
        sealed class Pdh
        {
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr q);
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
            [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
            [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr q);
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
            const uint FMT_DOUBLE = 0x200, FMT_NOCAP100 = 0x8000, MORE_DATA = 0x800007D2;

            readonly string[] paths;
            IntPtr q, c, buf; int bufSize; bool failed;
            public bool UsingFallback { get; private set; }
            readonly List<(string name, double value)> result = new();
            public Pdh(params string[] paths) => this.paths = paths;

            public List<(string name, double value)> Read()
            {
                result.Clear();
                if (failed) return result;
                if (q == IntPtr.Zero)
                {
                    for (int i = 0; i < paths.Length && q == IntPtr.Zero; i++)
                    {
                        if (PdhOpenQuery(null, IntPtr.Zero, out var nq) != 0) break;
                        if (PdhAddEnglishCounter(nq, paths[i], IntPtr.Zero, out var nc) == 0) { q = nq; c = nc; UsingFallback = i > 0; }
                        else PdhCloseQuery(nq);
                    }
                    if (q == IntPtr.Zero) { failed = true; return result; }
                    PdhCollectQueryData(q); // 1ª coleta só prepara a base dos contadores de taxa
                    return result;
                }
                if (PdhCollectQueryData(q) != 0) return result;
                uint size = 0;
                if ((uint)PdhGetFormattedCounterArray(c, FMT_DOUBLE | FMT_NOCAP100, ref size, out _, IntPtr.Zero) != MORE_DATA || size == 0) return result;
                if (size > bufSize) { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); buf = Marshal.AllocHGlobal((int)size); bufSize = (int)size; } // buffer reaproveitado
                if (PdhGetFormattedCounterArray(c, FMT_DOUBLE | FMT_NOCAP100, ref size, out var n, buf) != 0) return result;
                // PDH_FMT_COUNTERVALUE_ITEM_W (x64): nome (ponteiro, 8) + status (4) + alinhamento (4) + double (8) = 24 bytes
                for (int i = 0; i < n; i++)
                {
                    var p = buf + i * 24;
                    if (Marshal.ReadInt32(p, 8) != 0) continue;
                    result.Add((Marshal.PtrToStringUni(Marshal.ReadIntPtr(p)) ?? "", BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, 16))));
                }
                return result;
            }

            public void Close()
            {
                if (q != IntPtr.Zero) PdhCloseQuery(q);
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                q = c = buf = IntPtr.Zero; bufSize = 0;
            }
        }
    }
}
