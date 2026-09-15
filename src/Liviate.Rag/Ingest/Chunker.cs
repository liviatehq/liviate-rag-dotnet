using System.Text.RegularExpressions;

namespace Liviate.Rag.Ingest;

/// <summary>
/// Text chunking for Ingest(). A simple, dependency-free splitter: fills chunks up to
/// <c>chunkSize</c> characters, preferring to break on a paragraph boundary, then a sentence
/// boundary, then a plain word boundary as a fallback, and carries <c>overlap</c> characters of
/// context into the next chunk so a fact split across a boundary isn't lost. Good enough for the
/// sizes real documents chunk into (typically a few hundred to a couple thousand characters) --
/// not attempting token-exact sizing, which would require pulling in a tokenizer this package
/// otherwise has no use for.
/// </summary>
public static partial class Chunker
{
    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    public static IReadOnlyList<string> ChunkText(string text, int chunkSize = 1000, int overlap = 150)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }
        if (text.Length <= chunkSize)
        {
            return new[] { text };
        }

        var chunks = new List<string>();
        var start = 0;
        var n = text.Length;
        while (start < n)
        {
            var end = Math.Min(start + chunkSize, n);
            if (end < n)
            {
                end = BestBreak(text, start, end);
            }
            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }
            if (end >= n)
            {
                break;
            }
            start = Math.Max(end - overlap, start + 1);
        }
        return chunks;
    }

    private static int BestBreak(string text, int start, int end)
    {
        var window = text[start..end];

        Match? lastPara = null;
        foreach (Match m in ParagraphBreak().Matches(window))
        {
            lastPara = m;
        }
        if (lastPara is not null && lastPara.Index + lastPara.Length > window.Length * 0.5)
        {
            return start + lastPara.Index + lastPara.Length;
        }

        Match? lastSentence = null;
        foreach (Match m in SentenceBreak().Matches(window))
        {
            lastSentence = m;
        }
        if (lastSentence is not null && lastSentence.Index + lastSentence.Length > window.Length * 0.5)
        {
            return start + lastSentence.Index + lastSentence.Length;
        }

        var lastSpace = window.LastIndexOf(' ');
        if (lastSpace > window.Length * 0.5)
        {
            return start + lastSpace + 1;
        }

        return end;
    }
}
