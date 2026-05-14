using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Singleton que carrega o JSON do MOD ativo em memória (de
/// <c>MODs/{slug}/data.json</c>) e mantém disponível pras telas.
///
/// Antes era <c>LoadedRomCache</c> e ainda fazia parsing binário de ROM
/// como fallback. Agora o yugioh-tools só consome JSON (extração ROM
/// virou responsabilidade do yugiho-download-json), então o nome foi
/// atualizado pra refletir a fonte real.
///
/// Páginas observam <see cref="Changed"/> e re-renderizam quando o mod
/// ativo muda.
/// </summary>
public class LoadedModCache
{
    private readonly IModRepository      _repo;
    private readonly CurrentModContext   _modContext;
    private readonly ExtractedDataLoader _jsonLoader;

    private LoadedRomData? _data;
    private Mod?           _loadedFor;
    private string?        _loadedSlug;
    private bool           _isLoading;
    private int            _progress;
    private string         _message = "";
    /// <summary>Cancela load em andamento se o usuário trocar de MOD
    /// enquanto carrega — evita race entre dois loads concorrentes.</summary>
    private CancellationTokenSource? _cts;

    public event Action? Changed;

    public LoadedModCache(
        IModRepository repo,
        CurrentModContext modContext,
        ExtractedDataLoader jsonLoader)
    {
        _repo       = repo;
        _modContext = modContext;
        _jsonLoader = jsonLoader;
        _modContext.Changed += OnActiveModChanged;
    }

    public LoadedRomData? Current     => _data;
    public bool           HasData     => _data is not null;
    public Mod?           LoadedFor   => _loadedFor;
    public bool           IsLoading   => _isLoading;
    public int            Progress    => _progress;
    public string         Message     => _message;

    public Task EnsureLoadedAsync() => LoadActiveAsync(force: false);
    public Task ReloadAsync()        => LoadActiveAsync(force: true);
    public Task InitializeAsync()    => LoadActiveAsync(force: false);

    private async void OnActiveModChanged()
    {
        try { await LoadActiveAsync(force: false); }
        catch { /* já encapsulado abaixo */ }
    }

    private async Task LoadActiveAsync(bool force)
    {
        var mod = _modContext.Current;
        if (mod is null)
        {
            _data = null; _loadedFor = null; _loadedSlug = null;
            Notify();
            return;
        }

        if (!force && _data is not null && _loadedSlug == mod.Slug) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _isLoading = true;
        _progress  = 0;
        _message   = "Carregando dados do mod…";
        _data      = null;
        Notify();

        try
        {
            if (!_jsonLoader.HasExtractedData(mod))
            {
                _message = "MOD sem data.json — reimporte em /mods.";
                _data = null; _loadedFor = null; _loadedSlug = null;
                return;
            }

            _progress = 50;
            Notify();
            var fromJson = await Task.Run(() => _jsonLoader.TryLoadAsync(mod), token);
            if (token.IsCancellationRequested) return;
            if (fromJson is null)
            {
                _message = "data.json corrompido — reimporte o MOD.";
                _data = null; _loadedFor = null; _loadedSlug = null;
                return;
            }

            _data       = fromJson;
            _loadedFor  = mod;
            _loadedSlug = mod.Slug;

            // Aplica preferências per-MOD. ImageSource sempre = Mod
            // (default) — TEA online não é mais usado como fonte primária
            // depois que tudo vem empacotado no ZIP.
            Helpers.CardImage.UseModImages = true;
            if (mod.FrameOverrides is not null)
                Helpers.CardFrameRegistry.LoadPositions(mod.FrameOverrides);
            Helpers.CardFrameRegistry.ShowAtkDefLabels = mod.ShowAtkDefLabels;
        }
        catch (OperationCanceledException) { /* trocou de MOD; ignora */ }
        catch (Exception ex)
        {
            _message = $"Erro: {ex.Message}";
            _data = null; _loadedFor = null; _loadedSlug = null;
        }
        finally
        {
            _isLoading = false;
            Notify();
        }
    }

    private void Notify() => Changed?.Invoke();
}
