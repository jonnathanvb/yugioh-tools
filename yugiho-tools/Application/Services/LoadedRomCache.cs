using yugiho_tools.Application.UseCases;
using yugiho_tools.Domain.Entities;
using yugiho_tools.Domain.Interfaces;

namespace yugiho_tools.Application.Services;

/// <summary>
/// Singleton que orquestra a leitura do ROM e mantém os dados em memória
/// para todas as páginas. Antes desta versão, Home/Cards/MemoryCard
/// rodavam o parser cada uma — re-lendo o MRG (~30 MB) ao trocar de tela.
/// Agora a leitura acontece só quando o MOD ativo muda; as páginas
/// observam <see cref="Changed"/> e só renderizam.
/// </summary>
public class LoadedRomCache
{
    private readonly LoadRomDataUseCase   _loadRom;
    private readonly IModRepository       _repo;
    private readonly CurrentModContext    _modContext;
    private readonly ExtractedDataLoader  _jsonLoader;

    private LoadedRomData? _data;
    private Mod?           _loadedFor;
    private string?        _loadedSlug;
    private bool           _isLoading;
    private int            _progress;
    private string         _message = "";
    /// <summary>Token cancela load em andamento se o usuário trocar de
    /// MOD enquanto carrega — evita race entre dois loads concorrentes.</summary>
    private CancellationTokenSource? _cts;

    public event Action? Changed;

    public LoadedRomCache(
        LoadRomDataUseCase loadRom,
        IModRepository repo,
        CurrentModContext modContext,
        ExtractedDataLoader jsonLoader)
    {
        _loadRom    = loadRom;
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

    /// <summary>
    /// Forçar um (re)load do MOD ativo. Útil em casos como "usuário
    /// editou o MRG fora do app". No fluxo normal não precisa chamar —
    /// a invocação acontece automaticamente em <see cref="OnActiveModChanged"/>.
    /// </summary>
    public Task EnsureLoadedAsync() => LoadActiveAsync(force: false);

    public Task ReloadAsync() => LoadActiveAsync(force: true);

    /// <summary>Inicializa o serviço — faz o primeiro load se o MOD ativo
    /// já estiver setado quando o app sobe (restaurado de Preferences).</summary>
    public Task InitializeAsync() => LoadActiveAsync(force: false);

    private async void OnActiveModChanged()
    {
        // Disparado quando MOD ativo muda. Async void é OK aqui (handler
        // de evento sem espera); EnsureLoadedAsync já trata exceções.
        try { await LoadActiveAsync(force: false); }
        catch { /* já encapsulado abaixo */ }
    }

    private async Task LoadActiveAsync(bool force)
    {
        var mod = _modContext.Current;
        if (mod is null)
        {
            // Limpa quando o usuário tira o MOD ativo.
            _data = null; _loadedFor = null; _loadedSlug = null;
            Notify();
            return;
        }

        // Skip se já temos dados deste MOD e não foi pedido reload.
        if (!force && _data is not null && _loadedSlug == mod.Slug) return;

        // Cancela load anterior em andamento (caso usuário troque de MOD
        // várias vezes seguidas).
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _isLoading = true;
        _progress  = 0;
        _message   = "Lendo arquivos do ROM…";
        _data      = null;        // libera o anterior pra GC
        Notify();

        try
        {
            // Fonte canônica: data.json extraído. Sem JSON → mod não
            // foi processado (ou usuário deletou) → app não consegue
            // carregar e pede re-extração via UI.
            if (!_jsonLoader.HasExtractedData(mod))
            {
                _message = "MOD não processado — re-extraia em /mods.";
                _data = null; _loadedFor = null; _loadedSlug = null;
                return;
            }

            _message = "Carregando dados extraídos…";
            _progress = 50;
            Notify();
            var fromJson = await Task.Run(() => _jsonLoader.TryLoadAsync(mod), token);
            if (token.IsCancellationRequested) return;
            if (fromJson is null)
            {
                _message = "data.json corrompido — re-extraia o MOD.";
                _data = null; _loadedFor = null; _loadedSlug = null;
                return;
            }

            _data       = fromJson;
            _loadedFor  = mod;
            _loadedSlug = mod.Slug;
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
