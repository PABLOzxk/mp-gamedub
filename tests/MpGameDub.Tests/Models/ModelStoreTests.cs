using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using MpGameDub.Core.Models;

namespace MpGameDub.Tests.Models;

public sealed class ModelStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mpgd-models-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeServer(byte[] body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private static ModelFile Model(byte[] body, string? sha = null, long? size = null, bool extract = false) => new(
        "teste", "modelo de teste", new Uri("https://example.org/m/modelo.bin"),
        sha ?? Convert.ToHexStringLower(SHA256.HashData(body)), size ?? body.Length, "MIT", extract);

    [Fact]
    public async Task CorrectFile_IsSavedAndNotDownloadedAgain()
    {
        var body = "conteúdo do modelo"u8.ToArray();
        var server = new FakeServer(body);
        var store = new ModelStore(_root, new HttpClient(server));

        var path = await store.EnsureAsync(Model(body), null, CancellationToken.None);
        var again = await store.EnsureAsync(Model(body), null, CancellationToken.None);

        Assert.Equal(body, await File.ReadAllBytesAsync(path));
        Assert.Equal(path, again);
        Assert.Equal(1, server.Calls);
    }

    [Fact]
    public async Task WrongHash_DeletesEverythingAndThrows()
    {
        var body = "modelo adulterado"u8.ToArray();
        var store = new ModelStore(_root, new HttpClient(new FakeServer(body)));
        var model = Model(body, sha: new string('0', 64));

        await Assert.ThrowsAsync<ModelIntegrityException>(() => store.EnsureAsync(model, null, CancellationToken.None));

        Assert.False(store.IsReady(model));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task LargerThanDeclared_StopsAndThrows()
    {
        var body = new byte[5000];
        var store = new ModelStore(_root, new HttpClient(new FakeServer(body)));

        await Assert.ThrowsAsync<ModelIntegrityException>(() => store.EnsureAsync(Model(body, size: 100), null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ZipModel_IsExtractedIntoFolder()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("vocab.json").Open());
            w.Write("{}");
        }
        var body = zipStream.ToArray();
        var store = new ModelStore(_root, new HttpClient(new FakeServer(body)));

        var folder = await store.EnsureAsync(Model(body, extract: true), null, CancellationToken.None);

        Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(folder, "vocab.json")));
        Assert.True(store.IsReady(Model(body, extract: true)));
        Assert.False(File.Exists(Path.Combine(_root, "teste.zip")));
    }
}
