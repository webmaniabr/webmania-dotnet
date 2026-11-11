using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Webmania;
/// <summary>A returned Location is metadata only. Revealing it never performs a request or authorizes its origin.</summary>
[JsonConverter(typeof(ProtectedLocationConverter))]
public sealed class ProtectedLocation
{
    private readonly string location;
    internal ProtectedLocation(string location) => this.location = location;
    public bool CanReveal => true;
    public string Reveal() => location;
    public override string ToString() => "ProtectedLocation [redacted]";
}
public sealed class ProtectedLocationConverter : JsonConverter<ProtectedLocation>
{
    public override ProtectedLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new JsonException("Protected Location cannot be restored from diagnostic JSON.");
    public override void Write(Utf8JsonWriter writer, ProtectedLocation value, JsonSerializerOptions options)
    { writer.WriteStartObject(); writer.WriteBoolean("canReveal", true); writer.WriteString("value", "[redacted]"); writer.WriteEndObject(); }
}

// Parsing for diagnostics is deliberately separate from acceptance. A malformed
// authority must not hide a private path, or turn authority/query data into a UUID.
internal static class LocationMetadata
{
    private static readonly Regex Scheme = new(@"\A[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Characters = new(@"\A[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]+\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex InvalidEscape = new(@"%(?![0-9A-Fa-f]{2})|%(?:0[aAdD]|00)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex JoinedReferences = new(@",(?:[A-Za-z][A-Za-z0-9+.-]*:)?//", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex HostLabel = new(@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PathCharacters = new(@"\A[A-Za-z0-9\-._~!$&'()*+,;=:@%/]*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TailCharacters = new(@"\A[A-Za-z0-9\-._~!$&'()*+,;=:@%/?]*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PathUuid = new(@"(?<![0-9a-fA-F])[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}(?![0-9a-fA-F])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static bool TryValidate(string value, out string normalized)
    {
        normalized = value.Trim(' ', '\t');
        if (value.Length > 65536 || value.Any(c => c < 32 && c != '\t' || c == 127) || !Characters.IsMatch(normalized) || InvalidEscape.IsMatch(normalized) || normalized.StartsWith("///", StringComparison.Ordinal) || JoinedReferences.IsMatch(normalized)) return false;
        var scheme = Scheme.Match(normalized);
        var network = normalized.StartsWith("//", StringComparison.Ordinal);
        var (path, query, fragment, authority) = Components(normalized);
        if (!PathCharacters.IsMatch(path) || !TailCharacters.IsMatch(query) || !TailCharacters.IsMatch(fragment)) return false;
        // Uri's relative well-formedness check treats a rooted reference with a
        // fragment as a file URI on some runtimes; enforce URI-reference grammar.
        if (!scheme.Success && !network) return !path.Split('/')[0].Contains(':');
        if (scheme.Success && !normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(network ? "https:" + normalized : normalized, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString() || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Host.Length == 0) return false;
        if (authority.Contains('@') || authority.EndsWith(':')) return false;
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6) return true;
        var host = uri.Host.EndsWith('.') ? uri.Host[..^1] : uri.Host;
        return uri.HostNameType == UriHostNameType.Dns && host.Split('.').All(label => HostLabel.IsMatch(label));
    }

    private static (string Path, string Query, string Fragment, string Authority) Components(string value)
    {
        var reference = value.Trim(' ', '\t');
        var hash = reference.IndexOf('#');
        var fragment = hash < 0 ? "" : reference[(hash + 1)..];
        if (hash >= 0) reference = reference[..hash];
        var question = reference.IndexOf('?');
        var query = question < 0 ? "" : reference[(question + 1)..];
        if (question >= 0) reference = reference[..question];
        var scheme = Scheme.Match(reference);
        if (scheme.Success) reference = reference[scheme.Length..];
        var authority = "";
        if (reference.StartsWith("//", StringComparison.Ordinal))
        {
            var slash = reference.IndexOf('/', 2);
            authority = slash < 0 ? reference[2..] : reference[2..slash];
            reference = slash < 0 ? "" : reference[slash..];
        }
        return (reference, query, fragment, authority);
    }

    internal static IEnumerable<string> DiagnosticValues(string value)
    {
        var (path, query, fragment, authority) = Components(value);
        var values = new List<string> { value, value.Trim(' ', '\t'), path, query, fragment, authority };
        values.AddRange(path.Split('/'));
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            var userinfo = authority[..at]; values.Add(userinfo);
            values.AddRange(userinfo.Split(':', 2));
        }
        foreach (var item in query.Split('&'))
        {
            values.Add(item);
            var equals = item.IndexOf('=');
            if (equals >= 0)
            {
                var token = item[(equals + 1)..]; values.Add(token);
                values.Add(Uri.UnescapeDataString(token.Replace('+', ' ')));
            }
        }
        // Diagnostic observation is intentionally broader than navigation acceptance:
        // decode delimiters and split query, fragment and matrix assignments once more.
        foreach (var form in new[] { value, Uri.UnescapeDataString(value) })
            foreach (var item in form.Split(['?', '#', ';', '&']))
            {
                var decoded = Uri.UnescapeDataString(item.Replace('+', ' '));
                var eq = decoded.IndexOf('=');
                if (eq >= 0) { values.Add(decoded[(eq + 1)..]); values.Add(item[(item.IndexOf('=') + 1)..]); }
            }
        return values.Concat(values.Select(Uri.UnescapeDataString)).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static IEnumerable<string> PathUuids(string value) => PathUuid.Matches(Uri.UnescapeDataString(Components(value).Path)).Select(m => m.Value);
}
