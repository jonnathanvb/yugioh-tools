using System.Text;
using yugiho_tools.Application.DTOs;

namespace yugiho_tools.Application.Helpers;

/// <summary>
/// Generates a Mermaid LR flowchart from a group of FusionSequences.
/// Multiple paths that share intermediates are merged into one node,
/// producing a convergent graph (evolutionary-scale style).
/// </summary>
public static class MermaidGenerator
{
    public static string Generate(IEnumerable<FusionSequence> sequences)
    {
        var seqList = sequences.ToList();

        // Collect all card roles across every sequence in this group
        var allResultCards  = seqList
            .SelectMany(s => s.Steps.Select(st => st.Result))
            .ToHashSet();

        var allMaterialCards = seqList
            .SelectMany(s => s.Steps.SelectMany(st => new[] { st.Card1, st.Card2 }))
            .ToHashSet();

        // Intermediate: appears as both a result AND a material (used in a next step)
        var intermediateCards = allResultCards.Intersect(allMaterialCards).ToHashSet();

        // Pure inputs: appears as material but never as a result of any step
        var inputCards = allMaterialCards.Except(allResultCards).ToHashSet();

        // Final cards: last result of every sequence
        var finalCards = seqList.Select(s => s.FinalCard.Name).ToHashSet();

        var edges       = new HashSet<string>();
        var nodeLabels  = new Dictionary<string, string>(); // id → label

        foreach (var seq in seqList)
        {
            nodeLabels.TryAdd(NodeId(seq.FinalCard.Name), seq.FinalCard.Name);

            foreach (var step in seq.Steps)
            {
                nodeLabels.TryAdd(NodeId(step.Card1),  step.Card1);
                nodeLabels.TryAdd(NodeId(step.Card2),  step.Card2);
                nodeLabels.TryAdd(NodeId(step.Result), step.Result);

                edges.Add($"{NodeId(step.Card1)} --> {NodeId(step.Result)}");
                edges.Add($"{NodeId(step.Card2)} --> {NodeId(step.Result)}");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        // 1. Node definitions  (shape encodes the role)
        foreach (var (id, label) in nodeLabels)
        {
            string esc = Esc(label);
            string def = finalCards.Contains(label)         ? $"[[{esc}]]"  // stadium / double-bracket = final
                       : intermediateCards.Contains(label)  ? $"({esc})"    // rounded = intermediate
                                                            : $"[{esc}]";   // square  = input
            sb.AppendLine($"    {id}{def}");
        }

        sb.AppendLine();

        // 2. Edges
        foreach (var edge in edges)
            sb.AppendLine($"    {edge}");

        sb.AppendLine();

        // 3. Styles
        foreach (var (id, label) in nodeLabels)
        {
            if (finalCards.Contains(label))
                sb.AppendLine($"    style {id} fill:#2a1a00,stroke:#c8a415,color:#c8a415,font-weight:bold,stroke-width:2px");
            else if (intermediateCards.Contains(label))
                sb.AppendLine($"    style {id} fill:#1a1035,stroke:#6a3aae,color:#c0a0ff");
            else
                sb.AppendLine($"    style {id} fill:#141428,stroke:#2a5a8e,color:#e0e0e0");
        }

        return sb.ToString();
    }

    private static string NodeId(string name)
    {
        var sb = new StringBuilder("n");
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string id = sb.ToString();
        return id.Length > 48 ? id[..48] : id;
    }

    // Mermaid label: wrap in quotes, escape inner quotes
    private static string Esc(string label) =>
        $"\"{label.Replace("\"", "'")}\"";
}
