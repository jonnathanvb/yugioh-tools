using yugiho_tools.Application.DTOs;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Port of filereader.py: getFusionChain + evaluateFusion.
/// Now returns step-by-step sequences with intermediate results shown.
/// </summary>
public class FusionEngine : IFusionEngine
{
    public IReadOnlyList<FusionSequence> GetFusionsFromHand(
        IReadOnlyList<int> hand,
        IReadOnlyList<Card> cards)
    {
        var handList = hand.ToList();

        // Build all raw chains (each chain = ordered list of card indices)
        var allChains = new List<List<int>>();
        for (int i = 0; i < handList.Count; i++)
        {
            var rest = new List<int>(handList);
            rest.RemoveAt(i);
            allChains.AddRange(GetFusionChain(handList[i], rest, [handList[i]], cards));
        }

        // Evaluate each chain → get final result index
        var evaluated = new List<(int FinalIdx, List<int> Chain)>();
        foreach (var chain in allChains)
        {
            // Chains with only 1 card produce no fusion — skip
            if (chain.Count < 2) continue;

            int finalIdx = EvaluateFusion(chain, cards);
            evaluated.Add((finalIdx, chain));
        }

        // Sort descending by (attack - number of steps)
        evaluated.Sort((a, b) =>
            (cards[b.FinalIdx].Attack - b.Chain.Count)
            .CompareTo(cards[a.FinalIdx].Attack - a.Chain.Count));

        // Deduplicate by (finalIdx, chain sequence)
        var seen = new HashSet<string>();
        var sequences = new List<FusionSequence>();

        foreach (var (finalIdx, chain) in evaluated)
        {
            string key = $"{finalIdx}:{string.Join(",", chain)}";
            if (!seen.Add(key)) continue;

            var steps = BuildSteps(chain, cards);
            sequences.Add(new FusionSequence(steps, cards[finalIdx]));
        }

        return sequences;
    }

    /// <summary>
    /// Produces the list of FusionStep for a chain, showing each intermediate result.
    /// Chain [A, B, C]: step1 = A+B=X, step2 = X+C=Final.
    /// </summary>
    private static IReadOnlyList<FusionStep> BuildSteps(List<int> chain, IReadOnlyList<Card> cards)
    {
        var steps = new List<FusionStep>();
        int current = chain[0];

        for (int i = 1; i < chain.Count; i++)
        {
            int material = chain[i];
            int resultIdx = Fuse(current, material, cards);

            steps.Add(new FusionStep(
                Card1:  cards[current].Name,
                Card2:  cards[material].Name,
                Result: cards[resultIdx].Name
            ));
            current = resultIdx;
        }
        return steps;
    }

    // Port of getFusionChain — recursive chain builder
    private static List<List<int>> GetFusionChain(
        int myCard,
        List<int> hand,
        List<int> fusionChain,
        IReadOnlyList<Card> cards)
    {
        if (hand.Count == 0 || cards[myCard].FusionResults.Count == 0)
            return [[.. fusionChain]];

        var fusions = new List<List<int>>();

        for (int j = 0; j < hand.Count; j++)
        {
            int handCard = hand[j];
            bool handFusesMe  = cards[handCard].FusionMaterials.Contains(myCard);
            bool iFuseHand    = cards[myCard].FusionMaterials.Contains(handCard);

            if (!iFuseHand && !handFusesMe) continue;

            int result = handFusesMe
                ? cards[handCard].FusionResults[cards[handCard].FusionMaterials.IndexOf(myCard)]
                : cards[myCard].FusionResults[cards[myCard].FusionMaterials.IndexOf(handCard)];

            var newHand  = new List<int>(hand);
            newHand.RemoveAt(j);

            var newChain = new List<int>(fusionChain) { handCard };

            fusions.AddRange(GetFusionChain(result, newHand, newChain, cards));
        }

        fusions.Add([.. fusionChain]);
        return fusions;
    }

    // Port of evaluateFusion
    private static int EvaluateFusion(List<int> chain, IReadOnlyList<Card> cards)
    {
        int current = chain[0];
        for (int i = 1; i < chain.Count; i++)
            current = Fuse(current, chain[i], cards);
        return current;
    }

    private static int Fuse(int a, int b, IReadOnlyList<Card> cards)
    {
        int idx = cards[b].FusionMaterials.IndexOf(a);
        if (idx >= 0) return cards[b].FusionResults[idx];
        return cards[a].FusionResults[cards[a].FusionMaterials.IndexOf(b)];
    }
}
