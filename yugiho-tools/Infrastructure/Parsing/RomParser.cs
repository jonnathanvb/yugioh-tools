using System.Buffers.Binary;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;
using yugiho_tools.Domain.ValueObjects;

namespace yugiho_tools.Infrastructure.Parsing;

/// <summary>
/// Reads card / fusion / equip / duelist data straight out of the FM ROM files
/// (<c>SLUS_014.11</c> + <c>WA_MRG.MRG</c>). Offsets come from a
/// <see cref="RomOffsetProfile"/>; defaults match the original NTSC-U release and
/// are reused by mods that didn't relocate their tables (e.g. LMFV).
/// </summary>
public class RomParser : IRomParser
{
    private const int CardCount = 722;
    private const int ThumbWidth      = 40;
    private const int ThumbHeight     = 32;
    private const int ThumbPixelCount = ThumbWidth * ThumbHeight;
    private const int ClutSize        = 256 * 2;

    public async Task<RomData> ParseAsync(
        string gameFilePath,
        string mrgFilePath,
        RomOffsetProfile? profile = null,
        IProgress<int>? progress = null)
    {
        var p = profile ?? RomOffsetProfile.Default;

        byte[] game = await File.ReadAllBytesAsync(gameFilePath);
        byte[] mrg  = await File.ReadAllBytesAsync(mrgFilePath);
        progress?.Report(10);

        string[] charList = await LoadCharTableAsync(gameFilePath);
        progress?.Report(15);

        var cards = ParseCardData(game, mrg, charList, p);
        progress?.Report(25);

        var duelists = ParseDuelists(game, mrg, charList, p);
        progress?.Report(30);

        return new RomData(cards, duelists);
    }

    public async Task LoadThumbnailsAsync(
        IReadOnlyList<Card> cards,
        string mrgFilePath,
        RomOffsetProfile? profile = null,
        IProgress<int>? progress = null)
    {
        var p = profile ?? RomOffsetProfile.Default;
        byte[] mrg = await File.ReadAllBytesAsync(mrgFilePath);
        ParseThumbnails(mrg, cards, p, progress);
        progress?.Report(100);
    }

    // ── Text decoding ───────────────────────────────────────────────────────
    private static string ReadName(
        ReadOnlySpan<byte> game, int addr, string[] charList, RomOffsetProfile p,
        int maxBytes = 200)
    {
        var sb = new System.Text.StringBuilder();
        var ctrl3 = p.TextControlCodes3;

        for (int i = 0; i < maxBytes; i++)
        {
            int idx = addr + i;
            if (idx < 0 || idx >= game.Length) break;

            byte bt = game[idx];

            if (bt == 0xFF) break;          // string terminator
            if (bt == 0xFE) { sb.Append('\n'); continue; }

            // Control codes (3-byte: byte + 2 args) — used by mods for color/font escapes.
            if (Array.IndexOf(ctrl3, bt) >= 0) { i += 2; continue; }

            string? c = bt < charList.Length ? charList[bt] : null;
            if (c is { Length: > 0 }) sb.Append(c);
        }

        return PrettifyDescription(sb.ToString());
    }

