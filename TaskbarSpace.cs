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
        public sealed record Gap(int L, int R);

        // centered = ícones centralizados → vão à esquerda (depois do clima/Widgets); senão → vão à direita (antes da bandeja)
        public static Gap Find(IntPtr bar, bool centered, bool primary)
        {
            try
            {
                if (!GetWindowRect(bar, out var r) || r.R - r.L <= r.B - r.T) return null; // só barra horizontal
                int limit = r.R;
                var tray = primary ? FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null) : IntPtr.Zero;
                if (tray != IntPtr.Zero && GetWindowRect(tray, out var tr) && tr.L > r.L) limit = tr.L;

                var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None, TreeFilter = Automation.ControlViewCondition };
                cache.Add(AutomationElement.BoundingRectangleProperty);
                cache.Add(AutomationElement.IsOffscreenProperty);
                var root = AutomationElement.FromHandle(bar);
                AutomationElementCollection all;
                using (cache.Activate()) all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

                // itens visíveis da barra como intervalos horizontais
                int barW = r.R - r.L;
                var items = new List<(int L, int R)>(all.Count);
                foreach (AutomationElement el in all)
                {
                    var b = el.Cached.BoundingRectangle;
                    if (b.IsEmpty || b.Width < 4 || b.Height < 4 || b.Width > barW / 3.0) continue; // contêineres da barra inteira não contam
                    if (b.Bottom <= r.T || b.Top >= r.B || b.Right <= r.L || b.Left >= limit) continue;
                    if (el.Cached.IsOffscreen) continue;
                    items.Add(((int)b.Left, (int)Math.Ceiling(b.Right)));
                }
                if (items.Count == 0) return null;
                items.Sort((a, b) => a.L.CompareTo(b.L));

                // vãos entre os itens (itens a até 6 px de distância contam como um grupo só)
                Gap best = null; int mid = (r.L + r.R) / 2, cur = r.L;
                void Consider(int gl, int gr)
                {
                    if (gr - gl <= 0 || (centered ? gl >= mid : gr <= mid)) return;
                    if (best == null || gr - gl > best.R - best.L) best = new Gap(gl, gr);
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
