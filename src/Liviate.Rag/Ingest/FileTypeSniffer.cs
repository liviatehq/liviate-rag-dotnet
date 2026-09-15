using System.Text;
using System.Text.Json;
using Liviate.Rag.Exceptions;

namespace Liviate.Rag.Ingest;

/// <summary>
/// Detects file type from actual bytes -- never a filename extension, per the project brief.
/// .NET has no single dominant libmagic equivalent, so this implements lightweight magic-byte
/// signature checks for the fixed v1 file set directly, falling back to content-based
/// heuristics for the text-ish types the way the Python reference's sniff_file_type() does too.
/// </summary>
public static class FileTypeSniffer
{
    private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();
    private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 }; // "PK\x03\x04"

    public static string Sniff(byte[] data)
    {
        if (StartsWith(data, PdfSignature))
        {
            return "pdf";
        }

        if (StartsWith(data, ZipSignature) && LooksLikeDocx(data))
        {
            return "docx";
        }

        if (LooksLikeText(data))
        {
            var text = Encoding.UTF8.GetString(data).Trim();

            if (text.StartsWith('{') || text.StartsWith('['))
            {
                try
                {
                    using var _ = JsonDocument.Parse(text);
                    return "json";
                }
                catch (JsonException)
                {
                    // fall through to the other text-ish checks
                }
            }

            var lines = text.Split('\n');
            if (lines.Length >= 2 &&
                lines[0].Count(c => c == ',') > 0 &&
                lines[0].Count(c => c == ',') == lines[1].Count(c => c == ','))
            {
                return "csv";
            }

            if (text.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase))
            {
                return "html";
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^#{1,6}\s", System.Text.RegularExpressions.RegexOptions.Multiline) ||
                text.Contains("```"))
            {
                return "md";
            }

            return "txt";
        }

        throw new UnsupportedFileTypeException(
            "Unsupported file type (content did not match any supported signature). Supported " +
            "types: pdf, docx, md, txt, csv, json, html. Image/OCR ingestion is not supported.");
    }

    private static bool StartsWith(byte[] data, byte[] signature)
    {
        if (data.Length < signature.Length)
        {
            return false;
        }
        for (var i = 0; i < signature.Length; i++)
        {
            if (data[i] != signature[i])
            {
                return false;
            }
        }
        return true;
    }

    private static bool LooksLikeDocx(byte[] data)
    {
        // A DOCX is a ZIP containing "[Content_Types].xml" -- cheap way to distinguish it from
        // an arbitrary ZIP without a full archive read.
        var marker = "[Content_Types].xml"u8.ToArray();
        var haystack = data.AsSpan(0, Math.Min(data.Length, 4096));
        return haystack.IndexOf(marker) >= 0;
    }

    private static bool LooksLikeText(byte[] data)
    {
        // A cheap "is this mostly printable UTF-8" check: no NUL bytes in a leading sample,
        // and it decodes as valid UTF-8.
        var sample = data.AsSpan(0, Math.Min(data.Length, 8192));
        if (sample.Contains((byte)0))
        {
            return false;
        }
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(sample);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
