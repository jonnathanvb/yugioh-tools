using System.Text.Json;
using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Importa um JSON 
/// Útil quando o usuário tem um JSON externo com dados que nosso parser
/// não conseguiria extrair — drops criptografados em mods protegidos,
/// por exemplo.
///
/// Layout esperado (lab):
/// <code>
/// { "cards":    [{ "id", "atk", "def", "guardian1", "guardian2",
///                  "type", "lvl", "attributeId", "description", "name",
///                  "equips":[...], "fusions":[{"$1","$2","$3"}], ... }],
///   "duelists": [{ "id", "name", "uniqueDropId",
///                  "drops":[{"cardId","dropRate","duelistId","type"}] }] }
/// </code>
/// </summary>
public class LabJsonImporter
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ExtractedRomData> ImportAsync(string jsonPath)
    {
        await using var stream = File.OpenRead(jsonPath);
        var lab = await JsonSerializer.DeserializeAsync<LabRoot>(stream, Opts)
                  ?? throw new InvalidDataException("JSON vazio ou ilegível.");

        var cards = (lab.Cards ?? new())
            .Select(ConvertCard)
            .ToList();

        var duelists = (lab.Duelists ?? new())
            .Select(ConvertDuelist)
            .ToList();

        return new ExtractedRomData
        {
            Cards    = cards,
            Duelists = duelists,
            // Posições e diagnostics ficam com defaults — o JSON do lab
            // não traz esses campos. Vão ser sobrescritos pelo parser
            // quando rodamos a extração de imagens.
        };
    }

    private static ExtractedCard ConvertCard(LabCard c) => new()
    {
        Id          = c.Id,
        Atk         = c.Atk ?? 0,
        Def         = c.Def ?? 0,
        Lvl         = c.Lvl ?? 0,
        Type        = c.Type ?? 0,
        Attribute   = c.AttributeId ?? 0,
        Guardian1   = c.Guardian1 ?? 0,
        Guardian2   = c.Guardian2 ?? 0,
        Description = c.Description ?? "",
        // Lab armazena nome com prefixo de cor "|<digit>Texto" — descarta
        // os 2 primeiros caracteres se começar com "|".
        Name        = StripColorTag(c.Name),
        // Lab usa IDs 1-based em equips/fusions/rituals; nosso modelo
        // interno (que vem do RomParser binário) é 0-based. Subtraímos 1
        // aqui pra que o resto do app trate tudo de forma uniforme.
        Equips      = (c.Equips ?? new()).Select(e => e - 1).ToList(),
        Fusions     = (c.Fusions ?? new())
                        .Select(f => new ExtractedFusion
                        {
                            Material = f.S2 - 1,
                            Result   = f.S3 - 1,
                        })
                        .ToList(),

        IsRitual    = c.IsRitual ?? false,
        // IsFusion: derivado depois de carregar todas as cartas — uma carta
        // é "fusion" se aparece como result de alguma fusão. Aqui marcamos
        // só se o lab forneceu o flag explicitamente.
        IsFusion    = c.IsFusion ?? false,
        Limited     = c.Limited ?? 0,
        Password    = c.CodeStars?.Password ?? "",
        CostStars   = c.CodeStars?.Cost ?? 0,
        // Lab repete a mesma receita várias vezes (uma por slot do shop
        // ou por duelista que tem o ritual no deck), então deduplicamos
        // pela combinação completa de ingredientes + resultado pra não
        // poluir a UI com linhas idênticas.
        Rituals     = (c.Rituals ?? new())
                        .Select(ConvertRitual)
                        .DistinctBy(r =>
                            $"{r.Result}|{string.Join(",", r.Ingredients)}")
                        .ToList(),
    };

    /// <summary>
    /// Converte uma receita do lab em <see cref="ExtractedRitual"/>.
    /// Shape no JSON do lab:
    /// <c>{ "magicCard", "card1", "card2", "card3", "result" }</c> — a
    /// fórmula é "magicCard + card1 + card2 + card3 = result". Os IDs no
    /// lab são 1-based; convertemos pra 0-based pra ficar consistente
    /// com o resto do modelo (RomParser binário também é 0-based).
    /// </summary>
    private static ExtractedRitual ConvertRitual(LabRitual r) => new()
    {
        Ingredients = new List<int>
        {
            r.MagicCard - 1,
            r.Card1     - 1,
            r.Card2     - 1,
            r.Card3     - 1,
        },
        Result = r.Result - 1,
    };

    private static ExtractedDuelist ConvertDuelist(LabDuelist d)
    {
        var deck   = new ushort[722];
        var saPow  = new ushort[722];
        var bcdPow = new ushort[722];
        var saTec  = new ushort[722];

        foreach (var entry in d.Drops ?? new())
        {
            int idx = (entry.CardId ?? 0) - 1;  // lab usa 1-based; nossos arrays 0-based
            if (idx < 0 || idx >= 722) continue;
            ushort w = (ushort)Math.Min(entry.DropRate ?? 0, ushort.MaxValue);
            switch (entry.Type)
            {
                case "deck": deck[idx]   = w; break;
                case "pow":  saPow[idx]  = w; break;
                case "bcd":  bcdPow[idx] = w; break;
                case "tec":  saTec[idx]  = w; break;
            }
        }

        return new ExtractedDuelist
        {
            // Lab usa duelist id 1-based; nosso é 0-based.
            Id     = d.Id - 1,
            Name   = StripColorTag(d.Name),
            Deck   = deck,
            SaPow  = saPow,
            BcdPow = bcdPow,
            SaTec  = saTec,
        };
    }

    private static string StripColorTag(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        // Lab encoding: "|<dígito><texto>" representa cor + texto.
        if (name.Length >= 2 && name[0] == '|' && char.IsDigit(name[1]))
            return name[2..];
        return name;
    }

    // ── Lab JSON shape ─────────────────────────────────────────────────
    private sealed class LabRoot
    {
        public List<LabCard>?    Cards    { get; set; }
        public List<LabDuelist>? Duelists { get; set; }
    }

    /// <summary>Lab JSON tem campos que podem chegar com <c>null</c>
    /// (ex.: <c>"limited": null</c>). Tudo numérico/booleano vira tipo
    /// nullable pra que o desserializador não exploda. Casos raros
    /// onde o lab usa tipos diferentes (ex.: <c>type</c> chega como
    /// string em alguns dumps) são tolerados via
    /// <c>JsonNumberHandling.AllowReadingFromString</c>.</summary>
    private sealed class LabCard
    {
        public int Id { get; set; }
        public int? Atk { get; set; }
        public int? Def { get; set; }
        public int? Guardian1 { get; set; }
        public int? Guardian2 { get; set; }
        public int? Type { get; set; }
        public int? Lvl { get; set; }
        public int? AttributeId { get; set; }
        public string? Description { get; set; }
        public string? Name { get; set; }
        public List<int>? Equips { get; set; }
        public List<LabFusion>? Fusions { get; set; }

        public bool? IsRitual { get; set; }
        public bool? IsFusion { get; set; }
        public int?  Limited  { get; set; }
        public List<LabRitual>? Rituals { get; set; }
        public LabCodeStars? CodeStars { get; set; }
    }

    /// <summary>
    /// "codeStars": { "$1": password, "$2": cost }. Lab usa essa estrutura
    /// pra empacotar password TCG + custo na loja em um único objeto. O
    /// password pode vir como number ou string no JSON — usamos JsonElement
    /// pra tolerar os dois e convertemos pra string no getter.
    /// </summary>
    private sealed class LabCodeStars
    {
        [System.Text.Json.Serialization.JsonPropertyName("$1")]
        public System.Text.Json.JsonElement PasswordRaw { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("$2")]
        public int Cost { get; set; }

        public string Password => PasswordRaw.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => PasswordRaw.GetString() ?? "",
            System.Text.Json.JsonValueKind.Number => PasswordRaw.GetInt64().ToString(),
            _                                     => "",
        };
    }

    /// <summary>
    /// Receita de ritual no formato do lab. Diferente de fusão (que usa
    /// "$1/$2/$3"), ritual tem campos nomeados explícitos: a magic card
    /// que invoca + 3 monstros ofertados + o resultado. Total: 4
    /// ingredientes pra produzir o monstro de ritual.
    /// </summary>
    private sealed class LabRitual
    {
        public int MagicCard { get; set; }
        public int Card1     { get; set; }
        public int Card2     { get; set; }
        public int Card3     { get; set; }
        public int Result    { get; set; }
    }

    private sealed class LabFusion
    {
        // JSON propriedades "$1","$2","$3" — material1, material2, result.
        // System.Text.Json mapeia automaticamente quando os property names
        // batem com o JSON usando JsonPropertyName.
        [System.Text.Json.Serialization.JsonPropertyName("$1")]
        public int S1 { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("$2")]
        public int S2 { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("$3")]
        public int S3 { get; set; }
    }

    private sealed class LabDuelist
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        // uniqueDropId costuma vir null pra duelistas sem drops únicos —
        // nullable evita crash de desserialização.
        public int? UniqueDropId { get; set; }
        public List<LabDrop>? Drops { get; set; }
    }

    private sealed class LabDrop
    {
        public int?   CardId   { get; set; }
        public int?   DropRate { get; set; }
        public int?   DuelistId { get; set; }
        public string Type     { get; set; } = "";
    }
}
