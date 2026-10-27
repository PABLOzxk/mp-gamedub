namespace MpGameDub.Core.Pipeline;

public sealed record StalePolicyOptions
{
    /// <summary>Uma fala mais velha que isto (contando do fim dela no jogo) não é mais tocada.</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Se já há uma fala mais nova esperando, a velha é descartada em vez de atrasar todas.</summary>
    public bool KeepOnlyNewest { get; init; } = true;
}

/// <summary>Decide quando uma fala atrasada deixa de valer a pena, para o atraso não se acumular.</summary>
public static class StalePolicy
{
    public const string TooOld = "velha demais";
    public const string Overtaken = "atropelada por uma fala mais nova";

    /// <summary>Devolve o motivo para descartar, ou null para seguir.</summary>
    public static string? DropReason(TimeSpan age, bool newerWaiting, StalePolicyOptions options)
    {
        if (age > options.MaxAge) return TooOld;
        if (options.KeepOnlyNewest && newerWaiting) return Overtaken;
        return null;
    }
}
