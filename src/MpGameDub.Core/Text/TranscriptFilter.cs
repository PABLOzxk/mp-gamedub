using System.Text.RegularExpressions;

namespace MpGameDub.Core.Text;

public sealed record FilterResult(bool Accepted, string Text, string? Reason)
{
    public static FilterResult Accept(string text) => new(true, text, null);
    public static FilterResult Reject(string text, string reason) => new(false, text, reason);
}

/// <summary>
/// Limpa o texto do reconhecimento e descarta o que o Whisper costuma inventar em música, ruído e silêncio
/// (frases de fim de vídeo, créditos de legenda, a mesma palavra repetida), e falas que são só som de boca
/// ("Hmm.", "Um...", "Eesh!"): dublar isso não ajuda ninguém e ocupa a voz enquanto a próxima fala espera.
/// </summary>
public static partial class TranscriptFilter
{
    private static readonly string[] KnownHallucinations =
    [
        "thanks for watching",
        "thank you for watching",
        "thank you so much for watching",
        "please subscribe",
        "like and subscribe",
        "subtitles by",
        "subtitled by",
        "captions by",
        "transcribed by",
        "translated by",
        "amara.org",
        "www.",
        "obrigado por assistir",
        "legendas pela comunidade",
    ];

    public static FilterResult Apply(string? raw)
    {
        var text = Spaces().Replace(Annotations().Replace(raw ?? "", " "), " ").Trim();
        if (text.Length == 0) return FilterResult.Reject(text, "vazio");
        if (!text.Any(char.IsLetter)) return FilterResult.Reject(text, "sem palavras");

        var lower = text.ToLowerInvariant();
        foreach (var phrase in KnownHallucinations)
            if (lower.Contains(phrase, StringComparison.Ordinal)) return FilterResult.Reject(text, $"frase típica de alucinação (\"{phrase}\")");

        var words = Words().Matches(lower).Select(m => m.Value).ToArray();
        if (words.Length >= 4 && words.Distinct().Count() == 1) return FilterResult.Reject(text, "mesma palavra repetida");
        if (words.All(Interjection().IsMatch)) return FilterResult.Reject(text, "só interjeição");
        return FilterResult.Accept(text);
    }

    // [Music], (laughs), *sigh*, ♪ ... ♪
    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*|♪[^♪]*♪|♪")]
    private static partial Regex Annotations();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();

    // hmm, mm, um, uh, uh-huh, mm-hmm, er, erm, ah, oh, eh, huh, ugh, eesh, shh, psst, whoa
    [GeneratedRegex(@"^(h*m+|u+[hm]+|h+u+h+|e+r+m*|a+h+|o+h+|e+h+|u+g+h+|e+s+h+|s+h+|p+s+t+|w+h?o+a+h*)$")]
    private static partial Regex Interjection();
}
