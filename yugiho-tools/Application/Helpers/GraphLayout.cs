using yugiho_tools.Application.DTOs;
using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Application.Helpers;

public record GraphNode(
    string Name,
    Card? Card,
    NodeRole Role,
    int Layer,
    double X,
    double Y);

public record JunctionNode(
    double X,
    double Y,
    GraphNode Mat1,
    GraphNode Mat2,
    GraphNode Result,
    int Step);

public enum NodeRole { Input, Intermediate, Final }

public enum GraphLayoutMode
{
    /// <summary>Bases na primeira linha; resultados afundam por longest-path.</summary>
    Fusion,
    /// <summary>Cartas-base empurradas para a linha imediatamente antes do uso.</summary>
    Step,
}

public enum GraphVisibility
{
    /// <summary>Mostra todas as fusões possíveis (comportamento padrão).</summary>
    All,
    /// <summary>Greedy step-DESC: cada material só é consumido uma vez,
    /// priorizando fusões de maior profundidade.</summary>
    Simplified,
}

public record GraphLayoutResult(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<JunctionNode> Junctions,
    double Width,
    double Height);

/// <summary>
/// Top-to-bottom DAG layout merging all monster fusion sequences in a hand.
/// Pure inputs sit at the top row, intermediates in the middle, final cards
/// at the bottom — every card appears once, sharing branches across paths.
/// </summary>
public static class GraphLayout
{
    // Native source size of the card images served by basededatostea.xyz.
    public const double NodeW = 271;
    public const double NodeH = 386;
    public const double ColGap = 110;
    public const double RowGap = 230;
    public const double PadX = 24;
    public const double PadY = 24;
    public const double JunctionR = 22;

