namespace yugiho_tools.Application.Services;

/// <summary>De onde vem o valor de um campo da carta no merge MOD+JSON.</summary>
public enum FieldSource
{
    /// <summary>Usa o valor decodificado do binário (SLUS/MRG).</summary>
    Rom = 0,

    /// <summary>Usa o valor do JSON do lab importado pelo usuário.</summary>
    Json = 1,
}

/// <summary>
/// Define, por campo, se a fonte é o ROM (binário) ou o JSON do lab. Cada
/// MOD pode preferir uma fonte diferente: alguns o JSON traz textos ricos
/// com marcadores que o ROM corrompe; outros o ROM está atualizado e o JSON
/// está desatualizado.
///
/// Defaults: <see cref="FieldSource.Json"/> para tudo — o lab geralmente é
/// a fonte mais limpa. O usuário troca pra <see cref="FieldSource.Rom"/>
/// individualmente o que achar necessário no cadastro do MOD.
///
/// Para campos que só existem no JSON (Limited, Password, CostStars), Rom
/// significa "não importa, deixa default vazio/zero".
/// </summary>
public record FieldSourceMatrix(
    FieldSource Name        = FieldSource.Json,
    FieldSource Description = FieldSource.Json,
    FieldSource Guardians   = FieldSource.Json,
    FieldSource Equips      = FieldSource.Json,
    FieldSource Fusions     = FieldSource.Json,
    FieldSource Rituals     = FieldSource.Json,
    FieldSource Limited     = FieldSource.Json,
    FieldSource Password    = FieldSource.Json,
    FieldSource CostStars   = FieldSource.Json,
    FieldSource Duelists    = FieldSource.Json)
{
    public static readonly FieldSourceMatrix DefaultAllJson = new();

    public static readonly FieldSourceMatrix DefaultAllRom = new(
        Name:        FieldSource.Rom,
        Description: FieldSource.Rom,
        Guardians:   FieldSource.Rom,
        Equips:      FieldSource.Rom,
        Fusions:     FieldSource.Rom,
        Rituals:     FieldSource.Rom,
        Limited:     FieldSource.Rom,
        Password:    FieldSource.Rom,
        CostStars:   FieldSource.Rom,
        Duelists:    FieldSource.Rom);
}
