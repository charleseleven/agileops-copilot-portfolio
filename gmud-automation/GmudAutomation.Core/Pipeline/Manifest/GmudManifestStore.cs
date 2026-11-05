using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GmudAutomation.Core.Pipeline.Manifest;

/// <summary>Persistência do manifesto e dos artefatos de uma GMUD (scripts, relatórios).</summary>
public interface IGmudManifestStore
{
    /// <summary>Pasta da GMUD (<c>&lt;raiz&gt;/&lt;id&gt;</c>), criada se não existir.</summary>
    string GetGmudDirectory(int gmudId);

    GmudManifest? Load(int gmudId);

    void Save(GmudManifest manifest);
}

/// <inheritdoc cref="IGmudManifestStore"/>
public sealed class FileGmudManifestStore : IGmudManifestStore
{
    public const string ManifestFileName = "manifest.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Mantém acentos legíveis no arquivo (títulos dos cards); o manifesto não é servido em HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // Preserva os dicionários case-insensitive criados nos construtores das classes do manifesto.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    private readonly string _rootDirectory;

    public FileGmudManifestStore(string rootDirectory)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? throw new ArgumentException("Informe a pasta das GMUDs.", nameof(rootDirectory)) : rootDirectory;
    }

    public string GetGmudDirectory(int gmudId)
    {
        var directory = Path.Combine(_rootDirectory, gmudId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public GmudManifest? Load(int gmudId)
    {
        var path = Path.Combine(_rootDirectory, gmudId.ToString(System.Globalization.CultureInfo.InvariantCulture), ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<GmudManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Manifesto inválido em \"{path}\".");
    }

    public void Save(GmudManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Gmud.Id <= 0)
        {
            throw new InvalidOperationException("O manifesto precisa do ID da GMUD antes de ser gravado.");
        }

        var path = Path.Combine(GetGmudDirectory(manifest.Gmud.Id), ManifestFileName);

        // Grava em arquivo temporário e substitui: uma interrupção no meio nunca deixa um manifesto truncado.
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
