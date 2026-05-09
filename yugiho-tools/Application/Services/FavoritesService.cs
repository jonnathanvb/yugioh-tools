using System.Text.Json;
using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Application.Services;

/// <summary>
/// CRUD das listas de favoritos. Persistência simples num único arquivo
/// JSON em <see cref="FileSystem.AppDataDirectory"/> — cabe perfeitamente
/// pra dezenas de listas com dezenas de cartas cada.
///
/// Modelo:
///   FavoriteList { Id, Name, CreatedAt, Cards[FavoriteEntry { CardId, Stars, AddedAt }] }
/// </summary>
public class FavoritesService
{
    private const string FileName = "favorites.json";

    private readonly object   _lock = new();
    private List<FavoriteList>? _cache;

    public event Action? Changed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private string Path => System.IO.Path.Combine(FileSystem.AppDataDirectory, FileName);

    /// <summary>Retorna TODAS as listas (todos os mods).</summary>
    public IReadOnlyList<FavoriteList> GetAll()
    {
        lock (_lock)
        {
            return EnsureLoaded().ToList();
        }
    }

    /// <summary>Retorna apenas as listas do mod especificado. Quando o
    /// slug está vazio devolve nada — favoritos sempre exigem um mod
    /// ativo, já que o CardId é específico de cada ROM.</summary>
    public IReadOnlyList<FavoriteList> GetForMod(string? modSlug)
    {
        if (string.IsNullOrWhiteSpace(modSlug)) return [];
        lock (_lock)
        {
            return EnsureLoaded()
                .Where(l => string.Equals(l.ModSlug, modSlug, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public FavoriteList? GetById(Guid id)
    {
        lock (_lock)
        {
            return EnsureLoaded().FirstOrDefault(l => l.Id == id);
        }
    }

    public FavoriteList Create(string name, string modSlug)
    {
        lock (_lock)
        {
            var lists = EnsureLoaded();
            var list = new FavoriteList { Name = name.Trim(), ModSlug = modSlug };
            lists.Add(list);
            Save();
            Changed?.Invoke();
            return list;
        }
    }

    public void Rename(Guid id, string newName)
    {
        lock (_lock)
        {
            var list = EnsureLoaded().FirstOrDefault(l => l.Id == id);
            if (list is null) return;
            list.Name = newName.Trim();
            Save();
            Changed?.Invoke();
        }
    }

    public void Delete(Guid id)
    {
        lock (_lock)
        {
            var lists = EnsureLoaded();
            lists.RemoveAll(l => l.Id == id);
            Save();
            Changed?.Invoke();
        }
    }

    public void AddCards(Guid listId, IEnumerable<int> cardIds)
    {
        lock (_lock)
        {
            var list = EnsureLoaded().FirstOrDefault(l => l.Id == listId);
            if (list is null) return;
            var existing = list.Cards.Select(c => c.CardId).ToHashSet();
            var added = false;
            foreach (var cid in cardIds)
            {
                if (!existing.Add(cid)) continue;
                list.Cards.Add(new FavoriteEntry { CardId = cid });
                added = true;
            }
            if (added)
            {
                Save();
                Changed?.Invoke();
            }
        }
    }

    public void RemoveCard(Guid listId, int cardId)
    {
        lock (_lock)
        {
            var list = EnsureLoaded().FirstOrDefault(l => l.Id == listId);
            if (list is null) return;
            int removed = list.Cards.RemoveAll(c => c.CardId == cardId);
            if (removed > 0)
            {
                Save();
                Changed?.Invoke();
            }
        }
    }

    public void SetStars(Guid listId, int cardId, int stars)
    {
        lock (_lock)
        {
            var list = EnsureLoaded().FirstOrDefault(l => l.Id == listId);
            var entry = list?.Cards.FirstOrDefault(c => c.CardId == cardId);
            if (entry is null) return;
            entry.Stars = Math.Clamp(stars, 0, 5);
            Save();
            Changed?.Invoke();
        }
    }

    private List<FavoriteList> EnsureLoaded()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                _cache = JsonSerializer.Deserialize<List<FavoriteList>>(json, JsonOpts)
                       ?? new List<FavoriteList>();
            }
            else
            {
                _cache = new List<FavoriteList>();
            }
        }
        catch
        {
            _cache = new List<FavoriteList>();
        }
        return _cache;
    }

    private void Save()
    {
        if (_cache is null) return;
        try
        {
            Directory.CreateDirectory(FileSystem.AppDataDirectory);
            var json = JsonSerializer.Serialize(_cache, JsonOpts);
            File.WriteAllText(Path, json);
        }
        catch
        {
            // I/O fail — UI continua com cache em memória.
        }
    }
}
