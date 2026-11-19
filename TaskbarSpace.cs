using System;
using System.Collections.Generic;
using System.Windows.Automation;
using static MiniPlayer.Native;

namespace MiniPlayer
{
    // Espaço livre na barra de tarefas (em pixels físicos), lido pela acessibilidade do Windows:
    // posições de todos os itens da barra (Widgets/clima, Iniciar, pesquisa, apps, bandeja, relógio) → vãos entre eles.
    // Uma única consulta com cache traz todas as posições de uma vez (sem idas e vindas ao Explorer por item).
    static class TaskbarSpace
    {
        // vão livre ao longo da barra: Start/End = x (barra horizontal) ou y (barra lateral)
        public sealed record Gap(int Start, int End);

        // centered = ícones centralizados → vão no começo (depois do clima/Widgets); senão → vão no fim (antes da bandeja)
        public static Gap Find(IntPtr bar, bool centered, bool primary)
        {
            try
            {
                if (!GetWindowRect(bar, out var r)) return null;
                bool side = r.B - r.T > r.R - r.L;                  // barra lateral: mede na vertical
                int a0 = side ? r.T : r.L, a1 = side ? r.B : r.R;   // eixo da barra
                int c0 = side ? r.L : r.T, c1 = side ? r.R : r.B;   // eixo transversal
                int limit = a1;
                var tray = primary ? FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null) : IntPtr.Zero;
                if (tray != IntPtr.Zero && GetWindowRect(tray, out var tr) && (side ? tr.T : tr.L) > a0) limit = side ? tr.T : tr.L;

                var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None, TreeFilter = Automation.ControlViewCondition };
                cache.Add(AutomationElement.BoundingRectangleProperty);
                cache.Add(AutomationElement.IsOffscreenProperty);
                var root = AutomationElement.FromHandle(bar);
                AutomationElementCollection all;
                using (cache.Activate()) all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

                // itens visíveis da barra como intervalos no eixo da barra
                int barLen = a1 - a0;
                var items = new List<(int L, int R)>(all.Count);
                foreach (AutomationElement el in all)
                {
                    var b = el.Cached.BoundingRectangle;
                    if (b.IsEmpty || b.Width < 4 || b.Height < 4) continue;
                    double s0 = side ? b.Top : b.Left, s1 = side ? b.Bottom : b.Right, t0 = side ? b.Left : b.Top, t1 = side ? b.Right : b.Bottom;
                    if (s1 - s0 > barLen / 3.0) continue;                 // contêineres da barra inteira não contam
                    if (t1 <= c0 || t0 >= c1 || s1 <= a0 || s0 >= limit) continue;
                    if (el.Cached.IsOffscreen) continue;
                    items.Add(((int)s0, (int)Math.Ceiling(s1)));
                }
                if (items.Count == 0) return null;
                items.Sort((a, b) => a.L.CompareTo(b.L));

                // vãos entre os itens (itens a até 6 px de distância contam como um grupo só)
                Gap best = null; int mid = (a0 + a1) / 2, cur = a0;
                void Consider(int gl, int gr)
                {
                    if (gr - gl <= 0 || (centered ? gl >= mid : gr <= mid)) return;
                    if (best == null || gr - gl > best.End - best.Start) best = new Gap(gl, gr);
                }
                foreach (var (l, rr) in items)
                {
                    if (l > cur + 6) Consider(cur, l);
                    cur = Math.Max(cur, rr);
                }
                Consider(cur, limit);
                return best;
            }
            catch { return null; }
        }
    }
}
