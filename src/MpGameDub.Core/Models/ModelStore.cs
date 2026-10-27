using System.IO.Compression;
using System.Security.Cryptography;

namespace MpGameDub.Core.Models;

/// <summary>
/// Baixa modelos para uma pasta local e confere tamanho e SHA-256 antes de usar.
/// O download vai para um arquivo .parcial e só ganha o nome final depois de conferido,
/// então um arquivo com o nome final sempre passou na conferência.
/// </summary>
public sealed class ModelStore(string root, HttpClient http)
{
    public string Root { get; } = root;

    public string PathOf(ModelFile model) =>
        model.Extract ? System.IO.Path.Combine(Root, model.Id) : System.IO.Path.Combine(Root, model.FileName);

    public bool IsReady(ModelFile model)
    {
        if (model.Extract) return File.Exists(System.IO.Path.Combine(PathOf(model), ".conferido"));
        var info = new FileInfo(PathOf(model));
        return info.Exists && info.Length == model.Size && File.Exists(PathOf(model) + ".conferido");
    }

    /// <summary>Garante o modelo na pasta e devolve o caminho dele (arquivo, ou pasta se for extraído).</summary>
    public async Task<string> EnsureAsync(ModelFile model, IProgress<double>? progress, CancellationToken ct)
    {
        if (IsReady(model)) return PathOf(model);
        Directory.CreateDirectory(Root);
        var partial = System.IO.Path.Combine(Root, model.FileName + ".parcial");
        try
        {
            var hash = await DownloadAsync(model, partial, progress, ct);
            var size = new FileInfo(partial).Length;
            if (size != model.Size)
                throw new ModelIntegrityException($"{model.Id}: tamanho {size} bytes, esperado {model.Size}. O arquivo foi apagado.");
            if (!string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ModelIntegrityException($"{model.Id}: SHA-256 {hash} não confere com {model.Sha256}. O arquivo foi apagado.");

            if (model.Extract)
            {
                var target = PathOf(model);
                var staging = target + ".extraindo";
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                ZipFile.ExtractToDirectory(partial, staging);
                await File.WriteAllTextAsync(System.IO.Path.Combine(staging, ".conferido"), model.Sha256, ct);
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(staging, target);
                File.Delete(partial);
            }
            else
            {
                File.Move(partial, PathOf(model), overwrite: true);
                await File.WriteAllTextAsync(PathOf(model) + ".conferido", model.Sha256, ct);
            }
            return PathOf(model);
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    private async Task<string> DownloadAsync(ModelFile model, string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > model.Size)
                throw new ModelIntegrityException($"{model.Id}: o download passou de {model.Size} bytes. O arquivo foi apagado.");
            sha.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.Report((double)total / model.Size);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
