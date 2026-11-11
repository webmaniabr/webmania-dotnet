using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Webmania;

internal sealed class ContractOperation
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public string Module { get; set; } = "";
    public string Base { get; set; } = "";
    public string Scope { get; set; } = "";
    public JsonElement Schema { get; set; }
    public JsonElement Parameters { get; set; }
    public JsonElement? Body { get; set; }
    public JsonElement Effects { get; set; }
    public JsonElement Status { get; set; }
    public JsonElement Security { get; set; }
    public List<int> RedirectStatuses { get; set; } = [];
    public List<string> ResponseMedia { get; set; } = [];
    public Dictionary<string, Dictionary<string, JsonElement?>> ResponseSelections { get; set; } = [];
    public JsonElement? ResponseSchema { get; set; }
    public bool Binary { get; set; }
    public bool Enabled { get; set; }
}
public sealed record OperationInfo(string Id, string Method, string Route, string Scope, bool Enabled, string ContractModule, JsonElement ContractStatus);
internal static class Contracts
{
    private static readonly Assembly Assembly = typeof(Contracts).Assembly;
    internal static readonly IReadOnlyDictionary<string, ContractOperation> Operations = LoadOperations();
    private static readonly Dictionary<string, JsonElement> Modules = new();
    private static IReadOnlyDictionary<string, ContractOperation> LoadOperations()
    {
        using var stream = Assembly.GetManifestResourceStream("Webmania.Sdk.operations.json")!;
        return JsonSerializer.Deserialize<List<ContractOperation>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.ToDictionary(x => x.Id);
    }
    internal static JsonElement Module(string path)
    {
        lock (Modules)
        {
            if (Modules.TryGetValue(path, out var value)) return value;
            using var stream = Assembly.GetManifestResourceStream(Path.GetFileName(path))!;
            using var doc = JsonDocument.Parse(stream);
            return Modules[path] = doc.RootElement.Clone();
        }
    }
    internal static void Validate(ContractOperation op, JsonElement parameters, CancellationToken cancellationToken = default)
    {
        var doc = Module(op.Module);
        var error = Validator.Error(op.Schema, parameters, doc, "parameters", cancellationToken: cancellationToken);
        if (error is not null) throw WebmaniaException.Local(op.Id, "validation", error);
        if (op.Id == "nfeGetDocument" && !new[] { "chave", "uuid", "ID", "id" }.Any(k => parameters.TryGetProperty(k, out _)))
            throw WebmaniaException.Local(op.Id, "validation", "At least one document selector is required.");
        if (op.Id == "monitorGetDownloadStatistics" && parameters.TryGetProperty("ano", out _) != parameters.TryGetProperty("mes", out _))
            throw WebmaniaException.Local(op.Id, "validation", "Year and month must be supplied together.");
        if (op.Id == "monitorSearchDocuments" && parameters.TryGetProperty("body", out var body))
        {
            // Both flat and nested fields are validated before applying the provider's promotion rules.
            var schema = doc.GetProperty("components").GetProperty("schemas").GetProperty("MonitorSearchRequest");
            var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var p in body.EnumerateObject()) merged[p.Name] = p.Value;
            foreach (var nested in new[] { "filtros", "filtros_modelo" })
                if (body.TryGetProperty(nested, out var filters) && filters.ValueKind == JsonValueKind.Object)
                    foreach (var p in filters.EnumerateObject())
                        if (schema.GetProperty("properties").GetProperty(nested).GetProperty("properties").TryGetProperty(p.Name, out _)) merged.TryAdd(p.Name, p.Value);
            var effective = WebmaniaJson.ToElement(merged);
            error = Validator.Error(schema, effective, doc, "body", cancellationToken: cancellationToken);
            if (error is not null) throw WebmaniaException.Local(op.Id, "validation", error);
            var sections = new List<JsonElement> { body, effective };
            if (body.TryGetProperty("filtros", out var nestedFilters) && nestedFilters.ValueKind == JsonValueKind.Object) sections.Add(nestedFilters);
            foreach (var section in sections)
                if (section.TryGetProperty("data_inicial", out var start) && section.TryGetProperty("data_final", out var end) && string.CompareOrdinal(start.GetString(), end.GetString()) > 0)
                    throw WebmaniaException.Local(op.Id, "validation", "Date interval must be ascending.");
        }
    }
}
internal static class Validator
{
    internal static string? Error(JsonElement schema, JsonElement value, JsonElement document, string path, int depth = 0, bool response = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 128) return "Contract nesting limit exceeded.";
        if (schema.ValueKind == JsonValueKind.True) return null;
        if (schema.ValueKind == JsonValueKind.False) return "Value is not permitted at " + path;
        if (schema.ValueKind != JsonValueKind.Object) return null;
        string? Check(JsonElement s, JsonElement v, string p) => Error(s, v, document, p, depth + 1, response, cancellationToken);
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var target = document;
            foreach (var part in reference.GetString()![2..].Split('/')) target = target.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
            var refError = Check(target, value, path);
            if (refError is not null) return refError;
        }
        if (schema.TryGetProperty("type", out var types))
        {
            var allowed = types.ValueKind == JsonValueKind.Array ? types.EnumerateArray().Select(x => x.GetString()!).ToArray() : [types.GetString()!];
            if (!allowed.Any(t => MatchesType(t, value))) return "Invalid JSON type at " + path;
        }
        if (!response && schema.TryGetProperty("const", out var constant) && !Equivalent(value, constant)) return "Unexpected constant at " + path;
        if (!response && schema.TryGetProperty("enum", out var enums) && !enums.EnumerateArray().Any(x => Equivalent(value, x))) return "Unsupported request value at " + path;
        if (schema.TryGetProperty("allOf", out var all))
            foreach (var s in all.EnumerateArray()) { var e = Check(s, value, path); if (e is not null) return e; }
        if (schema.TryGetProperty("anyOf", out var any) && !any.EnumerateArray().Any(s => Check(s, value, path) is null)) return "No allowed representation at " + path;
        if (schema.TryGetProperty("oneOf", out var one) && one.EnumerateArray().Count(s => Check(s, value, path) is null) != 1) return "Expected one allowed representation at " + path;
        if (schema.TryGetProperty("not", out var not) && Check(not, value, path) is null) return "Forbidden combination at " + path;
        if (schema.TryGetProperty("if", out var condition))
        {
            var keyword = Check(condition, value, path) is null ? "then" : "else";
            if (schema.TryGetProperty(keyword, out var branch)) { var e = Check(branch, value, path); if (e is not null) return e; }
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) if (!seen.Add(p.Name)) return "Duplicate JSON property at " + path;
            if (schema.TryGetProperty("required", out var required))
                foreach (var r in required.EnumerateArray()) if (!value.TryGetProperty(r.GetString()!, out _)) return "Missing required field at " + path + "." + r.GetString();
            if (schema.TryGetProperty("dependentRequired", out var dependencies))
                foreach (var d in dependencies.EnumerateObject())
                    if (value.TryGetProperty(d.Name, out _))
                        foreach (var r in d.Value.EnumerateArray()) if (!value.TryGetProperty(r.GetString()!, out _)) return "Missing dependent field at " + path + "." + r.GetString();
            if (schema.TryGetProperty("minProperties", out var minP) && seen.Count < (int)minP.GetDecimal() || schema.TryGetProperty("maxProperties", out var maxP) && seen.Count > (int)maxP.GetDecimal()) return "Invalid property count at " + path;
            var hasProps = schema.TryGetProperty("properties", out var props);
            foreach (var p in value.EnumerateObject())
            {
                if (hasProps && props.TryGetProperty(p.Name, out var propertySchema))
                { var e = Check(propertySchema, p.Value, path + "." + p.Name); if (e is not null) return e; }
                else if (schema.TryGetProperty("additionalProperties", out var additional))
                { var e = Check(additional, p.Value, path + ".<extra>"); if (e is not null) return e; }
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var length = value.GetArrayLength();
            if (schema.TryGetProperty("minItems", out var min) && length < (int)min.GetDecimal() || schema.TryGetProperty("maxItems", out var max) && length > (int)max.GetDecimal()) return "Invalid array length at " + path;
            if (schema.TryGetProperty("items", out var items))
                foreach (var item in value.EnumerateArray()) { var e = Check(items, item, path + "[]"); if (e is not null) return e; }
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!; var length = text.EnumerateRunes().Count();
            if (schema.TryGetProperty("minLength", out var min) && length < (int)min.GetDecimal() || schema.TryGetProperty("maxLength", out var max) && length > (int)max.GetDecimal()) return "Invalid text length at " + path;
            if (schema.TryGetProperty("pattern", out var pattern))
            {
                // Published Monitor numeric-text patterns contain doubled escapes. Interpret their intended numeric grammar explicitly.
                var p = pattern.GetString()!;
                p = p.Replace(@"\\d", @"\d", StringComparison.Ordinal);
                var valid = Regex.IsMatch(text, p, RegexOptions.ECMAScript | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!valid) return "Invalid text format at " + path;
            }
            if (schema.TryGetProperty("format", out var format))
            {
                var valid = format.GetString() switch
                {
                    "uuid" => Guid.TryParseExact(text, "D", out _),
                    "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                    "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                    "uri" => Uri.TryCreate(text, UriKind.Absolute, out _),
                    "email" => System.Net.Mail.MailAddress.TryCreate(text, out var address) && address.Address == text && !text.Any(char.IsWhiteSpace),
                    _ => true
                };
                if (!valid) return "Invalid format at " + path;
            }
        }
        var numericText = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var isNumericConstraint = value.ValueKind == JsonValueKind.Number || schema.TryGetProperty("x-sdk-numeric-minimum", out _) || schema.TryGetProperty("x-sdk-numeric-maximum", out _);
        if (isNumericConstraint && numericText is not null && !ExactDecimal.TryParse(numericText, out _)) return "Numeric representation exceeds the supported validation limit at " + path;
        if (value.ValueKind == JsonValueKind.String && numericText?.Length > 4096 && schema.TryGetProperty("anyOf", out var numericAlternatives) && numericAlternatives.EnumerateArray().Any(b => b.TryGetProperty("minimum", out _) || b.TryGetProperty("maximum", out _))) return "Numeric representation exceeds the supported validation limit at " + path;
        if (ExactDecimal.TryParse(numericText, out var number))
        {
            foreach (var key in new[] { "minimum", "maximum", "x-sdk-numeric-minimum", "x-sdk-numeric-maximum" })
                if (schema.TryGetProperty(key, out var limit) && ExactDecimal.TryParse(limit.ValueKind == JsonValueKind.String ? limit.GetString() : limit.GetRawText(), out var bound))
                    if (key.EndsWith("minimum", StringComparison.Ordinal) ? number.CompareTo(bound) < 0 : number.CompareTo(bound) > 0) return "Numeric bound exceeded at " + path;
            // Numeric text unions inherit the numeric branch bounds, regardless of the selected string representation.
            if (value.ValueKind == JsonValueKind.String && schema.TryGetProperty("anyOf", out var branches))
                foreach (var b in branches.EnumerateArray())
                    if (b.TryGetProperty("type", out var bt) && bt.ValueKind == JsonValueKind.String && bt.GetString() is "number" or "integer")
                        foreach (var key in new[] { "minimum", "maximum" })
                            if (b.TryGetProperty(key, out var limit) && ExactDecimal.TryParse(limit.GetRawText(), out var bound))
                                if (key == "minimum" ? number.CompareTo(bound) < 0 : number.CompareTo(bound) > 0) return "Numeric bound exceeded at " + path;
        }
        return null;
    }
    private static bool Equivalent(JsonElement a, JsonElement b) => a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number && ExactDecimal.TryParse(a.GetRawText(), out var x) && ExactDecimal.TryParse(b.GetRawText(), out var y) ? x.CompareTo(y) == 0 : JsonElement.DeepEquals(a, b);
    private static bool MatchesType(string type, JsonElement value) => type switch
    {
        "null" => value.ValueKind == JsonValueKind.Null,
        "string" => value.ValueKind == JsonValueKind.String,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && ExactDecimal.TryParse(value.GetRawText(), out var n) && n.IsInteger,
        _ => false
    };
}
