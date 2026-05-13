using yugiho_tools.Domain.ValueObjects;

namespace yugiho_tools.Domain.Entities;

public class Mod
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string GameFileName { get; set; } = "SLUS_014.11";
    public string MrgFileName  { get; set; } = "WA_MRG.MRG";

    /// <summary>
    /// Template para a URL das imagens das cartas. Deve conter o placeholder
    /// <c>{id}</c>, que é substituído pelo CardId em tempo de renderização.
    /// Ex.: <c>https://www.basededatostea.xyz/img/lmfv/{id}.jpg</c>.
    /// </summary>
    public string ImageUrlTemplate { get; set; } = "";

    /// <summary>
    /// Optional offset profile name (key in Resources/Raw/offset-profiles.json).
    /// Null/empty = use defaults (original NTSC-U). Mods that relocate ROM tables
    /// reference a custom profile here.
    /// </summary>
    public string? OffsetProfile { get; set; }

    public DateTime CreatedAt  { get; set; }

    /// <summary>
    /// Fonte das imagens das cartas. Default = <c>Mod</c> (arte extraída
    /// do próprio ROM). <c>Tea</c> usa o <see cref="ImageUrlTemplate"/>
    /// pra buscar arte HD online.
    /// </summary>
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
}
