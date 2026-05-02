using System.Buffers.Binary;
using System.Text;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Infrastructure.Parsing;

/// <summary>
/// Parser for PSX memory card images (.mcr / .mc / .mem / .gme), 128 KB,
/// produced by emulators like ePSXe.
///
/// Layout:
///   Block 0 (8 KB): directory + header
///     Frame 0 (128 B): "MC" magic
///     Frames 1..15 (128 B each): directory entries (one per save block)
///   Blocks 1..15 (8 KB each): save data
///     Frame 0 (128 B): "SC" + title + icon palette
///     Frames 1..3 (128 B each): icon bitmaps
///     Frame 4 onwards: game-specific save data
///
/// Yu-Gi-Oh! Forbidden Memories saves carry a 722-byte "trunk" array
/// (one byte per card = how many copies the player owns). This parser
/// looks for that pattern inside FM saves and returns the list of cards.
/// </summary>
public class EpsxeMemoryCardParser : IMemoryCardParser
{
    private const int FrameSize  = 128;
    private const int BlockSize  = 8192;
    private const int BlockCount = 15;
    private const int CardCount  = 722;

    public async Task<MemoryCardParseResult> ParseAsync(string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        return Parse(bytes);
    }

    public MemoryCardParseResult Parse(byte[] data)
    {
        if (data.Length < BlockSize)
            throw new InvalidDataException(
                "Arquivo muito pequeno para ser um memory card PSX (esperado 128 KB).");

        // Validate "MC" magic in block 0 (warning-only — some dumps lack it)
        bool hasMcMagic = data[0] == (byte)'M' && data[1] == (byte)'C';

        var saves = new List<MemoryCardSave>();

        for (int slot = 0; slot < BlockCount; slot++)
        {
            int dirOffset = (slot + 1) * FrameSize;
            if (dirOffset + FrameSize > data.Length) break;

            uint status = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(dirOffset, 4));

            // Only consider blocks marked as "in use, first link of save"
            if (status != 0x51) continue;

            string country  = ReadAscii(data, dirOffset + 0x0A, 2);
            string product  = ReadAscii(data, dirOffset + 0x0C, 10);
            string saveId   = ReadAscii(data, dirOffset + 0x16, 8);
            string gameCode = (country + product).Trim();

            // Walk linked chain for multi-block saves
            int blockIndex = slot + 1;
            int blockCount = 1;
            int next = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(dirOffset + 0x08, 2));
            while (next >= 0 && next < BlockCount && blockCount < BlockCount)
            {
                blockCount++;
                int nextDir = (next + 1) * FrameSize;
                if (nextDir + FrameSize > data.Length) break;
                next = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(nextDir + 0x08, 2));
            }

            bool isFM = product.Contains("01411", StringComparison.OrdinalIgnoreCase);

            // Always attempt extraction — the user may be using a mod / pirate
            // build with a different product code (e.g. TLM-XXXXX) but the
            // trunk format remains the same as the original FM ROM.
            var cards = ExtractCardsFromSave(data, blockIndex, blockCount);

            saves.Add(new MemoryCardSave(
                SlotIndex: slot,
                GameCode:  gameCode,
                SaveId:    saveId,
                StartBlock: blockIndex,
                BlockCount: blockCount,
                IsForbiddenMemories: isFM,
                Cards: cards));
        }

        return new MemoryCardParseResult(saves);
    }

    /// <summary>
    /// Extracts the player's deck (40 cards) from a Forbidden Memories save.
    /// Strategy: scan for a window of 40 consecutive card IDs (16-bit LE)
    /// where every value falls in 1..722 — that's the deck array. Duplicate
    /// IDs become the multiplicity of each card.
    /// </summary>
    private static IReadOnlyList<MemoryCardCardEntry> ExtractCardsFromSave(
        byte[] data, int startBlock, int blockCount)
    {
        const int HeaderSkip = 0x200;          // SC header + 3 icon frames
        const int DeckSize   = 40;             // FM deck is exactly 40 cards

        int totalLen = blockCount * BlockSize;
        if (startBlock * BlockSize + totalLen > data.Length)
            totalLen = data.Length - startBlock * BlockSize;
        if (totalLen <= HeaderSkip) return [];

        var save = data.AsSpan(startBlock * BlockSize + HeaderSkip,
                               totalLen - HeaderSkip);

        var deck = TryFindDeck(save, DeckSize);
        return deck;
    }

    /// <summary>
    /// Locate the 40-card deck array. FM stores each card slot as a card ID
    /// (1..722). We try 2-byte and 4-byte strides because some builds pad
    /// each slot with extra fields.
    /// </summary>
    private static List<MemoryCardCardEntry> TryFindDeck(
        ReadOnlySpan<byte> save, int deckSize)
    {
        var bestEntries = new List<MemoryCardCardEntry>();
        int bestScore   = 0;

        foreach (var stride in new[] { 2, 4 })
        {
            int span = deckSize * stride;
            if (save.Length < span) continue;

            for (int offset = 0; offset + span <= save.Length; offset++)
            {
                int validIds = 0;
                bool ok = true;
                var counts = new Dictionary<int, int>();

                for (int i = 0; i < deckSize; i++)
                {
                    int id = BinaryPrimitives.ReadUInt16LittleEndian(
                        save.Slice(offset + i * stride, 2));
                    if (id == 0) continue;                      // empty slot
                    if (id < 1 || id > CardCount) { ok = false; break; }
                    validIds++;
                    counts[id] = counts.GetValueOrDefault(id) + 1;
                }
                if (!ok) continue;

                // Require a fully-populated, plausible deck.
                if (validIds < deckSize * 0.75) continue;

                // Prefer windows that match exactly 40 cards with reasonable
                // diversity (≥ 50 % distinct ids — FM decks rarely run a
                // single card ×40).
                int distinct = counts.Count;
                if (distinct < 5) continue;

                int score = validIds * 100 + distinct;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEntries = counts
                        .OrderBy(kv => kv.Key)
                        .Select(kv => new MemoryCardCardEntry(kv.Key, kv.Value))
                        .ToList();
                }
            }

            // First stride that finds something good wins (avoids scanning
            // a second time and ranking different formats against each other).
            if (bestEntries.Count > 0) return bestEntries;
        }

        return bestEntries;
    }

    private static string ReadAscii(byte[] data, int offset, int length)
    {
        if (offset + length > data.Length) return "";
        var bytes = data.AsSpan(offset, length);
        int end = bytes.IndexOf((byte)0);
        if (end < 0) end = bytes.Length;

        // Convert printable ASCII as-is, replace anything else with '?'.
        var sb = new StringBuilder(end);
        for (int i = 0; i < end; i++)
        {
            byte b = bytes[i];
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '?');
        }
        return sb.ToString().TrimEnd();
    }
}
