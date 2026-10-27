namespace MpGameDub.Core.Models;

/// <summary>
/// Os modelos que o programa sabe baixar. Endereço, tamanho e SHA-256 ficam fixos aqui:
/// trocar de versão é trocar a linha inteira, nunca só o endereço.
/// </summary>
public static class ModelCatalog
{
    public static readonly ModelFile SileroVad = new(
        "silero-vad-6.2.3",
        "Detector de fala Silero VAD 6.2.3 (2 MB)",
        new Uri("https://raw.githubusercontent.com/snakers4/silero-vad/v6.2.3/src/silero_vad/data/silero_vad.onnx"),
        "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3",
        2_327_524,
        "MIT");

    public static readonly ModelFile WhisperSmall = new(
        "whisper-small-q8_0",
        "Whisper small, 8 bits (264 MB): o mais leve, para comparar",
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q8_0.bin"),
        "49c8fb02b65e6049d5fa6c04f81f53b867b5ec9540406812c643f177317f779f",
        264_464_607,
        "MIT");

    public static readonly ModelFile WhisperMedium = new(
        "whisper-medium-q5_0",
        "Whisper medium, 5 bits (539 MB)",
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium-q5_0.bin"),
        "19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f",
        539_212_467,
        "MIT");

    public static readonly ModelFile WhisperTurbo = new(
        "whisper-large-v3-turbo-q5_0",
        "Whisper large-v3-turbo, 5 bits (574 MB): o candidato principal",
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin"),
        "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
        574_041_195,
        "MIT");

    public static readonly ModelFile WhisperTurbo8 = new(
        "whisper-large-v3-turbo-q8_0",
        "Whisper large-v3-turbo, 8 bits (874 MB): mais fiel, mais memória",
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q8_0.bin"),
        "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1",
        874_188_075,
        "MIT");

    public static readonly ModelFile Kokoro = new(
        "kokoro-82m-v1.0",
        "Voz Kokoro 82M v1.0 (326 MB)",
        new Uri("https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx"),
        "0cfd5e79aab70a3d8c1a57dc639835110ddb32c9f5ff4fdd1f4db202ea43bb05",
        325_508_342,
        "Apache-2.0");

    // Convertido e publicado pelo .github/workflows/modelos.yml deste repositório (ferramentas/exportar-opus-mt.py).
    public static readonly ModelFile OpusMt = new(
        "opus-mt-tc-big-en-pt-int8",
        "Tradução Opus-MT tc-big en-pt, 8 bits (421 MB)",
        new Uri("https://github.com/PABLOzxk/mp-gamedub/releases/download/modelos-opus-mt-v1/opus-mt-tc-big-en-pt-int8.zip"),
        "c7dccbaa3ab5bed0a7f14f41a476dc342da0e22f5f52aa19cffcfd49bcf90eba",
        421_447_752,
        "CC-BY-4.0",
        Extract: true);

    public static IReadOnlyList<ModelFile> Whisper { get; } = [WhisperTurbo, WhisperSmall, WhisperMedium, WhisperTurbo8];

    public static IReadOnlyList<ModelFile> All { get; } = [SileroVad, .. Whisper, Kokoro, OpusMt];

    public static ModelFile? Find(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}
