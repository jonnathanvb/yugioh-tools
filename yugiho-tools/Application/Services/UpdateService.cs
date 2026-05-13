#if WINDOWS
using Velopack;
using Velopack.Sources;
#endif

namespace yugiho_tools.Application.Services;

public record UpdateResult(bool Updated, string Message);

/// <summary>
/// Wrapper sobre o Velopack UpdateManager. Verifica releases publicadas no
/// GitHub e aplica atualização incremental quando disponível.
/// Em build local (sem manifest do Velopack instalado), <c>IsInstalled</c>
/// é falso e o método retorna mensagem informativa em vez de falhar.
///
/// No macOS o Velopack não está disponível — o método retorna sempre
/// <c>(false, "...")</c>. Para auto-update no Mac veja MIGRATION_NOTES
/// (Sparkle / SUUpdater seria o caminho equivalente).
/// </summary>
public class UpdateService
{
#if WINDOWS
    private const string GitHubRepoUrl = "https://github.com/jonnathanvb/yugiho";
    private readonly UpdateManager _manager;

    public UpdateService()
    {
        // Sem ExplicitChannel: o Velopack lê o canal gravado no install
        // (current/sq.version). Quem instalou stable só vê stable; quem
        // instalou beta só vê beta. Trocar de canal exige reinstalar com
        // o Setup do canal desejado.
        // prerelease=true permite que releases marcadas como pre-release
        // no GitHub sejam listadas — o filtro real é feito pelo canal.
        var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: true);
        _manager = new UpdateManager(source);
    }

    public async Task<UpdateResult> CheckAndApplyAsync()
    {
        if (!_manager.IsInstalled)
            return new UpdateResult(false, "App não foi instalado via Velopack — updates indisponíveis em build local.");

        var info = await _manager.CheckForUpdatesAsync();
        if (info == null)
            return new UpdateResult(false, "Você está na versão mais recente.");

        await _manager.DownloadUpdatesAsync(info);
        _manager.ApplyUpdatesAndRestart(info);
        return new UpdateResult(true, $"Atualizado para {info.TargetFullRelease.Version}. Reiniciando...");
    }
#else
    public Task<UpdateResult> CheckAndApplyAsync()
        => Task.FromResult(new UpdateResult(false,
            "Auto-update está disponível apenas no Windows. " +
            "No macOS, baixe a nova versão manualmente em GitHub Releases."));
#endif
}
