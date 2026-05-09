using System.Text.Encodings.Web;
using System.Text.Json;
using yugiho_tools.Domain.Entities;

namespace yugiho_tools.Infrastructure.Storage;

/// <summary>
/// Persiste e lê o <see cref="ExtractedRomData"/> em
/// <c>MOD/{slug}/data.json</c>. JSON de ~3-7 MB por MOD — leitura é
/// uma ordem de magnitude mais rápida que reparsear o ROM (~30 MB de MRG
/// + decode de imagens), o que justifica o cache em disco.
/// </summary>
public class ExtractedDataRepository
{
    public const string FileName = "data.json";

    /// <summary>Opções:
    /// • WriteIndented=false reduz tamanho ~2x; JSON não é editado a mão.
    /// • UnsafeRelaxedJsonEscaping preserva chars como &lt;, &gt;, &amp;,
    ///   ', e Unicode literal — sem isso, descrições importadas do lab
    ///   (com marcadores <c>&lt;_N_&gt;</c>) saem como <c><_N_></c>
    ///   no nosso data.json, parecendo "transformação" mesmo sendo o
    ///   mesmo conteúdo logicamente.</summary>
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string PathFor(string modFolderPath)
        => System.IO.Path.Combine(modFolderPath, FileName);

    public bool Exists(string modFolderPath)
        => File.Exists(PathFor(modFolderPath));

    public async Task<ExtractedRomData?> ReadAsync(string modFolderPath)
    {
        var path = PathFor(modFolderPath);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ExtractedRomData>(stream, Opts);
        }
        catch
        {
            // JSON corrompido/desatualizado — caller cai no parser binário.
            return null;
        }
    }

    public async Task WriteAsync(string modFolderPath, ExtractedRomData data)
    {
        Directory.CreateDirectory(modFolderPath);
        var path = PathFor(modFolderPath);
        // Escreve em arquivo temporário e move atomicamente — protege contra
        // app crashar no meio do save deixando JSON corrompido.
        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, data, Opts);
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public void Delete(string modFolderPath)
    {
        var path = PathFor(modFolderPath);
        if (File.Exists(path)) File.Delete(path);
    }
}
