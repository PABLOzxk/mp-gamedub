using System.Text.RegularExpressions;

namespace MpGameDub.Core.Text;

/// <summary>
/// Separa uma fala em frases antes da tradução. O Opus-MT aprendeu frase a frase e, com duas juntas, costuma engolir
/// a curta: "Reload! I'm out of ammo!" vira só "Estou sem munição!". O ferramentas/exportar-opus-mt.py usa a mesma
/// regra, e o teste do tradutor confere que os dois dividem igual.
/// </summary>
public static partial class SentenceSplitter
{
    // Abreviações comuns em falas de jogo; o ponto delas não termina a frase.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "jr", "sr", "st", "lt", "sgt", "capt", "col", "gen", "prof", "vs",
    };

    private static readonly char[] ClosingMarks = ['"', '\'', '”', '’', ')', ']'];

    public static List<string> Split(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        foreach (Match end in SentenceEnd().Matches(text))
        {
            if (end.Value.TrimEnd(ClosingMarks) == "." && Abbreviations.Contains(WordBefore(text, end.Index))) continue;
            Add(sentences, text[start..(end.Index + end.Length)]);
            start = end.Index + end.Length;
        }
        Add(sentences, text[start..]);
        return sentences;
    }

    private static string WordBefore(string text, int index)
    {
        var start = index;
        while (start > 0 && char.IsLetter(text[start - 1])) start--;
        return text[start..index];
    }

    private static void Add(List<string> sentences, string part)
    {
        var trimmed = part.Trim();
        if (trimmed.Length > 0) sentences.Add(trimmed);
    }

    // Pontuação final (com aspas ou parênteses que fecham logo depois) seguida de espaço.
    [GeneratedRegex(@"[.!?…]+[""'”’)\]]*(?=\s)")]
    private static partial Regex SentenceEnd();
}
