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
    /// <summary>
    /// Último offset usado pra ler a tabela de duelistas no parse mais
    /// recente. Igual ao do <see cref="RomOffsetProfile"/> em mods
    /// padrão; diferente quando a auto-detecção entrou em ação. Exposto
    /// pra páginas de debug poderem mostrar o valor real.
    /// </summary>
    public static int LastDuelistOffsetUsed { get; private set; }

    /// <summary>True quando o último parse usou o offset auto-detectado
    /// em vez do configurado no profile.</summary>
    public static bool LastDuelistOffsetWasAutoDetected { get; private set; }

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
        progress?.Report(28);

        ParseRituals(mrg, cards, p);
        progress?.Report(30);

        // Posições dos slots da moldura (ATK/DEF/nome/atributo) ficam
        // no SLUS — leio aqui pra que a UI possa renderizar overlays
        // exatamente onde o jogo coloca o texto.
        Application.Helpers.CardFrameRegistry.LoadPositions(game);
        // Marca quais CardIds são resultado de fusão — necessário pro
        // mapeamento "fusão → frame roxo" em MappingForCard.
        Application.Helpers.CardFrameRegistry.LoadFusionResults(cards);

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
        // Decodifica os "espelhos" (frames de carta) extraídos do MRG e
        // popula o registry compartilhado. Sem isso o modo MOD da UI
        // mostra só a arte 102×96 sem moldura.
        Application.Helpers.CardFrameRegistry.Load(mrg);
        progress?.Report(100);
    }

    // ── Text decoding ───────────────────────────────────────────────────────
    /// <summary>
    /// Decodifica uma string FM. Bytes especiais:
    /// <list type="bullet">
    /// <item><c>0xFF</c>: terminador.</item>
    /// <item><c>0xFE</c>: line break (vira espaço depois do <c>PrettifyDescription</c>).</item>
    /// <item><c>0xFD AA BB</c>: <strong>POINTER pra outra string</strong>
    /// — compressão por referência. Endereço alvo:
    /// <c>(addr &amp; 0xFFFF0000) | (uint16(AA,BB) + 0x800)</c>. Após resolver
    /// o pointer, a string atual é encerrada (não há texto após o pointer).</item>
    /// <item><c>0xF8 KK ARG</c>: escape de cor/fonte (3 bytes total). Pulado.</item>
    /// </list>
    /// O algoritmo é o mesmo usado pelo pra ler descrições de mods
    /// como TLM e Remaster. Sem o pointer 0xFD, descrições vinham truncadas
    /// ou emendadas com texto de outras cartas.
    /// </summary>
    private static string ReadName(
        ReadOnlySpan<byte> game, int addr, string[] charList, RomOffsetProfile p,
        int maxBytes = 600, int recursionDepth = 0)
    {
        // Evita loop infinito se um mod tiver pointers circulares.
        if (recursionDepth > 4) return "";

        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < maxBytes; i++)
        {
            int idx = addr + i;
            if (idx < 0 || idx >= game.Length) break;

            byte bt = game[idx];

            if (bt == 0xFF) break;
            if (bt == 0xFE) { sb.Append('\n'); continue; }

            // 0xFD: pointer pra outra string no mesmo bank de 64KB.
            // Resolve recursivamente e termina esta string.
            if (bt == 0xFD)
            {
                if (idx + 2 >= game.Length) break;
                byte lo = game[idx + 1];
                byte hi = game[idx + 2];
                int  bank   = addr & unchecked((int)0xFFFF0000);
                int  offset = ((lo | (hi << 8)) + 0x800) & 0xFFFF;
                int  target = bank | offset;
                if (target >= 0 && target < game.Length)
                {
                    sb.Append(ReadName(game, target, charList, p,
                                       maxBytes, recursionDepth + 1));
                }
                break;
            }

            // 0xF8: escape de cor/fonte (3 bytes). Defesa: se algum arg é
            // 0xFF, é o terminador real — quebra antes de pular.
            if (bt == 0xF8)
            {
                byte arg1 = (idx + 1 < game.Length) ? game[idx + 1] : (byte)0;
                byte arg2 = (idx + 2 < game.Length) ? game[idx + 2] : (byte)0;
                if (arg1 == 0xFF || arg2 == 0xFF) break;
                i += 2;
                continue;
            }

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
            // Descrições podem ter 300+ bytes em mods (efeitos elaborados,
            // múltiplas linhas) — 200 do default cortava no meio.
            cards[i].Description = ReadName(
                game, p.DescTable + (off - p.DescPtrBase), charList, p, maxBytes: 600);
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

    // ── Rituals (5 × uint16 LE = 10 bytes per entry) ───────────────────────
    // Engenharia reversa do TEA Online Ritual Editor:
    //   for each ritual entry e starting at p.Rituals:
    //     ritualSpell = u16[+0] & 0x3FF       (1-based card id)
    //     req1        = u16[+2]               (1-based)
    //     req2        = u16[+4]               (1-based)
    //     req3        = u16[+6]               (1-based)
    //     result      = u16[+8]               (1-based)
    //   loop termina quando todos os 5 são 0.
    // Resultado é agregado no <see cref="Card.Rituals"/> da carta-result.
    private const int RitualEntrySize = 10;
    private const int CardIdMask10Bit = 0x3FF;
    /// <summary>Limite defensivo pra não varrer o MRG inteiro se o
    /// terminador zero estiver corrompido. FM clássico tem ~36 rituais;
    /// 256 dá folga pra qualquer mod.</summary>
    private const int MaxRitualEntries = 256;

    private static void ParseRituals(byte[] mrg, List<Card> cards, RomOffsetProfile p)
    {
        if (p.Rituals < 0 || p.Rituals + RitualEntrySize > mrg.Length) return;

        for (int e = 0; e < MaxRitualEntries; e++)
        {
            int off = p.Rituals + e * RitualEntrySize;
            if (off + RitualEntrySize > mrg.Length) break;

            int spell  = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(off + 0, 2)) & CardIdMask10Bit;
            int req1   = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(off + 2, 2));
            int req2   = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(off + 4, 2));
            int req3   = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(off + 6, 2));
            int result = BinaryPrimitives.ReadUInt16LittleEndian(mrg.AsSpan(off + 8, 2));

            // Sentinel: tudo zero = fim da tabela.
            if (spell == 0 && req1 == 0 && req2 == 0 && req3 == 0 && result == 0) break;

            // Indexa por card 0-based; ROM armazena 1-based.
            int resultIdx = result - 1;
            if (resultIdx < 0 || resultIdx >= cards.Count) continue;

            cards[resultIdx].Rituals.Add(new RitualRecipe
            {
                Ingredients = new List<int> { spell - 1, req1 - 1, req2 - 1, req3 - 1 },
                Result      = resultIdx,
            });
            cards[resultIdx].IsRitual = true;
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

        // 1) Tenta o offset configurado (FM-US default ou override do profile).
        // 2) Se nenhum deck somou plausivelmente, escaneia o MRG procurando
        //    a tabela em outro endereço — cobre mods que relocaram (LMFV,
        //    Remaster, etc.).
        int duelistBase = p.DuelistData;
        bool autoDetected = false;
        if (!LooksLikeValidDuelistBase(mrg, duelistBase, p))
        {
            // Antes de fazer scan completo, testa candidatos óbvios:
            // mods conhecidos (TLM) usam DuelistData - 0x1800. Tentar
            // primeiro evita varredura desnecessária.
            int[] candidates = {
                duelistBase - 0x1800,   // TLM: tabela 1 slot ANTES
                duelistBase + 0x1800,   // hipotético: 1 slot DEPOIS
            };

            foreach (var c in candidates)
            {
                if (c >= 0 && LooksLikeValidDuelistBase(mrg, c, p))
                {
                    duelistBase  = c;
                    autoDetected = true;
                    break;
                }
            }

            // Fallback: varredura completa do MRG procurando a assinatura.
            if (!autoDetected)
            {
                var detected = DuelistOffsetDetector.Detect(mrg);
                if (detected.HasValue)
                {
                    duelistBase  = detected.Value;
                    autoDetected = true;
                }
            }
        }
        LastDuelistOffsetUsed             = duelistBase;
        LastDuelistOffsetWasAutoDetected  = autoDetected;

        for (int i = 0; i < p.DuelistCount; i++)
        {
            var d = new Duelist { Id = i };

            int addr     = p.NameTable + ptrs[i]     - p.NamePtrBase;
            int nextAddr = p.NameTable + ptrs[i + 1] - p.NamePtrBase;
            int maxLen   = Math.Max(2, Math.Min(64, nextAddr - addr));
            d.Name = ReadName(game, addr, charList, p, maxLen).TrimEnd();

            int baseAddr = duelistBase + p.DuelistStride * i;
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

    /// <summary>Verifica rapidamente se o offset configurado realmente
    /// aponta pra tabela de duelistas — basta o deck do duelista 0
    /// somar dentro de uma faixa plausível (~2048).</summary>
    private static bool LooksLikeValidDuelistBase(
        byte[] mrg, int baseOff, RomOffsetProfile p)
    {
        int deckOff = baseOff + p.DuelistDeckOff;
        if (deckOff < 0 || deckOff + 1444 > mrg.Length) return false;
        long sum = 0;
        var span = mrg.AsSpan(deckOff, 1444);
        for (int i = 0; i < 722; i++)
            sum += BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(i * 2, 2));
        return sum >= 1500 && sum <= 2800;
    }

    private static void ClearIfImplausible(ushort[] pool)
    {
        long sum = 0;
        foreach (var v in pool) sum += v;
        // FM-US usa denominador 2048; alguns mods (incluindo Remaster
        // e variantes do TLM) usam 4096. Janela 512..8192 cobre os dois,
        // recusando lixo de offsets errados (que somam dezenas de milhões).
        if (sum < 512 || sum > 8192) Array.Clear(pool, 0, pool.Length);
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
            // BGR top-down (3 bytes/px) — usado para gerar o BMP.
            var bgr  = new byte[ThumbPixelCount * 3];
            for (int px = 0; px < ThumbPixelCount; px++)
            {
                ushort color = BinaryPrimitives.ReadUInt16LittleEndian(clut.Slice(pixels[px] * 2, 2));
                byte r = (byte)((color & 31) * 8);
                byte g = (byte)((color >> 5 & 31) * 8);
                byte b = (byte)((color >> 10 & 31) * 8);
                gray[px] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);

                int o = px * 3;
                bgr[o + 0] = b;
                bgr[o + 1] = g;
                bgr[o + 2] = r;
            }
            cards[i].ThumbnailPixels = gray;

            // Arte HD (102×96) — mesma slot de 14336 bytes da thumbnail,
            // só que começando em outro endereço base (CardArt). É o que
            // o jogo mostra no detalhe da carta (Triangle).
            int artPixelCount = p.CardArtWidth * p.CardArtHeight;
            int artBase = p.CardArt + i * p.ThumbnailStride;
            if (artBase + artPixelCount + ClutSize <= mrg.Length)
            {
                var artPixels = mrg.AsSpan(artBase, artPixelCount);
                var artClut   = mrg.AsSpan(artBase + artPixelCount, ClutSize);
                var artBgr    = new byte[artPixelCount * 3];
                for (int px = 0; px < artPixelCount; px++)
                {
                    ushort color = BinaryPrimitives.ReadUInt16LittleEndian(artClut.Slice(artPixels[px] * 2, 2));
                    byte r = (byte)((color & 31) * 8);
                    byte g = (byte)((color >> 5  & 31) * 8);
                    byte b = (byte)((color >> 10 & 31) * 8);
                    int o = px * 3;
                    artBgr[o + 0] = b;
                    artBgr[o + 1] = g;
                    artBgr[o + 2] = r;
                }
                cards[i].ModImageDataUrl = yugiho_tools.Application.Helpers.PngEncoder
                    .ToDataUrl24(artBgr, p.CardArtWidth, p.CardArtHeight);
            }
            else
            {
                // Fallback: se o offset HD estourar (ROM pequeno/exótico),
                // usa o thumbnail mesmo — melhor algo do que nada.
                cards[i].ModImageDataUrl = yugiho_tools.Application.Helpers.PngEncoder
                    .ToDataUrl24(bgr, ThumbWidth, ThumbHeight);
            }
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
