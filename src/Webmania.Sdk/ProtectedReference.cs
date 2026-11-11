using System.Text.Json;
using System.Text.Json.Serialization;
namespace Webmania;

/// <summary>Retains a consultation selector without exposing it in ordinary diagnostics or JSON.</summary>
[JsonConverter(typeof(ProtectedReferenceConverter))]
public sealed class ProtectedReference
{
    private readonly string? value;
    public string? Selector { get; }
    public string Fingerprint { get; }
    public bool CanReveal => value is not null;
    private ProtectedReference(string? selector, string? value, string fingerprint) { Selector = selector; this.value = value; Fingerprint = fingerprint; }
    internal static ProtectedReference Diagnostic(string fingerprint) => new(null, null, fingerprint);
    internal static ProtectedReference Retained(string selector, string value, string fingerprint) => new(selector, value, fingerprint);
    /// <summary>Explicitly returns the retained selector value. Treat it as fiscal data; do not log it.</summary>
    public string Reveal() => value ?? throw new InvalidOperationException("No consultation identifier was retained for this operation.");
    /// <summary>Explicit API1 own-document consultation parameters. API2 callers may use Reveal with their named consultation method.</summary>
    public NfeGetDocumentParameters ToLookupParameters()
    {
        if (Selector is not ("uuid" or "chave" or "ID" or "id")) throw new InvalidOperationException("Use the corresponding service consultation method with Reveal().");
        return WebmaniaJson.FromElement<NfeGetDocumentParameters>(WebmaniaJson.ToElement(new Dictionary<string, string> { [Selector] = Reveal() }))!;
    }
    public override string ToString() => $"ProtectedReference({Selector ?? "unavailable"}) [redacted]";
}
public sealed class ProtectedReferenceConverter : JsonConverter<ProtectedReference>
{
    public override ProtectedReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new JsonException("Protected references cannot be restored from diagnostic JSON.");
    public override void Write(Utf8JsonWriter writer, ProtectedReference value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();writer.WriteString("selector", value.Selector);writer.WriteString("fingerprint", value.Fingerprint);writer.WriteBoolean("canReveal", value.CanReveal);writer.WriteString("value", "[redacted]");writer.WriteEndObject();
    }
}
