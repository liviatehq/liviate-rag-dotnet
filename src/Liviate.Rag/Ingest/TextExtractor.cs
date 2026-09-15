using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using Liviate.Rag.Exceptions;
using UglyToad.PdfPig;

namespace Liviate.Rag.Ingest;

/// <summary>
/// Plain-text extraction per supported source type. Each function takes raw bytes and returns
/// text ready for chunking. Markdown/plain text are returned as-is (their own syntax is already
/// readable, no need to strip it for a RAG chunk); the others are converted to plain text since
/// their native format has no value once chunked.
/// </summary>
public static partial class TextExtractor
{
    public static string ExtractText(byte[] data, string sourceType) => sourceType switch
    {
        "md" or "txt" => Encoding.UTF8.GetString(data),
        "pdf" => ExtractPdf(data),
        "docx" => ExtractDocx(data),
        "html" => ExtractHtml(data),
        "csv" => ExtractCsv(data),
        "json" => ExtractJson(data),
        _ => throw new UnsupportedFileTypeException($"No text extractor for source type '{sourceType}'."),
    };

    private static string ExtractPdf(byte[] data)
    {
        using var document = PdfDocument.Open(data);
        var pages = document.GetPages().Select(p => p.Text ?? string.Empty);
        return string.Join("\n\n", pages).Trim();
    }

    private static string ExtractDocx(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var paragraph in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }
        foreach (var table in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>())
        {
            foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
            {
                var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>()
                    .Select(c => c.InnerText.Trim())
                    .ToList();
                if (cells.Any(c => c.Length > 0))
                {
                    parts.Add(string.Join(" | ", cells));
                }
            }
        }
        return string.Join("\n\n", parts).Trim();
    }

    [GeneratedRegex("<(script|style|noscript)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SkipTagBlock();

    [GeneratedRegex("<(p|br|div|li|tr|h[1-6])[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagOpen();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex CollapseSpaces();

    private static string ExtractHtml(byte[] data)
    {
        var html = Encoding.UTF8.GetString(data);
        html = SkipTagBlock().Replace(html, "\n");
        html = BlockTagOpen().Replace(html, "\n");
        html = AnyTag().Replace(html, " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        html = CollapseSpaces().Replace(html, " ");

        var lines = html.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
        return string.Join("\n", lines);
    }

    private static string ExtractCsv(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var rows = ParseCsv(text);
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var header = rows[0];
        var lines = new List<string>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var pairs = new List<string>();
            for (var i = 0; i < Math.Min(header.Count, row.Count); i++)
            {
                pairs.Add($"{header[i]}: {row[i]}");
            }
            lines.Add(string.Join(", ", pairs));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Minimal RFC 4180 CSV parser (quoted fields, embedded commas/newlines, "" escaping).</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    i++;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    i++;
                    break;
                case '\r':
                    i++;
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    i++;
                    break;
                default:
                    field.Append(c);
                    i++;
                    break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    private static string ExtractJson(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        using var document = JsonDocument.Parse(text);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
