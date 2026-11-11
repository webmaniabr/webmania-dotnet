using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Webmania;

/// <summary>Source module/pointer and canonical DTO schema hash. Exact module byte hashes are pinned in contracts.lock.json.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SchemaSourceAttribute(string module, string pointer, string sha256) : Attribute
{
    public string Module { get; } = module;
    public string Pointer { get; } = pointer;
    public string Sha256 { get; } = sha256;
}

/// <summary>Default means omitted. Null explicitly writes JSON null. Value keeps zero and false.</summary>
[JsonConverter(typeof(OptionalConverterFactory))]
public readonly struct Optional<T>
{
    public bool IsSet { get; }
    public bool IsNull { get; }
    private readonly T? value;
    public T? Value => IsSet && !IsNull ? value : default;
    private Optional(T? value, bool isNull) { this.value = value; IsSet = true; IsNull = isNull; }
    public static Optional<T> Of(T? value) => new(value, value is null);
    public static Optional<T> FromValue(T? value) => Of(value);
    public static Optional<T> Null => new(default, true);
    public static implicit operator Optional<T>(T value) => Of(value);
    public override string ToString() => IsSet ? "[present]" : "[omitted]";
}
public sealed class OptionalConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);
    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalConverter<>).MakeGenericType(type.GenericTypeArguments[0]))!;
    private sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        public override bool HandleNull => true;
        public override Optional<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? Optional<T>.Null : Optional<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options));
        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (!value.IsSet || value.IsNull) writer.WriteNullValue();
            else JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}

/// <summary>Exact JSON number. Use text for quantities outside decimal's precision; no binary floating-point coercion.</summary>
[JsonConverter(typeof(JsonNumberConverter))]
public readonly struct JsonNumber
{
    public string Text { get; }
    public JsonNumber(string text)
    {
        if (text is null || text.Length > 4096 || !Regex.IsMatch(text, @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Expected an exact JSON number.");
        Text = text;
    }
    public static implicit operator JsonNumber(int value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static implicit operator JsonNumber(long value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static implicit operator JsonNumber(decimal value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public override string ToString() => "[number]";
}
public sealed class JsonNumberConverter : JsonConverter<JsonNumber>
{
    public override JsonNumber Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Number) throw new JsonException("Expected JSON number.");
        return new(doc.RootElement.GetRawText());
    }
    public override void Write(Utf8JsonWriter writer, JsonNumber value, JsonSerializerOptions options) => writer.WriteRawValue(value.Text ?? "0", false);
}

public abstract class JsonModel : IJsonOnSerializing
{
    [JsonExtensionData] public Dictionary<string, JsonElement> Extras { get; set; } = new(StringComparer.Ordinal);
    void IJsonOnSerializing.OnSerializing()
    {
        var declared = GetType().GetProperties().Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (Extras is null || Extras.Keys.Any(declared.Contains)) throw new JsonException("Extras cannot shadow declared fields, including omitted fields.");
    }
    public sealed override string ToString() => $"{GetType().Name} [redacted]";
}
public abstract class JsonUnion
{
    [JsonIgnore] public JsonElement Value { get; }
    protected JsonUnion(JsonElement value) => Value = value.Clone();
    public sealed override string ToString() => $"{GetType().Name} [redacted]";
}
public sealed class JsonUnionConverter<T> : JsonConverter<T> where T : JsonUnion
{
    public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return (T)Activator.CreateInstance(typeof(T), doc.RootElement)!;
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => value.Value.WriteTo(writer);
}
public static class WebmaniaJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { MaxDepth = 96, PropertyNameCaseInsensitive = false, Encoder = ScalarJavaScriptEncoder.Instance };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    public static JsonElement ToElement<T>(T value)
    {
        try
        {
            // Exactly one serializer invocation: getters and custom converters are not prewalked.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
            UnicodeScalars.ValidateJson(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = Options.MaxDepth });
            return document.RootElement.Clone();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { throw new JsonException("JSON serialization failed. Check Unicode scalar text, omitted/null fields and extension collisions."); }
    }
    public static T? FromElement<T>(JsonElement value)
    {
        UnicodeScalars.ValidateElement(value);
        return value.Deserialize<T>(Options);
    }
}

internal readonly struct ExactDecimal : IComparable<ExactDecimal>
{
    private readonly BigInteger coefficient;
    private readonly BigInteger exponent;
    private ExactDecimal(BigInteger c, BigInteger e) { coefficient = c; exponent = e; }
    public static bool TryParse(string? text, out ExactDecimal result)
    {
        result = default;
        if (text is null || text.Length > 4096) return false;
        var m = Regex.Match(text, @"^([+-]?)([0-9]*)(?:\.([0-9]*))?(?:[eE]([+-]?[0-9]+))?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!m.Success || m.Groups[2].Length + m.Groups[3].Length == 0) return false;
        var digits = m.Groups[2].Value + m.Groups[3].Value;
        var c = BigInteger.Parse((m.Groups[1].Value == "-" ? "-" : "") + digits, System.Globalization.CultureInfo.InvariantCulture);
        var e = (m.Groups[4].Success ? BigInteger.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) : BigInteger.Zero) - m.Groups[3].Length;
        result = new(c, e); return true;
    }
    public int CompareTo(ExactDecimal other)
    {
        if (coefficient.Sign != other.coefficient.Sign) return coefficient.Sign.CompareTo(other.coefficient.Sign);
        if (coefficient.IsZero) return 0;
        var a = BigInteger.Abs(coefficient).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var b = BigInteger.Abs(other.coefficient).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var mag = ((BigInteger)a.Length + exponent).CompareTo((BigInteger)b.Length + other.exponent);
        if (mag == 0) mag = StringComparer.Ordinal.Compare(a.PadRight(Math.Max(a.Length, b.Length), '0'), b.PadRight(Math.Max(a.Length, b.Length), '0'));
        return mag * coefficient.Sign;
    }
    public bool TryToInteger(int maximumDigits, out BigInteger value)
    {
        value = default;
        if (!IsInteger) return false;
        if (coefficient.IsZero) return true;
        var digits = BigInteger.Abs(coefficient).ToString(System.Globalization.CultureInfo.InvariantCulture).Length + exponent;
        if (digits < 1 || digits > maximumDigits) return false;
        value = exponent >= 0 ? coefficient * BigInteger.Pow(10, (int)exponent) : coefficient / BigInteger.Pow(10, (int)-exponent);
        return true;
    }
    public bool IsInteger => coefficient.IsZero || exponent >= 0 || (exponent >= -4096 && coefficient % BigInteger.Pow(10, (int)-exponent) == 0);
}