    public static GraphLayoutResult Build(
        IEnumerable<FusionSequence> sequences,
        IReadOnlyDictionary<string, Card> cardsByName,
        int? maxSteps = null,
        GraphLayoutMode mode = GraphLayoutMode.Fusion,
        GraphVisibility visibility = GraphVisibility.All)
    {
        var seqList = sequences.ToList();

        // Distinct fusion steps (a+b=r normalized so a<=b).
        // Equip-style steps (where one material equals the result) are skipped —
        // they don't produce a new card and just create a self-loop in the DAG.
        var stepKeys = new HashSet<(string A, string B, string R)>();
        var distinctSteps = new List<(string A, string B, string R)>();
        foreach (var seq in seqList)
            foreach (var s in seq.Steps)
            {
                if (s.Card1 == s.Result || s.Card2 == s.Result) continue;
                var (a, b) = string.Compare(s.Card1, s.Card2, StringComparison.Ordinal) <= 0
                    ? (s.Card1, s.Card2) : (s.Card2, s.Card1);
                var key = (a, b, s.Result);
                if (stepKeys.Add(key)) distinctSteps.Add(key);
            }

        // Apply max-steps filter. The "step" of a fusion is determined by the
        // DEEPEST MATERIAL it consumes — not by the longest path to its
        // result. So A + B = X is step 1 even if X is also reachable via a
        // longer chain. We use the pre-filter layers to know how deep each
        // material currently sits.
        if (maxSteps is > 0)
        {
            var preLayers = ComputeLayers(distinctSteps);
            distinctSteps = distinctSteps
                .Where(s =>
                {
                    int la = preLayers.GetValueOrDefault(s.A, 0);
                    int lb = preLayers.GetValueOrDefault(s.B, 0);
                    return Math.Max(la, lb) + 1 <= maxSteps.Value;
                })
                .ToList();
        }

        // Modo "Simplificada": opera sobre FusionSequences inteiras (cadeias
        // completas) em vez de steps individuais. Isso captura conflitos
        // TRANSITIVOS — duas cadeias que terminam em finais diferentes mas
        // que dependem da mesma carta-base (mesmo via intermediários
        // distintos) não podem coexistir.
        //
        // Greedy por ATK do final (DESC) com tiebreaker por profundidade da
        // cadeia (mais steps = plano mais elaborado), depois nome.
        if (visibility == GraphVisibility.Simplified && distinctSteps.Count > 0)
        {
            var distinctKeys = new HashSet<(string, string, string)>(distinctSteps);

            static (string A, string B, string R) NormalizeKey(FusionStep s)
            {
                var (a, b) = string.Compare(s.Card1, s.Card2, StringComparison.Ordinal) <= 0
                    ? (s.Card1, s.Card2) : (s.Card2, s.Card1);
                return (a, b, s.Result);
            }

            // Bases TRANSITIVAS de uma cadeia: todos os materiais que NÃO são
            // resultados de outro step da mesma cadeia (ou seja, vieram da
            // mão direto, não foram produzidos no caminho).
            static HashSet<string> GetBases(FusionSequence seq)
            {
                var mats = new HashSet<string>();
                var results = new HashSet<string>();
                foreach (var st in seq.Steps)
                {
                    if (st.Card1 == st.Result || st.Card2 == st.Result) continue;
                    mats.Add(st.Card1); mats.Add(st.Card2); results.Add(st.Result);
                }
                mats.ExceptWith(results);
                return mats;
            }

            // Mantém só cadeias cujos passos sobreviveram ao filtro maxSteps
            var validSeqs = seqList.Where(seq =>
                seq.Steps.Count > 0 &&
                seq.Steps.All(st => st.Card1 == st.Result || st.Card2 == st.Result
                                    || distinctKeys.Contains(NormalizeKey(st))))
                .ToList();

            var sorted = validSeqs
                .OrderByDescending(s => s.FinalCard.Attack)
                .ThenByDescending(s => s.Steps.Count)
                .ThenByDescending(s => s.FinalCard.Defense)
                .ThenBy(s => s.FinalCard.Name)
                .ToList();

            var consumedBases = new HashSet<string>();
            var keptSeqs = new List<FusionSequence>();
            foreach (var seq in sorted)
            {
                var bases = GetBases(seq);
                if (bases.Overlaps(consumedBases)) continue;
                keptSeqs.Add(seq);
                consumedBases.UnionWith(bases);
            }

            // Reconstrói distinctSteps somente com os passos das cadeias mantidas
            var newKeys = new HashSet<(string, string, string)>();
            var newSteps = new List<(string A, string B, string R)>();
            foreach (var seq in keptSeqs)
                foreach (var st in seq.Steps)
                {
                    if (st.Card1 == st.Result || st.Card2 == st.Result) continue;
                    var k = NormalizeKey(st);
                    if (newKeys.Add(k)) newSteps.Add(k);
                }
            distinctSteps = newSteps;
        }

        var allResults    = distinctSteps.Select(s => s.R).ToHashSet();
        var allMaterials  = distinctSteps.SelectMany(s => new[] { s.A, s.B }).ToHashSet();
        var intermediates = allResults.Intersect(allMaterials).ToHashSet();
        var inputs        = allMaterials.Except(allResults).ToHashSet();
        var finals        = seqList.Select(s => s.FinalCard.Name).ToHashSet();

        var preds = new Dictionary<string, HashSet<string>>();
        var succs = new Dictionary<string, HashSet<string>>();
        var allNames = new HashSet<string>();

        void AddName(string n)
        {
            allNames.Add(n);
            preds.TryAdd(n, []);
            succs.TryAdd(n, []);
        }

        foreach (var s in distinctSteps)
        {
            AddName(s.A); AddName(s.B); AddName(s.R);
            preds[s.R].Add(s.A);
            preds[s.R].Add(s.B);
            succs[s.A].Add(s.R);
            succs[s.B].Add(s.R);
        }
        foreach (var f in finals) AddName(f);

        // Row depends on the layout mode.
        Dictionary<string, int> layer = ComputeFusionDepthLayers(allNames, preds);
        if (mode == GraphLayoutMode.Step)
        {
            // Empurra cartas-base para a linha imediatamente antes do
            // primeiro uso, gerando um efeito "escada" diagonal.
            foreach (var n in inputs)
                if (succs.TryGetValue(n, out var ss) && ss.Count > 0)
                    layer[n] = ss.Min(s => layer.GetValueOrDefault(s, 0)) - 1;
        }

        // Normaliza para começar em 0 (modos podem produzir valores negativos)
        int minLayer = layer.Values.DefaultIfEmpty(0).Min();
        if (minLayer != 0)
            foreach (var k in layer.Keys.ToList())
                layer[k] -= minLayer;

        int maxLayer = layer.Values.DefaultIfEmpty(0).Max();

        // Group by row
        var byRow = Enumerable.Range(0, maxLayer + 1)
            .Select(l => allNames.Where(n => layer[n] == l).OrderBy(n => n).ToList())
            .ToList();

        // Initial column index per row
        var col = new Dictionary<string, double>();
        for (int l = 0; l <= maxLayer; l++)
            for (int i = 0; i < byRow[l].Count; i++)
                col[byRow[l][i]] = i;

        // Barycenter sweeps to reduce edge crossings (horizontal ordering)
        for (int sweep = 0; sweep < 8; sweep++)
        {
            for (int l = 1; l <= maxLayer; l++)
            {
                byRow[l] = byRow[l]
                    .OrderBy(n => preds[n].Count == 0 ? col[n] : preds[n].Average(p => col[p]))
                    .ThenBy(n => n)
                    .ToList();
                for (int i = 0; i < byRow[l].Count; i++) col[byRow[l][i]] = i;
            }
            for (int l = maxLayer - 1; l >= 0; l--)
            {
                byRow[l] = byRow[l]
                    .OrderBy(n => succs[n].Count == 0 ? col[n] : succs[n].Average(s => col[s]))
                    .ThenBy(n => n)
                    .ToList();
                for (int i = 0; i < byRow[l].Count; i++) col[byRow[l][i]] = i;
            }
        }

        // Pixel positions: row = Y, X computed top-down by predecessor barycenter,
        // resolving overlaps with a left→right sweep.
        var nodes = new Dictionary<string, GraphNode>();

        NodeRole RoleOf(string name) =>
            finals.Contains(name) ? NodeRole.Final
            : intermediates.Contains(name) ? NodeRole.Intermediate
            : NodeRole.Input;

        // Row 0: even spacing (after barycenter sort by successor centroid).
        double row0Y = PadY;
        for (int i = 0; i < byRow[0].Count; i++)
        {
            var name = byRow[0][i];
            cardsByName.TryGetValue(name, out var card);
            double px = PadX + i * (NodeW + ColGap);
            nodes[name] = new GraphNode(name, card, RoleOf(name), 0, px, row0Y);
        }

        for (int l = 1; l <= maxLayer; l++)
        {
            double rowY = PadY + l * (NodeH + RowGap);
            var rowNames = byRow[l];

            double DesiredX(string n)
            {
                var ps = preds[n].Where(p => nodes.ContainsKey(p)).ToList();
                if (ps.Count == 0) return PadX;
                double avgCenter = ps.Average(p => nodes[p].X + NodeW / 2);
                return avgCenter - NodeW / 2;
            }

            var ordered = rowNames.OrderBy(DesiredX).ThenBy(n => n).ToList();

            var x = new Dictionary<string, double>();
            double prevRight = double.MinValue;
            foreach (var n in ordered)
            {
                double d = Math.Max(DesiredX(n), PadX);
                double placed = Math.Max(d, prevRight + ColGap);
                x[n] = placed;
                prevRight = placed + NodeW;
            }

            foreach (var n in ordered)
            {
                cardsByName.TryGetValue(n, out var card);
                nodes[n] = new GraphNode(n, card, RoleOf(n), l, x[n], rowY);
            }
        }

        // Junctions: one per distinct (A, B, R) — sit just above the result.
        // Step number is derived from the materials' layers, not the result's
        // (so A+B=Z stays "step 1" even if Z is also reachable via a longer
        // chain that has Z at row 3).
        var junctions = new List<JunctionNode>();
        foreach (var s in distinctSteps)
        {
            var m1 = nodes[s.A];
            var m2 = nodes[s.B];
            var r  = nodes[s.R];

            double jx = (m1.X + m2.X) / 2 + NodeW / 2;
            // Junção SEMPRE dentro do gap adjacente ao resultado, do lado dos
            // materiais. Isso evita que a junção caia em cima de uma carta
            // intermediária quando os materiais estão em rows diferentes
            // (ex.: m1 em row 0 e m2 em row 2 → midpoint cairia em row 1).
            double avgMatYCenter = (m1.Y + m2.Y) / 2 + NodeH / 2;
            double resultYCenter = r.Y + NodeH / 2;
            double jy = avgMatYCenter < resultYCenter
                ? r.Y - RowGap / 2              // materiais acima → junção acima do resultado
                : r.Y + NodeH + RowGap / 2;     // materiais abaixo → junção abaixo do resultado

            int step = Math.Max(m1.Layer, m2.Layer) + 1;
            junctions.Add(new JunctionNode(jx, jy, m1, m2, r, step));
        }

        double rightmost = nodes.Values.DefaultIfEmpty().Max(n => n is null ? 0 : n.X + NodeW);
        double width  = rightmost + PadX;
        double height = PadY * 2 + (maxLayer + 1) * NodeH + maxLayer * RowGap;

        return new GraphLayoutResult(
            nodes.Values.ToList(),
            junctions,
            Math.Max(width,  NodeW + PadX * 2),
            Math.Max(height, NodeH + PadY * 2));
    }

