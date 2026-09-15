namespace Liviate.Rag.Exceptions;

/// <summary>
/// A file's sniffed content type isn't one of the v1 supported types (.pdf, .docx, .md, .txt,
/// .csv, .json, .html). OCR/image ingestion is out of scope for v1 and throws this rather than
/// failing silently or half-parsing.
/// </summary>
public class UnsupportedFileTypeException : LiviateException
{
    public UnsupportedFileTypeException(string message) : base(message) { }
}
