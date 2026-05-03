namespace yugiho_tools.Application.Helpers;

public static class CardImage
{
    /// <summary>
    /// Resolve a URL da imagem da carta substituindo o placeholder
    /// <c>{id}</c> do template do mod pelo CardId.
    /// Retorna string vazia se o template estiver vazio.
    /// </summary>
    public static string Url(string? template, int cardId)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        return template.Replace("{id}", cardId.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
