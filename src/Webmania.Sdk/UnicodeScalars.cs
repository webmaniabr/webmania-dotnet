using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Webmania;

// Text validity only: no normalization, replacement, schema interpretation or object prewalk.
internal static class UnicodeScalars
{
    private const string InvalidText = "JSON text must contain valid Unicode scalar values.";
    internal static bool IsWellFormed(ReadOnlySpan<char> text, bool allowIncompleteTail = false)
    {
        for (var at = 0; at < text.Length; at++)
        {
            if (!char.IsSurrogate(text[at])) continue;
            if (!char.IsHighSurrogate(text[at])) return false;
            if (++at == text.Length) return allowIncompleteTail;
            if (!char.IsLowSurrogate(text[at])) return false;
        }
        return true;
    }
    internal static void ValidateUtf16(ReadOnlySpan<char> text, bool allowIncompleteTail = false)
    { if (!IsWellFormed(text, allowIncompleteTail)) throw new JsonException(InvalidText); }
    internal static void ValidateUtf8(ReadOnlySpan<byte> text, bool allowIncompleteTail = false)
    {
        while (!text.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(text, out _, out var consumed);
            if (status == OperationStatus.NeedMoreData && allowIncompleteTail) return;
            if (status != OperationStatus.Done) throw new JsonException(InvalidText);
            text = text[consumed..];
        }
    }
    internal static void ValidateJson(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateUtf8(bytes);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 96 });
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The framework's strict unescaper rejects unmatched escaped surrogates;
                // lexical JSON parsing alone deliberately does not establish that property.
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
                    ValidateUtf16(reader.GetString().AsSpan());
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { throw new JsonException(InvalidText); }
    }
    internal static void ValidateElement(JsonElement element)
    {
        try { ValidateJson(Encoding.UTF8.GetBytes(element.GetRawText())); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { throw new JsonException(InvalidText); }
    }
}

// The real writer boundary also covers extension names, JsonElement.WriteTo and
// custom converters writing values/names directly. Default escaping stays identical.
// Partial scalar tails are allowed only provisionally by the streaming encoder;
// a final write must complete them, or fail before the framework can replace them.
internal sealed class ScalarJavaScriptEncoder : JavaScriptEncoder
{
    internal static readonly ScalarJavaScriptEncoder Instance = new();
    public override int MaxOutputCharactersPerInputCharacter => Default.MaxOutputCharactersPerInputCharacter;
    public override bool WillEncode(int unicodeScalar) => Default.WillEncode(unicodeScalar);
    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        UnicodeScalars.ValidateUtf16(new ReadOnlySpan<char>(text, textLength), allowIncompleteTail: true);
        return Default.FindFirstCharacterToEncode(text, textLength);
    }
    public override int FindFirstCharacterToEncodeUtf8(ReadOnlySpan<byte> utf8Text)
    {
        UnicodeScalars.ValidateUtf8(utf8Text, allowIncompleteTail: true);
        return Default.FindFirstCharacterToEncodeUtf8(utf8Text);
    }
    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten) =>
        Default.TryEncodeUnicodeScalar(unicodeScalar, buffer, bufferLength, out numberOfCharactersWritten);
    public override OperationStatus Encode(ReadOnlySpan<char> source, Span<char> destination, out int charsConsumed, out int charsWritten, bool isFinalBlock = true)
    {
        UnicodeScalars.ValidateUtf16(source, allowIncompleteTail: !isFinalBlock);
        return Default.Encode(source, destination, out charsConsumed, out charsWritten, isFinalBlock);
    }
    public override OperationStatus EncodeUtf8(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
    {
        UnicodeScalars.ValidateUtf8(source, allowIncompleteTail: !isFinalBlock);
        return Default.EncodeUtf8(source, destination, out bytesConsumed, out bytesWritten, isFinalBlock);
    }
    public override string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value); UnicodeScalars.ValidateUtf16(value);
        return Default.Encode(value);
    }
}
