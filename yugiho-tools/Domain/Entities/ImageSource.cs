namespace yugiho_tools.Domain.Entities;

/// <summary>
/// De onde vem a arte das cartas pra renderização. Cada MOD carrega
/// sua própria preferência — alguns MODs trazem arte HD via TEAONLINE,
/// outros só têm thumbnails extraídos do ROM.
/// </summary>
public enum ImageSource
{
    /// <summary>Imagens hospedadas no basededatostea.xyz (TEAONLINE).
    /// Resolução alta, depende de internet.</summary>
    Tea = 0,

    /// <summary>Arte extraída do próprio ROM (thumbnail 40×32 + frame
    /// composto com overlays HTML). Offline, fiel ao jogo, baixa-res.</summary>
    Mod = 1,
}
