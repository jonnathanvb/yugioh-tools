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

    /// <summary>
    /// Localiza o trunk de FM dentro do save.
    /// 1) Tenta o offset conhecido do FM-US (SLUS-01411): 80 bytes após o
    ///    header+ícones (= file offset 0x2250 quando o save está em block 1).
    /// 2) Se a validação falhar, executa heurística byte-per-card.
    /// 3) Por último, tenta heurística 16-bit-per-card (mods exóticos).
    /// </summary>
    public MemoryCardTrunk? FindTrunk(byte[] data, MemoryCardSave save)
    {
        const int HeaderSkip       = 0x200;
        const int FmTrunkOffset    = 80;     // offset confirmado para FM-US e MODs derivados
        int saveStart = save.StartBlock * BlockSize + HeaderSkip;
        int saveLen   = Math.Min(save.BlockCount * BlockSize,
                                 data.Length - save.StartBlock * BlockSize) - HeaderSkip;
        if (saveLen <= CardCount) return null;

        // Tentativa 1: offset hardcoded do FM. Valida que a janela de 722
        // bytes seja plausível (todos ≤ 99, ≥ 1 não-zero).
        if (saveLen >= FmTrunkOffset + CardCount)
        {
            int absolute = saveStart + FmTrunkOffset;
            int violations = 0, nonZero = 0;
            for (int i = 0; i < CardCount; i++)
            {
                byte b = data[absolute + i];
                if (b > 99) violations++;
                else if (b > 0) nonZero++;
            }
            if (violations == 0 && nonZero > 0)
            {
                var counts = new byte[CardCount];
                Array.Copy(data, absolute, counts, 0, CardCount);
                return new MemoryCardTrunk(save, absolute, 1, counts);
            }
        }

        // Tentativa 2/3: heurísticas (mods com layout não padrão)
        var byteResult = FindTrunkByte(data, saveStart, saveLen);
        var wordResult = FindTrunkWord(data, saveStart, saveLen);

        int byteScore = byteResult?.NonZero ?? -1;
        int wordScore = wordResult?.NonZero ?? -1;

        if (byteScore < 0 && wordScore < 0) return null;

        if (byteScore >= wordScore)
            return new MemoryCardTrunk(save, byteResult!.Value.Offset, 1, byteResult.Value.Counts);

        return new MemoryCardTrunk(save, wordResult!.Value.Offset, 2, wordResult.Value.Counts);
    }

    private static (int Offset, byte[] Counts, int NonZero)? FindTrunkByte(byte[] data, int saveStart, int saveLen)
    {
        const int MaxViolations = CardCount / 20; // ≤ 5%
        int bestOffset  = -1;
        int bestNonZero = 0;

        for (int offset = 0; offset + CardCount <= saveLen; offset++)
        {
            int violations = 0;
            int nonZero = 0;
            for (int i = 0; i < CardCount; i++)
            {
                byte b = data[saveStart + offset + i];
                if (b > 99) { violations++; if (violations > MaxViolations) break; continue; }
                if (b > 0)  nonZero++;
            }
            if (violations > MaxViolations) continue;
            if (nonZero < 1) continue;
            if (nonZero > bestNonZero)
            {
                bestNonZero = nonZero;
                bestOffset  = offset;
            }
        }

        if (bestOffset < 0) return null;
        var counts = new byte[CardCount];
        for (int i = 0; i < CardCount; i++)
        {
            byte b = data[saveStart + bestOffset + i];
            counts[i] = b > 99 ? (byte)0 : b;  // clipa violações para 0 na visualização
        }
        return (saveStart + bestOffset, counts, bestNonZero);
    }

    private static (int Offset, byte[] Counts, int NonZero)? FindTrunkWord(byte[] data, int saveStart, int saveLen)
    {
        const int MaxViolations = CardCount / 20;
        int trunkLen = CardCount * 2;
        int bestOffset  = -1;
        int bestNonZero = 0;

        for (int offset = 0; offset + trunkLen <= saveLen; offset++)
        {
            int violations = 0;
            int nonZero = 0;
            for (int i = 0; i < CardCount; i++)
            {
                int v = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                    data.AsSpan(saveStart + offset + i * 2, 2));
                if (v > 99) { violations++; if (violations > MaxViolations) break; continue; }
                if (v > 0)  nonZero++;
            }
            if (violations > MaxViolations) continue;
            if (nonZero < 1) continue;
            if (nonZero > bestNonZero)
            {
                bestNonZero = nonZero;
                bestOffset  = offset;
            }
        }

        if (bestOffset < 0) return null;

        var counts = new byte[CardCount];
        for (int i = 0; i < CardCount; i++)
        {
            int v = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                data.AsSpan(saveStart + bestOffset + i * 2, 2));
            counts[i] = (byte)(v > 99 ? 0 : v);
        }
        return (saveStart + bestOffset, counts, bestNonZero);
    }

    /// <summary>
    /// Reescreve o trunk no arquivo + atualiza os 2 CRC16-CCITT do save block
    /// e zera os respectivos fillers, em ambas as metades (main e backup).
    ///
    /// Estrutura confirmada (FM-US e mods derivados como TLMFV):
    ///  • Trunk main em block_start + 0x250 (720 bytes)
    ///  • CRC#1 cobre block_start + 0x200 .. 0x53D (gravado em 0x53E-0x53F MSB-first)
    ///  • Filler #1 em 0x540-0x57F (zerado pós-save)
    ///  • CRC#2 cobre block_start + 0x600 .. 0x7FD (gravado em 0x7FE-0x7FF)
    ///  • Filler #2 em 0x800-0x82F (zerado pós-save)
    ///  • Tudo isso é espelhado em block_start + 0x680 (backup half)
    ///
    /// Sem o CRC correto, o jogo mostra "illegal data!" ao carregar.
    /// </summary>
    public async Task SaveTrunkAsync(
        string filePath,
        MemoryCardTrunk trunk,
        IReadOnlyDictionary<int, int> counts)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Memory card não encontrado.", filePath);

        // Backup automático antes de mexer no arquivo
        WriteAutoBackup(filePath);

        var bytes = await File.ReadAllBytesAsync(filePath);
        int span = CardCount * trunk.BytesPerEntry;
        if (trunk.OffsetInFile + span > bytes.Length)
            throw new InvalidDataException("Offset do trunk fora do arquivo.");

        // Trunk offset == block_start + 0x250
        int blockStart = trunk.OffsetInFile - 0x250;
        const int BackupDelta = 0x680;

        // Escreve trunk + recalcula CRCs nas DUAS metades (main e backup)
        ApplyHalf(bytes, blockStart, trunk, counts);
        if (blockStart + BackupDelta + 0x830 <= bytes.Length)
            ApplyHalf(bytes, blockStart + BackupDelta, trunk, counts);

        await File.WriteAllBytesAsync(filePath, bytes);
    }

    /// <summary>
    /// Atualiza uma metade do save block (main ou backup): grava trunk,
    /// recomputa os 2 CRCs e zera os fillers.
    /// </summary>
    private static void ApplyHalf(
        byte[] bytes, int halfStart, MemoryCardTrunk trunk,
        IReadOnlyDictionary<int, int> counts)
    {
        // 1. Trunk em halfStart + 0x250
        WriteCounts(bytes, halfStart + 0x250, trunk, counts);

        // 2. CRC#1 sobre [halfStart+0x200 .. halfStart+0x53D] inclusive
        ushort crc1 = Crc16Ccitt(bytes, halfStart + 0x200, 0x53E - 0x200);
        bytes[halfStart + 0x53E] = (byte)((crc1 >> 8) & 0xFF);  // MSB
        bytes[halfStart + 0x53F] = (byte)(crc1 & 0xFF);          // LSB
        for (int i = halfStart + 0x540; i <= halfStart + 0x57F; i++) bytes[i] = 0;

        // 3. CRC#2 sobre [halfStart+0x600 .. halfStart+0x7FD] inclusive
        ushort crc2 = Crc16Ccitt(bytes, halfStart + 0x600, 0x7FE - 0x600);
        bytes[halfStart + 0x7FE] = (byte)((crc2 >> 8) & 0xFF);
        bytes[halfStart + 0x7FF] = (byte)(crc2 & 0xFF);
        for (int i = halfStart + 0x800; i <= halfStart + 0x82F; i++) bytes[i] = 0;
    }

    // ─── Save Counter ─────────────────────────────────────────────
    //
    // FM grava um "games played counter" em duas posições dentro do save
    // block, espelhadas na backup half (+0x680):
    //   block_start + 0x604  →  contador principal (byte)
    //   block_start + 0x60A  →  byte de verificação (geralmente +1)
    //
    // Bug do load: se o jogador carrega um savestate antigo, o contador
    // em RAM fica menor que o do memcard. O jogo então mostra "Unable to
    // locate load data". Corrigir = gravar valor menor no memcard.

    private const int CounterOffsetA  = 0x604;
    private const int CounterOffsetB  = 0x60A;
    private const int CounterBackupDelta = 0x680;

    /// <inheritdoc/>
    public MemoryCardSaveCounter? ReadSaveCounter(byte[] data, MemoryCardSave save)
    {
        int blockStart = save.StartBlock * BlockSize;
        int absA = blockStart + CounterOffsetA;
        int absB = blockStart + CounterOffsetB;
        if (absB >= data.Length) return null;

        return new MemoryCardSaveCounter(
            Save:         save,
            Counter:      data[absA],
            CounterCheck: data[absB],
            OffsetA:      absA,
            OffsetB:      absB);
    }

    /// <inheritdoc/>
    public async Task WriteSaveCounterAsync(string filePath, MemoryCardSave save, int newCounter)
    {
        if (newCounter < 0 || newCounter > 255)
            throw new ArgumentOutOfRangeException(nameof(newCounter),
                "Contador deve estar entre 0 e 255 (1 byte).");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Memory card não encontrado.", filePath);

        WriteAutoBackup(filePath);

        var bytes = await File.ReadAllBytesAsync(filePath);
        int blockStart = save.StartBlock * BlockSize;
        if (blockStart + CounterBackupDelta + CounterOffsetB >= bytes.Length)
            throw new InvalidDataException("Save block fora do arquivo — memory card inválido?");

        byte counter      = (byte)newCounter;
        byte counterCheck = (byte)((newCounter + 1) & 0xFF);

        // Main + backup half
        WriteCounterHalf(bytes, blockStart, counter, counterCheck);
        WriteCounterHalf(bytes, blockStart + CounterBackupDelta, counter, counterCheck);

        await File.WriteAllBytesAsync(filePath, bytes);
    }

    /// <summary>Grava counter+check numa metade (main ou backup) e recalcula
    /// o CRC#2 (que cobre a região onde o contador vive).</summary>
    private static void WriteCounterHalf(byte[] bytes, int halfStart, byte counter, byte counterCheck)
    {
        bytes[halfStart + CounterOffsetA] = counter;
        bytes[halfStart + CounterOffsetB] = counterCheck;

        // CRC#2 cobre [halfStart+0x600 .. halfStart+0x7FD]
        ushort crc2 = Crc16Ccitt(bytes, halfStart + 0x600, 0x7FE - 0x600);
        bytes[halfStart + 0x7FE] = (byte)((crc2 >> 8) & 0xFF);
        bytes[halfStart + 0x7FF] = (byte)(crc2 & 0xFF);
    }

    /// <summary>CRC16-CCITT, polynomial 0x1021, init 0x0000, MSB-first.</summary>
    private static ushort Crc16Ccitt(byte[] data, int offset, int length)
    {
        const ushort polynomial = 0x1021;
        ushort crc = 0x0000;
        for (int i = 0; i < length; i++)
        {
            crc ^= (ushort)(data[offset + i] << 8);
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ polynomial);
                else
                    crc <<= 1;
            }
        }
        return crc;
    }

    private static void WriteCounts(
        byte[] bytes, int offset, MemoryCardTrunk trunk,
        IReadOnlyDictionary<int, int> counts)
    {
        for (int i = 0; i < CardCount; i++)
        {
            int cardId = i + 1;
            int v = counts.TryGetValue(cardId, out var c) ? c : trunk.Counts[i];
            if (v < 0)  v = 0;
            if (v > 99) v = 99;

            if (trunk.BytesPerEntry == 1)
                bytes[offset + i] = (byte)v;
            else
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(offset + i * 2, 2), (ushort)v);
        }
    }

    /// <summary>Cria <c>filePath.bak-yyyyMMdd_HHmmss</c> e mantém só os 5 mais recentes.</summary>
    private static void WriteAutoBackup(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath) ?? ".";
            var name = Path.GetFileName(filePath);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(dir, $"{name}.bak-{stamp}");
            File.Copy(filePath, backupPath, overwrite: false);

            // Limpa backups antigos (mantém os 5 mais recentes)
            var oldBackups = Directory.GetFiles(dir, $"{name}.bak-*")
                .OrderByDescending(p => p)
                .Skip(5);
            foreach (var old in oldBackups)
            {
                try { File.Delete(old); } catch { /* ignora */ }
            }
        }
        catch { /* falha de backup não bloqueia o save */ }
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
