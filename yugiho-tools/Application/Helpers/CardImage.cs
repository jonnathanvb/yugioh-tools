using System.Collections.Concurrent;
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
    // ConcurrentDictionary porque a UI lê durante render (thread MAUI/UI)
    // enquanto LoadFromCards repopula no Task.Run do LoadedModCache.
    private static readonly ConcurrentDictionary<int, string> ModUrls  = new();
    /// <summary>Cache <c>cardId → data URL</c> da variante mini, usada
    /// exclusivamente pelo grafo de fusão. Pode ter resolução/qualidade
    /// diferente do <see cref="ModUrls"/> conforme escolha do usuário em
    /// <see cref="Domain.Entities.Mod.FusionMiniVariant"/>.</summary>
    private static readonly ConcurrentDictionary<int, string> MiniUrls = new();

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
        // Clear+repopulate: a janela de inconsistência é tolerada porque
        // ConcurrentDictionary só pode mostrar "carta ainda sem URL" durante
        // a swap (fallback do template online cobre) — nunca exception.
        ModUrls.Clear();
        MiniUrls.Clear();
        foreach (var c in cards)
        {
            if (!string.IsNullOrEmpty(c.ModImageDataUrl))
                ModUrls[c.CardId] = c.ModImageDataUrl;
            if (!string.IsNullOrEmpty(c.MiniImageDataUrl))
                MiniUrls[c.CardId] = c.MiniImageDataUrl;
        }
    }

    /// <summary>Resolve a URL da variante mini (grafo de fusão). Cai pra
    /// arte principal (<see cref="Url"/>) se a mini não foi carregada —
    /// assim o grafo continua mostrando algo mesmo se o pacote do MOD
    /// não tiver mini_sd/mini_hd.</summary>
    public static string MiniUrl(string? template, int cardId)
    {
        if (UseModImages && MiniUrls.TryGetValue(cardId, out var mini))
            return mini;
        return Url(template, cardId);
    }
}
