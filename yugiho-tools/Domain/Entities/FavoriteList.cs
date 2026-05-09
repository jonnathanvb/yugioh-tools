namespace yugiho_tools.Domain.Entities;

/// <summary>Lista nomeada de cartas favoritas, com avaliação 0-5 estrelas
/// por carta. Persistida como JSON pelo <c>FavoritesService</c>.</summary>
public class FavoriteList
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public string Name      { get; set; } = "";
    /// <summary>Slug do MOD ao qual a lista pertence. Cartas de mods
    /// diferentes têm tabelas diferentes (CardId 50 do MOD A pode não
    /// existir no MOD B), por isso cada lista é exclusiva de um mod.</summary>
    public string ModSlug   { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<FavoriteEntry> Cards { get; set; } = new();
}

/// <summary>Entrada de uma <see cref="FavoriteList"/>.</summary>
public class FavoriteEntry
{
    /// <summary>CardId 1-based (mesmo do <see cref="Card"/>).</summary>
    public int CardId   { get; set; }
    /// <summary>0-5 estrelas. 0 = sem avaliação (default).</summary>
    public int Stars    { get; set; }
    /// <summary>Pra ordenação "por adição".</summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
