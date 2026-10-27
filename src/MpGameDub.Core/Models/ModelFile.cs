namespace MpGameDub.Core.Models;

/// <summary>
/// Um arquivo de modelo baixado à parte do programa. A origem, o tamanho e o SHA-256 são fixos:
/// se o arquivo baixado não bater exatamente, ele é apagado e nada é usado.
/// </summary>
/// <param name="Extract">Se for um .zip, é extraído numa pasta com o nome do Id depois de conferido.</param>
public sealed record ModelFile(
    string Id,
    string Description,
    Uri Url,
    string Sha256,
    long Size,
    string License,
    bool Extract = false)
{
    public string FileName => Id + (Extract ? ".zip" : Path.GetExtension(Url.AbsolutePath));
}

public sealed class ModelIntegrityException(string message) : Exception(message);