    /// <summary>
    /// Layering por longest-path no DAG: bases ficam em 0, resultados afundam
    /// na profundidade do caminho mais longo até eles.
    /// </summary>
    private static Dictionary<string, int> ComputeFusionDepthLayers(
        IReadOnlyCollection<string> allNames,
        IReadOnlyDictionary<string, HashSet<string>> preds)
    {
        var layer = new Dictionary<string, int>();
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 200)
        {
            changed = false;
            foreach (var n in allNames)
            {
                var ps = preds[n];
                int newL = ps.Count == 0 ? 0
                         : ps.All(p => layer.ContainsKey(p))
                            ? ps.Max(p => layer[p]) + 1
                            : -1;
                if (newL < 0) continue;
                if (!layer.TryGetValue(n, out var cur) || cur != newL)
                {
                    layer[n] = newL;
                    changed = true;
                }
            }
        }
        foreach (var n in allNames)
            layer.TryAdd(n, 0);
        return layer;
    }

    /// <summary>
    /// Topological-style longest-path layering for a list of fusion steps.
    /// Returns the row index (0-based) of every node in the resulting DAG.
    /// </summary>
    private static Dictionary<string, int> ComputeLayers(
        IReadOnlyList<(string A, string B, string R)> steps)
    {
        var preds = new Dictionary<string, HashSet<string>>();
        var allNames = new HashSet<string>();

        void Touch(string n)
        {
            if (!allNames.Add(n)) return;
            preds[n] = [];
        }

        foreach (var s in steps)
        {
            Touch(s.A); Touch(s.B); Touch(s.R);
            preds[s.R].Add(s.A);
            preds[s.R].Add(s.B);
        }

        var layer = new Dictionary<string, int>();
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 200)
        {
            changed = false;
            foreach (var n in allNames)
            {
                int newL = preds[n].Count == 0 ? 0
                         : preds[n].All(p => layer.ContainsKey(p))
                            ? preds[n].Max(p => layer[p]) + 1
                            : -1;
                if (newL < 0) continue;
                if (!layer.TryGetValue(n, out var cur) || cur != newL)
                {
                    layer[n] = newL;
                    changed = true;
                }
            }
        }
        foreach (var n in allNames)
            layer.TryAdd(n, 0);
        return layer;
    }
}