    /// <summary>
    /// Cleans up multi-line description text: collapses runs of spaces, joins
    /// soft line breaks back into a single line so things like
    /// <c>"Cannot be destroyed\nby card effects."</c> render naturally.
    /// </summary>
    private static string PrettifyDescription(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        // Replace newlines with spaces and collapse repeated whitespace.
        var collapsed = raw.Replace("\r", "").Replace('\n', ' ');
        var parts = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    // ── Cards (attribs, names, descriptions, fusions, equips) ──────────────
    private static List<Card> ParseCardData(
        byte[] game, byte[] mrg, string[] charList, RomOffsetProfile p)
    {
        var cards = new List<Card>(CardCount);
        for (int i = 0; i < CardCount; i++) cards.Add(new Card());

        int addr = p.CardAttribs;
        for (int i = 0; i < CardCount; i++)
        {
            uint d = BinaryPrimitives.ReadUInt32LittleEndian(game.AsSpan(addr, 4));
            addr += 4;
            cards[i].CardId        = i + 1;
            cards[i].Attack        = (int)(d & 0x1FF) * 10;
            cards[i].Defense       = (int)(d >> 9  & 0x1FF) * 10;
            cards[i].GuardianStar1 = (int)(d >> 18 & 0xF);
            cards[i].GuardianStar2 = (int)(d >> 22 & 0xF);
            cards[i].CardType      = (int)(d >> 26 & 0x1F);
        }

        addr = p.LevelAttr;
        for (int i = 0; i < CardCount; i++)
        {
            byte b = game[addr++];
            cards[i].Level     = b & 0xF;
            cards[i].Attribute = (b >> 4) & 0xF;
        }

        for (int i = 0; i < CardCount; i++)
        {
            int num  = BinaryPrimitives.ReadUInt16LittleEndian(game.AsSpan(p.NamePtrs + i * 2, 2));
            cards[i].Name = ReadName(game, p.NameTable + num - p.NamePtrBase, charList, p);
        }

        for (int i = 0; i < CardCount; i++)
        {
            int off  = BinaryPrimitives.ReadUInt16LittleEndian(game.AsSpan(p.DescPtrs + i * 2, 2));
            cards[i].Description = ReadName(game, p.DescTable + (off - p.DescPtrBase), charList, p);
        }

        ParseFusions(mrg, cards, p);
        ParseEquips (mrg, cards, p);
        return cards;
    }

    private static void ParseFusions(byte[] mrg, List<Card> cards, RomOffsetProfile p)
    {
        var fuseDat = mrg.AsSpan(p.Fusions, p.FusionBlockSize);

        for (int i = 0; i < CardCount; i++)
        {
            int position = i * 2 + 2;
            int num = BinaryPrimitives.ReadUInt16LittleEndian(fuseDat.Slice(position, 2));
            position = num & 0xFFFF;
            if (position == 0) continue;

            int fusionAmt = fuseDat[position++];
            if (fusionAmt == 0) fusionAmt = 511 - fuseDat[position++];

            int num2 = fusionAmt;
            while (num2 > 0)
            {
                byte b0 = fuseDat[position], b1 = fuseDat[position + 1],
                     b2 = fuseDat[position + 2], b3 = fuseDat[position + 3],
                     b4 = fuseDat[position + 4];
                position += 5;

                cards[i].FusionMaterials.Add(((b0 & 3) << 8 | b1) - 1);
                cards[i].FusionResults  .Add(((b0 >> 2 & 3) << 8 | b2) - 1);
                num2--;
                if (num2 <= 0) continue;

                cards[i].FusionMaterials.Add(((b0 >> 4 & 3) << 8 | b3) - 1);
                cards[i].FusionResults  .Add(((b0 >> 6 & 3) << 8 | b4) - 1);
                num2--;
            }
            cards[i].FusionCount = cards[i].FusionMaterials.Count;
        }
    }

    /// <summary>
    /// Equip-compatibility table. Variable-length records:
    /// <c>[equip_id u16] [count u16] [monster_id u16]×count</c>, terminated by
    /// <c>equip_id == 0</c>. IDs are 1-based on disk.
    /// </summary>
    private static void ParseEquips(byte[] mrg, List<Card> cards, RomOffsetProfile p)
    {
        int pos = p.Equips;
        int end = Math.Min(p.Equips + p.EquipBlockSize, mrg.Length);

        // Sanity-check the first record: if equip_id or count look wildly out of
        // range, the configured offset doesn't fit this mod's MRG layout — bail
        // rather than parse garbage into Equips/EquipTargets.
        if (pos + 4 > end) return;
        int firstId    = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(pos, 2));
        int firstCount = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(pos + 2, 2));
        if (firstId == 0 || firstId > CardCount || firstCount == 0 || firstCount > 300)
            return;

