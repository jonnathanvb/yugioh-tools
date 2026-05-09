using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Application.Helpers;

public static class CardImage
{
    /// <summary>
    /// Quando true, <see cref="Url"/> tenta resolver pela arte extraída do
    /// próprio ROM (<see cref="ModUrls"/>). Falha-segura: se a entrada não
    /// existir no registry — por exemplo antes do load — cai pro template
    /// online (TEA/MOD URL externa).
    /// </summary>
    public static bool UseModImages { get; set; }

    /// <summary>
    /// Cache <c>cardId → data URL</c> populado por
    /// <see cref="LoadFromCards"/>. Usado quando <see cref="UseModImages"/>
    /// está ligado.
    /// </summary>
    private static readonly Dictionary<int, string> ModUrls = new(722);

    /// <summary>
    /// Resolve a URL da imagem da carta. Se <see cref="UseModImages"/>
    /// estiver ligado e existir uma entrada no registry, retorna a data URL
    /// da arte do ROM; caso contrário substitui <c>{id}</c> no template.
    /// </summary>
    public static string Url(string? template, int cardId)
    {
        if (UseModImages && ModUrls.TryGetValue(cardId, out var dataUrl))
            return dataUrl;
        if (string.IsNullOrWhiteSpace(template)) return "";
        return template.Replace("{id}", cardId.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reabastece o cache de data URLs com base na lista de cartas
    /// recém-parseadas. Chamado após <c>LoadThumbnailsAsync</c>.
    /// </summary>
    public static void LoadFromCards(IEnumerable<Card> cards)
    {
        ModUrls.Clear();
        foreach (var c in cards)
        {
            if (!string.IsNullOrEmpty(c.ModImageDataUrl))
                ModUrls[c.CardId] = c.ModImageDataUrl;
        }
    }
}
