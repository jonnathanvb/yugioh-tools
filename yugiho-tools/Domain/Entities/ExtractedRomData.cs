namespace yugiho_tools.Domain.Entities;

/// <summary>
/// DTO serializável que representa todos os dados extraídos de um MOD,
/// Persistido em <c>MOD/{slug}/data.json</c> após o cadastro do MOD — leituras
/// posteriores carregam direto desse JSON, sem rodar o parser binário.
///
/// Campos que ainda não conseguimos extrair (rituals detalhados,
/// codeStars, frameColor) ficam nulos / vazios — o app trata como
/// "ausente" sem quebrar.
/// </summary>
public class ExtractedRomData
{
    /// <summary>Versão do schema. Bump quando mudar campos pra invalidar
    /// caches antigos sem precisar deletar mão.</summary>
    public int Version { get; set; } = 1;
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
    public string ModSlug { get; set; } = "";

    public List<ExtractedCard>    Cards    { get; set; } = new();
    public List<ExtractedDuelist> Duelists { get; set; } = new();

    /// <summary>Resultado da auto-detecção de offsets (pra debug).</summary>
    public ExtractionDiagnostics Diagnostics { get; set; } = new();

    /// <summary>
    /// Posições dos slots da moldura (ATK/DEF/nome/atributo/estrelas)
    /// lidas do SLUS no momento da extração. Persistidas no JSON para
    /// que o app NÃO precise mais do SLUS depois de extraído.
    /// </summary>
    public FramePositions Positions { get; set; } = new();
}

public class FramePositions
{
    public int ArtX  { get; set; } = 19;
    public int ArtY  { get; set; } = 50;
    public int NameX { get; set; } = 12;
    public int NameY { get; set; } = 14;
    public int AtkX  { get; set; } = 97;
    public int AtkY  { get; set; } = 157;
    public int DefX  { get; set; } = 97;
    public int DefY  { get; set; } = 171;
    public int StX   { get; set; } = 119;
    public int StY   { get; set; } = 32;
    public int AttrX { get; set; } = 110;
    public int AttrY { get; set; } = 13;

    // Posições dos rótulos "ATK"/"DEF" como texto (opcional — só renderiza
    // se Mod.ShowAtkDefLabels = true). Defaults colocam à esquerda dos
    // valores numéricos no layout NTSC-U original; o usuário ajusta no
    // dialog se o MOD usa layout diferente.
    public int AtkLabelX { get; set; } = 70;
    public int AtkLabelY { get; set; } = 157;
    public int DefLabelX { get; set; } = 70;
    public int DefLabelY { get; set; } = 171;

    // Tamanho de fonte em "cqw" (percentual da largura do container do
    // frame). Defaults batem com .fm-frame-name / .fm-frame-atk no app.css.
    // MODs com frames de proporção diferente costumam precisar ajustar.
    public double NameFontSize         { get; set; } = 5.0;
    public double AtkDefValueFontSize  { get; set; } = 6.5;
    public double AtkDefLabelFontSize  { get; set; } = 6.5;
}

public class ExtractedCard
{
    public int    Id          { get; set; }      // 1-based
    public string Name        { get; set; } = "";
    public string Description { get; set; } = "";
    public int    Atk         { get; set; }
    public int    Def         { get; set; }
    public int    Lvl         { get; set; }
    public int    Type        { get; set; }
    public int    Attribute   { get; set; }
    public int    Guardian1   { get; set; }
    public int    Guardian2   { get; set; }

    /// <summary>Lista de IDs de cartas que podem ser equipadas nesta
    /// (quando esta é monstro). 0-based no JSON original; aqui mantemos
    /// 0-based pra simetria com a parsing.</summary>
    public List<int> Equips       { get; set; } = new();
    public List<int> EquipTargets { get; set; } = new();

    /// <summary>Pares de fusão (material + result), 0-based.</summary>
    public List<ExtractedFusion> Fusions { get; set; } = new();
    
    public bool IsRitual { get; set; }
    public bool IsFusion { get; set; }
    /// <summary>0 = livre. 1-3 = cópias máximas no deck.</summary>
    public int  Limited  { get; set; }
    /// <summary>Password TCG (8 dígitos). String pra preservar zeros à esquerda.</summary>
    public string Password { get; set; } = "";
    /// <summary>Custo em Star Chips na loja.</summary>
    public int  CostStars { get; set; }

    /// <summary>Receitas de ritual onde esta carta é o resultado.
    /// Cada receita lista os ingredientes 0-based.</summary>
    public List<ExtractedRitual> Rituals { get; set; } = new();

    /// <summary>Traduções da descrição por código de idioma (ex.: "pt",
    /// "es"). Geradas pelo serviço de tradução quando configurado.
    /// O idioma original (em) fica em <see cref="Description"/> e não
    /// é repetido aqui pra evitar duplicação.</summary>
    public Dictionary<string, string> DescriptionsByLanguage { get; set; } = new();
}

public class ExtractedFusion
{
    /// <summary>Material acompanhante (essa carta é o outro material).</summary>
    public int Material { get; set; }
    public int Result   { get; set; }
}

public class ExtractedRitual
{
    public List<int> Ingredients { get; set; } = new();
    public int       Result      { get; set; }
}

public class ExtractedDuelist
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Pesos por carta (722 entradas). Soma típica = 40 (deck).</summary>
    public ushort[] Deck   { get; set; } = [];
    /// <summary>Pesos por carta (722 entradas). Soma típica = 2048 ou 4096.</summary>
    public ushort[] SaPow  { get; set; } = [];
    public ushort[] BcdPow { get; set; } = [];
    public ushort[] SaTec  { get; set; } = [];
}

public class ExtractionDiagnostics
{
    public int  DuelistOffsetUsed         { get; set; }
    public bool DuelistOffsetAutoDetected { get; set; }
    /// <summary>Soma do primeiro pool (pow) do primeiro duelista — útil
    /// pra diagnosticar se os drops estão "bons" (≈2048 ou 4096) ou
    /// criptografados pelo criptoDrop (≈2512-2542).</summary>
    public int FirstDuelistPowSum         { get; set; }
    public bool DropsAppearScrambled       { get; set; }
}
