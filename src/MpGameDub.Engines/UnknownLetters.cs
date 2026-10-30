using System.Text.RegularExpressions;

namespace MpGameDub.Engines;

/// <summary>
/// O vocabulário do Opus-MT en-pt não tem À, Ó, Ú, Í, Â, Ê, Ô, Ã, Õ nem Ç maiúsculos: "Great." sai "&lt;unk&gt;timo." e
/// "Sometimes" sai "&lt;unk&gt;s vezes". As palavras minúsculas estão lá ("▁ótimo", "▁às"), então dá para achar a letra
/// que falta e devolvê-la em maiúscula. Sem palpite que bata, o &lt;unk&gt; só sai do texto, para a voz não ler "unk".
/// </summary>
internal static partial class UnknownLetters
{
    // Na ordem em que costumam abrir frase em português: "À noite", "Ótimo", "Último", "Índio", "Ônibus"...
    private const string Candidates = "àóúíôêâãõç";

    public static string Repair(string text, IReadOnlyDictionary<string, int> vocab)
    {
        if (!text.Contains(Unk, StringComparison.Ordinal)) return text;
        var repaired = Word().Replace(text, m => Guess(m.Groups[1].Value, vocab) ?? m.Value);
        return Leftover().Replace(repaired, "").Trim();
    }

    private static string? Guess(string rest, IReadOnlyDictionary<string, int> vocab)
    {
        // Primeiro a palavra inteira no vocabulário ("▁ótimo"); depois uma peça que comece a palavra ("▁ótima" em "ótimas").
        foreach (var c in Candidates)
            if (vocab.ContainsKey("▁" + c + rest)) return char.ToUpperInvariant(c) + rest;
        if (rest.Length == 0) return null;
        foreach (var c in Candidates)
        {
            var word = "▁" + c + rest;
            if (vocab.Keys.Any(k => k.Length >= 3 && word.StartsWith(k, StringComparison.Ordinal))) return char.ToUpperInvariant(c) + rest;
        }
        return null;
    }

    private const string Unk = "<unk>";

    // Letras logo depois do <unk>, ou nenhuma quando vem uma palavra em seguida ("<unk> noite" é "À noite").
    [GeneratedRegex(@"<unk>(\p{Ll}+|(?=\s\p{Ll}))")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s?<unk>")]
    private static partial Regex Leftover();
}
