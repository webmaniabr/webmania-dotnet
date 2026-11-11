using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Webmania;

public sealed record ApiErrorDetail(string? Code, string Message, string? Field);
[System.Text.Json.Serialization.JsonConverter(typeof(WebmaniaExceptionConverter))]
public sealed class WebmaniaException : Exception
{
    public string OperationId { get; }
    public string Kind { get; }
    public int? StatusCode { get; }
    public bool OutcomeUncertain { get; }
    public ProtectedReference? SafeReference { get; }
    public string? CorrelationId { get; }
    public IReadOnlyList<ApiErrorDetail> Details { get; }
    public string RecoveryAdvice => OutcomeUncertain ? "Consulte o resultado anterior pela referência original antes de decidir por novo envio. Nenhum reenvio automático foi feito." : "Revise os detalhes e os parâmetros da operação.";
    internal WebmaniaException(string op, string kind, string message, int? status = null, bool uncertain = false, ProtectedReference? reference = null, string? correlation = null, IReadOnlyList<ApiErrorDetail>? details = null) : base(message)
    {
        OperationId = op; Kind = kind; StatusCode = status; OutcomeUncertain = uncertain; SafeReference = reference; CorrelationId = correlation; Details = details ?? [];
    }
    internal static WebmaniaException Local(string op, string kind, string message) => new(op, kind, message);
    public override string ToString() => $"WebmaniaException({OperationId}, {Kind}, status={StatusCode}, uncertain={OutcomeUncertain}): {Message}";
}
internal sealed class Redactor(CancellationToken cancellationToken = default)
{
    /// <summary>Strict mode redacts every UUID and long opaque token, not only remembered ones.</summary>
    internal bool Strict { get; init; }
    private static readonly System.Text.RegularExpressions.Regex OpaqueToken = new(@"[A-Za-z0-9_\-]{20,}", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly SortedSet<string> literals = new(Comparer<string>.Create((a, b) =>
    {
        var length = b.Length.CompareTo(a.Length);
        return length != 0 ? length : StringComparer.Ordinal.Compare(a, b);
    }));
    private readonly HashSet<string> strongLiterals = new(StringComparer.Ordinal);
    private readonly HashSet<string> boundaryLiterals = new(StringComparer.Ordinal);
    private readonly HashSet<string> uuids = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> digitSequences = new(StringComparer.Ordinal);
    private static readonly Regex Uuid = new(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Url = new(@"\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s<>\""']+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    internal void Add(string? text, bool observed = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(text)) return;
        literals.Add(text);
        if (!observed) strongLiterals.Add(text);
        else if (text.Length < 4) boundaryLiterals.Add(text);
        var encoded = JsonSerializer.Serialize(text, WebmaniaJson.Options);
        if (encoded.Length >= 2) { literals.Add(encoded[1..^1]); if (!observed) strongLiterals.Add(encoded[1..^1]); }
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq >= 0) { var v = Uri.UnescapeDataString(pair[(eq + 1)..]); if (v.Length > 0) { literals.Add(v); if (!observed) strongLiterals.Add(v); else if (v.Length < 4) boundaryLiterals.Add(v); } }
            }

        var normalized = NormalizeDigits(text);
        if (normalized.All(c => c is >= '0' and <= '9') && normalized.Length > 0 && (!observed || normalized.Length >= 4)) digitSequences.Add(normalized);
    }
    internal void AddCapability(string? value, bool uuidCapability = false)
    {
        if (string.IsNullOrEmpty(value)) return;
        Add(value, observed: true);
        if (uuidCapability && Guid.TryParseExact(value, "D", out _)) uuids.Add(value);
        if (Uri.TryCreate(value, UriKind.Absolute, out var url) && IsReportCapabilityUrl(url)) uuids.Add(url.GetComponents(UriComponents.Path, UriFormat.Unescaped).TrimEnd('/').Split('/')[^1]);
    }
    internal void AddLocation(string value)
    {
        foreach (var component in LocationMetadata.DiagnosticValues(value)) Add(component, observed: true);
        foreach (var uuid in LocationMetadata.PathUuids(value)) uuids.Add(uuid);
    }
    internal static bool IsReportCapabilityUrl(Uri url)
    {
        var parts = url.GetComponents(UriComponents.Path, UriFormat.Unescaped).TrimEnd('/').Split('/');
        return parts.Length >= 2 && parts[^2] == "relatorio" && Guid.TryParseExact(parts[^1], "D", out _);
    }
    internal void AddRequestJson(JsonElement value, JsonElement schema, JsonElement document)
    {
        // Reuse pinned schema vocabulary only to distinguish declared names from
        // extension names. Validation and serialization still have one engine.
        var declared = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        void Properties(JsonElement node, int depth)
        {
            if (depth > 96 || node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("$ref", out var reference))
            {
                var target = document;
                foreach (var part in reference.GetString()![2..].Split('/')) target = target.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
                Properties(target, depth + 1);
            }
            if (node.TryGetProperty("properties", out var properties)) foreach (var p in properties.EnumerateObject()) declared.TryAdd(p.Name, p.Value);
            foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" }) if (node.TryGetProperty(keyword, out var variants)) foreach (var variant in variants.EnumerateArray()) Properties(variant, depth + 1);
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            Properties(schema, 0);
            foreach (var p in value.EnumerateObject())
            {
                if (!declared.TryGetValue(p.Name, out var child)) { if (SensitiveKey(p.Name)) Add(p.Name); child = default; }
                AddRequestJson(p.Value, child, document);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var items = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("items", out var item) ? item : default;
            foreach (var child in value.EnumerateArray()) AddRequestJson(child, items, document);
        }
        else if (value.ValueKind == JsonValueKind.String) Add(value.GetString());
        else if (value.ValueKind == JsonValueKind.Number) Add(value.GetRawText());
    }
    internal void AddJson(JsonElement value, bool sensitive, bool reportCapabilities = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var p in value.EnumerateObject())
            {
                var secret = sensitive || SensitiveKey(p.Name);

                if (reportCapabilities && p.Name == "uuid" && p.Value.ValueKind == JsonValueKind.String) AddCapability(p.Value.GetString(), true);
                AddJson(p.Value, secret, reportCapabilities);
            }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var v in value.EnumerateArray()) AddJson(v, sensitive, reportCapabilities);
        else if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (sensitive) Add(text);
            foreach (Match m in Url.Matches(text)) AddCapability(m.Value);
        }
        else if (sensitive && value.ValueKind == JsonValueKind.Number) Add(value.GetRawText());
    }
    private static bool SensitiveKey(string key)
    {
        var normalized = new string(key.Normalize(NormalizationForm.FormKD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsAsciiLetterOrDigit(c)).ToArray()).ToLowerInvariant();
        return new[] { "token", "secret", "password", "passwd", "authorization", "credential", "certificate", "certificado", "senha", "segredo", "credenci", "autorizacao", "apikey", "privatekey", "consumerkey", "chaveapi", "chaveprivada", "chavesecreta", "chaveconsumidor", "cpf", "cnpj", "email" }.Any(normalized.Contains);
    }
    internal bool IsSensitive(string value) => Clean(value) != value;
    internal string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length > 8192) return "API message exceeded the diagnostic limit.";
        // Collect all spans against the original text, then merge their union. Replacing
        // incrementally would destroy overlap evidence and expose a credential suffix.
        var spans = new List<(int Start, int End)>();
        void Span(int start, int length) { if (length > 0) spans.Add((start, start + length)); }
        foreach (Match m in Url.Matches(text)) Span(m.Index, m.Length);
        foreach (Match m in Uuid.Matches(text)) if (Strict || uuids.Contains(m.Value)) Span(m.Index, m.Length);
        if (Strict) foreach (Match m in OpaqueToken.Matches(text)) Span(m.Index, m.Length);
        foreach (var literal in literals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var at = 0; at <= text.Length - literal.Length;)
            {
                var found = text.IndexOf(literal, at, StringComparison.Ordinal); if (found < 0) break;
                // Observed short capabilities use component boundaries. Actual credentials
                // deliberately retain literal substring semantics (including 1234 in 912345).
                var boundary = boundaryLiterals.Contains(literal) && !strongLiterals.Contains(literal) && literal.Length < 4;
                if (!boundary || (found == 0 || !char.IsAsciiLetterOrDigit(text[found - 1])) && (found + literal.Length == text.Length || !char.IsAsciiLetterOrDigit(text[found + literal.Length]))) Span(found, literal.Length);
                at = found + 1;
            }
        }
        foreach (Match m in Regex.Matches(text, @"\p{Nd}+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            if (digitSequences.Any(s => NormalizeDigits(m.Value).Contains(s, StringComparison.Ordinal))) Span(m.Index, m.Length);
        foreach (Match m in Regex.Matches(text, @"(?i)\b(?:authorization|bearer|access[_-]?token|consumer[_-]?secret|password|senha|certificate|certificado)\s*[:=]\s*[^\s,;}]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) Span(m.Index, m.Length);
        var output = new StringBuilder(); var cursor = 0;
        foreach (var span in spans.OrderBy(s => s.Start).ThenByDescending(s => s.End))
        {
            if (span.Start > cursor) output.Append(text, cursor, span.Start - cursor);
            if (span.Start >= cursor) output.Append("[redacted]");
            cursor = Math.Max(cursor, span.End);
        }
        output.Append(text, cursor, text.Length - cursor);
        var result = output.ToString();
        return result.Length <= 2048 ? result : result[..2048] + "…";
    }
    private static string NormalizeDigits(string text)
    {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var number = Rune.GetNumericValue(rune);
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber && number is >= 0 and <= 9) result.Append((char)('0' + (int)number));
            else result.Append(rune.ToString());
        }
        return result.ToString();
    }
    internal static string Fingerprint(JsonElement parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(parameters.GetRawText());
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..24];
    }
}

