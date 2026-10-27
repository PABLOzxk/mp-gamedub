using System.Text;
using MpGameDub.Core.Pipeline;

namespace MpGameDub.Core.Reports;

/// <summary>Legenda .srt com a tradução no tempo da fala original, para conferir a tradução assistindo ao vídeo.</summary>
public static class SubtitleWriter
{
    public static string Srt(IEnumerable<UtteranceTrace> traces)
    {
        var srt = new StringBuilder();
        var n = 0;
        foreach (var t in traces.Where(t => t.Translation.Length > 0).OrderBy(t => t.SpeechStart))
        {
            var end = t.SpeechEnd - t.SpeechStart < TimeSpan.FromSeconds(1) ? t.SpeechStart + TimeSpan.FromSeconds(1) : t.SpeechEnd;
            srt.AppendLine((++n).ToString());
            srt.AppendLine($"{Time(t.SpeechStart)} --> {Time(end)}");
            srt.AppendLine(t.Translation);
            srt.AppendLine();
        }
        return srt.ToString();
    }

    private static string Time(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
}