        while (pos + 4 <= end)
        {
            int equipId = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(pos, 2));
            if (equipId == 0) break;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(pos + 2, 2));
            pos += 4;

            // Skip the entire record if it doesn't pass sanity (out-of-range count
            // would walk off the buffer; out-of-range equip just emits no links).
            if (equipId > CardCount || count > 300)
            {
                pos += count * 2;
                continue;
            }

            int equipIdx = equipId - 1;

            for (int i = 0; i < count && pos + 2 <= end; i++, pos += 2)
            {
                int monsterId  = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(pos, 2));
                int monsterIdx = monsterId - 1;

                if ((uint)monsterIdx < CardCount && (uint)equipIdx < CardCount)
                {
                    cards[monsterIdx].Equips.Add(equipIdx);
                    cards[equipIdx]  .EquipTargets.Add(monsterIdx);
                }
            }
        }
    }

    // ── Duelists (names + decks + 3 drop pools) ────────────────────────────
    private static List<Duelist> ParseDuelists(
        byte[] game, byte[] mrg, string[] charList, RomOffsetProfile p)
    {
        var list = new List<Duelist>(p.DuelistCount);

        // Pre-load all pointers so we can use the next pointer as a length bound.
        var ptrs = new int[p.DuelistCount + 1];
        for (int i = 0; i < p.DuelistCount; i++)
        {
            int pa = p.DuelistNamePtrs + i * 2;
            ptrs[i] = pa + 2 <= game.Length
                ? BinaryPrimitives.ReadUInt16LittleEndian(game.AsSpan(pa, 2))
                : 0;
        }
        ptrs[p.DuelistCount] = ptrs[p.DuelistCount - 1] + 64; // soft cap for last

        for (int i = 0; i < p.DuelistCount; i++)
        {
            var d = new Duelist { Id = i };

            int addr     = p.NameTable + ptrs[i]     - p.NamePtrBase;
            int nextAddr = p.NameTable + ptrs[i + 1] - p.NamePtrBase;
            int maxLen   = Math.Max(2, Math.Min(64, nextAddr - addr));
            d.Name = ReadName(game, addr, charList, p, maxLen).TrimEnd();

            int baseAddr = p.DuelistData + p.DuelistStride * i;
            ReadPool(mrg, baseAddr + p.DuelistDeckOff,   d.Deck);
            ReadPool(mrg, baseAddr + p.DuelistSaPowOff,  d.SaPow);
            ReadPool(mrg, baseAddr + p.DuelistBcdPowOff, d.BcdPow);
            ReadPool(mrg, baseAddr + p.DuelistSaTecOff,  d.SaTec);

            // Drop pools should sum to ~2048 (probability denominator). If any
            // pool's sum is wildly off, the offsets don't fit this mod's layout
            // — clear it so the UI can fall back to "no drop data".
            ClearIfImplausible(d.SaPow);
            ClearIfImplausible(d.BcdPow);
            ClearIfImplausible(d.SaTec);

            list.Add(d);
        }

        return list;
    }

    private static void ClearIfImplausible(ushort[] pool)
    {
        long sum = 0;
        foreach (var v in pool) sum += v;
        // Real FM drop pools sum to exactly 2048. Allow a bit of slack for variants
        // that may add 1 or omit padding; reject anything outside a plausible window.
        if (sum < 1024 || sum > 4096) Array.Clear(pool, 0, pool.Length);
    }

    private static void ReadPool(byte[] mrg, int offset, ushort[] dst)
    {
        if (offset < 0 || offset + dst.Length * 2 > mrg.Length) return;
        var span = mrg.AsSpan(offset, dst.Length * 2);
        for (int i = 0; i < dst.Length; i++)
            dst[i] = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(i * 2, 2));
    }

    // ── Thumbnails ──────────────────────────────────────────────────────────
    private static void ParseThumbnails(
        byte[] mrg, IReadOnlyList<Card> cards, RomOffsetProfile p, IProgress<int>? progress)
    {
        for (int i = 0; i < CardCount; i++)
        {
            if (i % 100 == 0) progress?.Report(30 + i / 10);

            int pixelStart = p.Thumbnails + i * p.ThumbnailStride;
            if (pixelStart + ThumbPixelCount + ClutSize > mrg.Length) break;

            var pixels = mrg.AsSpan(pixelStart, ThumbPixelCount);
            var clut   = mrg.AsSpan(pixelStart + ThumbPixelCount, ClutSize);

            var gray = new byte[ThumbPixelCount];
            for (int px = 0; px < ThumbPixelCount; px++)
            {
                ushort color = BinaryPrimitives.ReadUInt16LittleEndian(clut.Slice(pixels[px] * 2, 2));
                byte r = (byte)((color & 31) * 8);
                byte g = (byte)((color >> 5 & 31) * 8);
                byte b = (byte)((color >> 10 & 31) * 8);
                gray[px] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }
            cards[i].ThumbnailPixels = gray;
        }
    }

    // ── chartable.tbl loader ────────────────────────────────────────────────
    private static async Task<string[]> LoadCharTableAsync(string gameFilePath)
    {
        string dir  = Path.GetDirectoryName(gameFilePath) ?? "";
        string path = Path.Combine(dir, "chartable.tbl");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "chartable.tbl");

        var charList = new string[256];
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && int.TryParse(parts[0],
                System.Globalization.NumberStyles.HexNumber, null, out int idx))
                charList[idx] = parts[1];
        }
        return charList;
    }
}