public sealed class WebmaniaExceptionConverter : System.Text.Json.Serialization.JsonConverter<WebmaniaException>
{
    public override WebmaniaException Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new JsonException("Diagnostic exceptions cannot be restored from JSON.");
    public override void Write(Utf8JsonWriter writer, WebmaniaException value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();writer.WriteString("operationId",value.OperationId);writer.WriteString("kind",value.Kind);writer.WriteString("message",value.Message);
        if(value.StatusCode.HasValue)writer.WriteNumber("statusCode",value.StatusCode.Value);else writer.WriteNull("statusCode");
        writer.WriteBoolean("outcomeUncertain",value.OutcomeUncertain);writer.WriteString("correlationId",value.CorrelationId);writer.WritePropertyName("reference");JsonSerializer.Serialize(writer,value.SafeReference,options);writer.WritePropertyName("details");JsonSerializer.Serialize(writer,value.Details,options);writer.WriteString("recoveryAdvice",value.RecoveryAdvice);writer.WriteEndObject();
    }
}

/// <summary>Caller cancellation with ordinary canceled Task semantics and private typed recovery metadata.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(WebmaniaCancellationExceptionConverter))]
public sealed class WebmaniaCancellationException : OperationCanceledException
{
    public WebmaniaException Diagnostic { get; }
    public string OperationId => Diagnostic.OperationId;
    public string Kind => Diagnostic.Kind;
    public int? StatusCode => Diagnostic.StatusCode;
    public bool OutcomeUncertain => Diagnostic.OutcomeUncertain;
    public ProtectedReference? SafeReference => Diagnostic.SafeReference;
    public string? CorrelationId => Diagnostic.CorrelationId;
    public IReadOnlyList<ApiErrorDetail> Details => Diagnostic.Details;
    public string RecoveryAdvice => Diagnostic.RecoveryAdvice;
    internal WebmaniaCancellationException(WebmaniaException diagnostic, CancellationToken token) : base(diagnostic.Message, null, token) => Diagnostic = diagnostic;
    public override string ToString() => $"WebmaniaCancellationException({OperationId}, uncertain={OutcomeUncertain}) [redacted]";
}
public sealed class WebmaniaCancellationExceptionConverter : System.Text.Json.Serialization.JsonConverter<WebmaniaCancellationException>
{
    public override WebmaniaCancellationException Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new JsonException("Diagnostic exceptions cannot be restored from JSON.");
    public override void Write(Utf8JsonWriter writer, WebmaniaCancellationException value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.Diagnostic, options);
}
