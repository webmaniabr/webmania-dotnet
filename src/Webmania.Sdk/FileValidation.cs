using System.IO.Compression;
using System.Text;
using System.Xml;
namespace Webmania;
internal static class FileValidation
{
    internal const string Spreadsheet = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    /// <summary>A legacy report labeled text/xml: a ZIP local-file signature is XLSX, any other body is CSV
    /// (kept byte-exact); an XML, HTML or JSON body is refused later as malformed CSV.</summary>
    internal static string LegacyReportMedia(byte[] bytes) => bytes.AsSpan().StartsWith("PK\u0003\u0004"u8) ? Spreadsheet : "text/csv";
    private static bool MarkupOrJson(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) span = span[3..];
        else if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return true;
        var i = 0; while (i < span.Length && span[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        return i < span.Length && span[i] is (byte)'<' or (byte)'{';
    }
    internal static FileReadiness Inspect(string media, byte[] bytes, IReadOnlyList<string> allowed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (media == "text/html") return FileReadiness.NotFile; // Explicit non-file representation; never executed or classified as IsFile.
        if (!allowed.Contains(media, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException();
        try
        {
            switch (media)
            {
                case "application/octet-stream":
                    return FileReadiness.Unsupported; // The contract is opaque; FileResult keeps Other/IsFile=false and preserves bytes.
                case "application/pdf":
                    return PdfStructure.Inspect(bytes, token);
                case "application/xml": case "text/xml":
                    using (var input = new MemoryStream(bytes, writable: false))
                    using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = bytes.LongLength * 2 + 1 }))
                    {
                        var rootSeen = false;
                        while (reader.Read())
                        {
                            token.ThrowIfCancellationRequested();
                            if (reader.Depth > 128) throw new InvalidDataException();
                            if (reader.NodeType == XmlNodeType.Element && !rootSeen)
                            { if (reader.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(); rootSeen = true; }
                        }
                        return rootSeen ? FileReadiness.Ready : throw new InvalidDataException();
                    }
                case "application/zip": case "application/x-zip-compressed":
                case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
                    return ZipStructure.Inspect(bytes, media == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", token);
                case "text/csv":
                    // Checked on the bytes, never decoded: legacy exports may be Latin-1 as well as UTF-8.
                    return bytes.Length > 0 && bytes.AsSpan().IndexOf((byte)0) < 0 && !MarkupOrJson(bytes) && !bytes.AsSpan().StartsWith("PK"u8) && !bytes.AsSpan().StartsWith("%PDF-"u8) ? FileReadiness.Ready : throw new InvalidDataException();
                default: throw new InvalidDataException();
            }
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or DecoderFallbackException or ArgumentException or IOException or FormatException or OverflowException) { throw new InvalidDataException("File structure is not supported or coherent."); }
    }
}
