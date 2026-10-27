namespace MpGameDub.Core.Pipeline;

/// <summary>O que aconteceu com uma fala, do jogo até a voz traduzida. Tempos em milissegundos.</summary>
public sealed class UtteranceTrace
{
    public required int Id { get; init; }

    /// <summary>Início e fim da fala no áudio do jogo.</summary>
    public required TimeSpan SpeechStart { get; init; }
    public required TimeSpan SpeechEnd { get; init; }
    public bool ForcedCut { get; init; }

    /// <summary>Do fim da fala até o detector ter certeza de que ela acabou.</summary>
    public double VadMs { get; set; }
    public double AsrMs { get; set; }
    public double MtMs { get; set; }
    public double TtsMs { get; set; }

    /// <summary>Tempo parado na fila, esperando a fala anterior sair do motor ou do alto-falante.</summary>
    public double QueueMs { get; set; }

    /// <summary>Do fim da fala no jogo até começar a voz traduzida. É o número que importa.</summary>
    public double? EndToVoiceMs { get; set; }

    /// <summary>Quando a voz começou a tocar, na linha do tempo do áudio do jogo.</summary>
    public TimeSpan? VoiceStart { get; set; }
    public TimeSpan? VoiceDuration { get; set; }

    public string Original { get; set; } = "";
    public string Translation { get; set; } = "";

    /// <summary>Motivo de a fala não ter sido dublada (filtrada, velha, atropelada, erro), ou null.</summary>
    public string? Dropped { get; set; }

    public double ProcessingMs => AsrMs + MtMs + TtsMs;
}
