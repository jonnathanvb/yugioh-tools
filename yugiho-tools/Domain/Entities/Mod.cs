namespace yugiho_tools.Domain.Entities;

public class Mod
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";

    /// <summary>
    /// Template (legado) pra URL das imagens das cartas online. Hoje as
    /// imagens vêm dentro do ZIP do MOD em <c>cards/{variant}/</c>;
    /// este campo só é consultado como fallback se a variante escolhida
    /// não tiver a imagem da carta.
    /// </summary>
    public string ImageUrlTemplate { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    /// <summary>Fonte das imagens. Mantido por compat de schema com o
    /// mods.json antigo — o app sempre usa <c>Mod</c> hoje.</summary>
    public ImageSource ImageSource { get; set; } = ImageSource.Mod;

    /// <summary>
    /// Override de posições dos slots da moldura (nome, ATK, DEF, estrelas,
    /// atributo). Null = usa as posições extraídas do SLUS no
    /// <see cref="ExtractedRomData.Positions"/>. Setar aqui permite o
    /// usuário ajustar manualmente quando o MOD tem layout não-padrão.
    /// </summary>
    public FramePositions? FrameOverrides { get; set; }

    /// <summary>
    /// Renderiza os rótulos "ATK" e "DEF" como texto separado sobre o
    /// frame. Alguns MODs (especialmente customizados) trazem o frame
    /// sem essas palavras impressas — o usuário precisa adicioná-las
    /// via overlay HTML pra ficar legível.
    ///
    /// Quando true, posiciona usando <see cref="FramePositions.AtkLabelX"/>
    /// /<c>Y</c> e <c>DefLabelX/Y</c>.
    /// </summary>
    public bool ShowAtkDefLabels { get; set; }

    /// <summary>Variante de imagem da carta (subpasta dentro de
    /// <c>cards/</c>) usada na visualização principal. Valores típicos:
    /// <c>sd</c> (JPEG, sempre presente), <c>hd</c> (PNG hi-res, opcional),
    /// <c>mini_hd</c> (PNG renderizado pelo software com a moldura).
    /// Default <c>sd</c> — sempre obrigatório no pacote.</summary>
    public string CardImageVariant { get; set; } = "sd";

    /// <summary>Variante de mini (40×32 ou 102×96) usada no grafo de
    /// fusão. Valores: <c>mini_sd</c> (sprite original, default) ou
    /// <c>mini_hd</c> (versão renderizada com a moldura).</summary>
    public string FusionMiniVariant { get; set; } = "mini_sd";
}

/// <summary>Constantes pra nomes de variantes de carta — evita typos
/// nas comparações espalhadas pelo código.</summary>
public static class CardVariants
{
    public const string Sd      = "sd";
    public const string Hd      = "hd";
    public const string MiniSd  = "mini_sd";
    public const string MiniHd  = "mini_hd";

    /// <summary>Variantes válidas pra display principal (Mod.CardImageVariant).</summary>
    public static readonly string[] MainOptions = [MiniHd, Sd, Hd];

    /// <summary>Variantes válidas pro grafo de fusão (Mod.FusionMiniVariant).</summary>
    public static readonly string[] MiniOptions = [MiniSd, MiniHd];
}
