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
    GraphNode Result);

public enum NodeRole { Input, Intermediate, Final }

public record GraphLayoutResult(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<JunctionNode> Junctions,
    double Width,
    double Height);

/// <summary>
/// Top-to-bottom DAG layout merging all fusion sequences in a hand.
/// Pure inputs sit at the top row, intermediates in the middle, final cards
/// at the bottom — every card appears once, sharing branches across paths.
/// </summary>
public static class GraphLayout
{
    public const double NodeW = 130;
    public const double NodeH = 110;
    public const double ColGap = 60;
    public const double RowGap = 110;
    public const double PadX = 24;
    public const double PadY = 24;
    public const double JunctionR = 11;

    public static GraphLayoutResult Build(
        IEnumerable<FusionSequence> sequences,
        IReadOnlyDictionary<string, Card> cardsByName)
    {
        var seqList = sequences.ToList();

        // Distinct fusion steps (a+b=r normalized so a<=b)
        var stepKeys = new HashSet<(string A, string B, string R)>();
        var distinctSteps = new List<(string A, string B, string R)>();
        foreach (var seq in seqList)
            foreach (var s in seq.Steps)
            {
                var (a, b) = string.Compare(s.Card1, s.Card2, StringComparison.Ordinal) <= 0
                    ? (s.Card1, s.Card2) : (s.Card2, s.Card1);
                var key = (a, b, s.Result);
                if (stepKeys.Add(key)) distinctSteps.Add(key);
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

        // Row = longest path from any source
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

        // Row 0 = pure base cards (no predecessors).
        // Row N = result of fusion(s) at depth N (longest path from any base).
        // We deliberately do NOT push inputs forward nor pin finals to the last row,
        // so base cards always sit together at the top.
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

            // Desired top-left X = avg(predecessor center) - NodeW/2
            double DesiredX(string n)
            {
                var ps = preds[n].Where(p => nodes.ContainsKey(p)).ToList();
                if (ps.Count == 0) return PadX;
                double avgCenter = ps.Average(p => nodes[p].X + NodeW / 2);
                return avgCenter - NodeW / 2;
            }

            // Sort by desired X to keep order consistent with predecessors
            var ordered = rowNames.OrderBy(DesiredX).ThenBy(n => n).ToList();

            // Pass 1: left→right, push right to avoid overlap
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

        // Junctions: one per distinct (A, B, R) — sit just above the result
        var junctions = new List<JunctionNode>();
        foreach (var s in distinctSteps)
        {
            var m1 = nodes[s.A];
            var m2 = nodes[s.B];
            var r  = nodes[s.R];

            double jx = (m1.X + m2.X) / 2 + NodeW / 2;
            double jy = r.Y - RowGap / 2;
            junctions.Add(new JunctionNode(jx, jy, m1, m2, r));
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
}
