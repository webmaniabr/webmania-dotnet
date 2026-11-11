using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Authentication;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Webmania;

/// <summary>One immutable credential scope per client. Values are never shown by ToString.</summary>
public sealed class WebmaniaCredentials
{
    internal string Scope { get; }
    internal IReadOnlyDictionary<string, string> Headers { get; }
    internal string? QueryToken { get; }
    internal string FileMode { get; }
    private WebmaniaCredentials(string scope, Dictionary<string, string> headers, string? token = null, string mode = "")
    {
        if (headers.Values.Any(string.IsNullOrWhiteSpace) || mode == "token" && string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Credential value is required.");
        foreach (var value in headers.Values.Append(token).OfType<string>())
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n') || !UnicodeScalars.IsWellFormed(value)) throw new ArgumentException("Credential value is missing, contains a line break or invalid Unicode.");
        Scope = scope; Headers = headers; QueryToken = token; FileMode = mode;
    }
    private static string Bearer(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Bearer token is required.") : "Bearer " + value;
    private static Dictionary<string, string> Four(string key, string secret, string token, string tokenSecret) => new() { ["X-Consumer-Key"] = key, ["X-Consumer-Secret"] = secret, ["X-Access-Token"] = token, ["X-Access-Token-Secret"] = tokenSecret };
    public static WebmaniaCredentials Company(string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret) => new("company", Four(consumerKey, consumerSecret, accessToken, accessTokenSecret));
    public static WebmaniaCredentials Issuer(string bearer) => new("issuer", new() { ["Authorization"] = Bearer(bearer) });
    public static WebmaniaCredentials Monitor(string bearer) => new("monitor", new() { ["Authorization"] = Bearer(bearer) });
    public static WebmaniaCredentials Reseller(string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret) => new("reseller", Four(consumerKey, consumerSecret, accessToken, accessTokenSecret));
    public static WebmaniaCredentials Api1FilesCompany(string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret) => new("api1-files", Four(consumerKey, consumerSecret, accessToken, accessTokenSecret), mode: "company");
    public static WebmaniaCredentials Api1ReportCapability() => new("report-file", new(), mode: "capability");
    /// <summary>Report URL capability for current report exports (fiscalDownloadReportV2); no credential header is sent.</summary>
    public static WebmaniaCredentials ReportCapability() => new("report-file", new(), mode: "capability");
    public static WebmaniaCredentials Api1FilesDocumentToken(string token) => new("api1-files", new(), token, "token");
    public static WebmaniaCredentials Api2FilesCompanyBearer(string bearer) => new("api2-files", new() { ["Authorization"] = Bearer(bearer) }, mode: "company");
    public static WebmaniaCredentials Api2FilesDocumentToken(string token) => new("api2-files", new(), token, "token");
    public static WebmaniaCredentials Api2FilesAnonymous() => new("api2-files", new(), mode: "anonymous");
    public override string ToString() => $"WebmaniaCredentials({Scope}) [redacted]";
}
public sealed class WebmaniaClientOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public long MaximumResponseBytes { get; set; } = 16 * 1024 * 1024;
    public long MaximumRequestBytes { get; set; } = 8 * 1024 * 1024;
    /// <summary>Explicit controlled-test endpoint. Only numeric loopback or localhost is accepted.</summary>
    /// <summary>Explicitly approved HTTPS base for this scoped client. Its path is a prefix for the pinned routes.</summary>
    public Uri? ServiceEndpoint { get; set; }
    public Uri? LoopbackTestEndpoint { get; set; }
    /// <summary>Optional private CA for a loopback TLS test. Hostname validation remains mandatory.</summary>
    public X509Certificate2? LoopbackTestRootCertificate { get; set; }
    /// <summary>Absolute path of a .NET 10 host (dotnet) used to run the killable name-resolver process.
    /// Defaults to the host of the running shared runtime; self-contained apps must set it.</summary>
    public string? DnsHelperHost { get; set; }
}
public sealed class ApiResponse<T>
{
    public string OperationId { get; }
    public int StatusCode { get; }
    public T? Data { get; }
    public string? CorrelationId { get; }
    public JsonElement Json { get; }
    internal ApiResponse(string op, int status, T? data, JsonElement json, string? correlation) { OperationId = op; StatusCode = status; Data = data; Json = json; CorrelationId = correlation; }
    public override string ToString() => $"ApiResponse({OperationId}, {StatusCode}) [redacted]";
}
public enum FileReadiness { NotFile, Ready, Protected, Unsupported }
public enum FileRepresentation { Pdf, Xml, Zip, Csv, Spreadsheet, Html, Json, Redirect, Other }
public sealed class FileResult
{
    public string OperationId { get; }
    public int StatusCode { get; }
    public string MediaType { get; }
    public string? CorrelationId { get; }
    public FileRepresentation Representation { get; }
    public ProtectedLocation? RedirectLocation { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public JsonElement? Json { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    /// <summary>HTML and JSON are separate representations; HTTP 200 is not proof that a file is ready.</summary>
    public bool IsBinaryFile => Readiness == FileReadiness.Ready && Representation is FileRepresentation.Pdf or FileRepresentation.Xml or FileRepresentation.Zip or FileRepresentation.Spreadsheet;
    public FileReadiness Readiness { get; }
    public bool IsFile => Readiness == FileReadiness.Ready && (IsBinaryFile || Representation == FileRepresentation.Csv);
    internal FileResult(string op, int status, string media, byte[] bytes, JsonElement? json, IReadOnlyDictionary<string, string> headers, string? correlation, ProtectedLocation? redirectLocation, FileReadiness readiness)
    {
        Readiness = readiness; Headers = headers; CorrelationId = correlation; RedirectLocation = redirectLocation;
        OperationId = op; StatusCode = status; MediaType = media; Bytes = bytes; Json = json;
        Representation = redirectLocation is not null ? FileRepresentation.Redirect : media switch { "text/csv" => FileRepresentation.Csv, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => FileRepresentation.Spreadsheet, "application/pdf" => FileRepresentation.Pdf, "application/zip" or "application/x-zip-compressed" => FileRepresentation.Zip, "text/xml" or "application/xml" => FileRepresentation.Xml, "text/html" => FileRepresentation.Html, "application/json" => FileRepresentation.Json, _ => FileRepresentation.Other };
    }
    public override string ToString() => $"FileResult({OperationId}, {StatusCode}, {Representation}, {Bytes.Length} bytes) [redacted]";
}

/// <summary>Long-lived, isolated HttpClient; no automatic redirect, retry, cookies or authentication fallback.</summary>
public sealed partial class WebmaniaClient : IDisposable
{
    private readonly WebmaniaCredentials credentials;
    private readonly HttpClient http;
    private readonly SemaphoreSlim transferGate = new(1, 1);
    private TransferContext? activeTransfer;
    private readonly TimeSpan timeout;
    private readonly long maxResponse, maxRequest;
    private readonly Uri? testEndpoint;
    private readonly Uri? serviceEndpoint;
    private readonly X509Certificate2? testRoot;
    private readonly ConcurrentDictionary<string, bool> capabilities = new(StringComparer.Ordinal);
    // Remembered capabilities are bounded. Beyond the bound nothing is forgotten: the client switches
    // to strict diagnostics, which redact every UUID, URL and long opaque token in later errors.
    private const int MaximumRememberedCapabilities = 4096;
    private volatile bool strictDiagnostics;
    private void Remember(string value, bool report)
    {
        if (capabilities.Count >= MaximumRememberedCapabilities && !capabilities.ContainsKey(value)) { strictDiagnostics = true; return; }
        if (report) capabilities.AddOrUpdate(value, true, (_, _) => true); else capabilities.TryAdd(value, false);
    }
    public static string Version => typeof(WebmaniaClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0-preview.6";
    public static IReadOnlyList<OperationInfo> Operations { get; } = Array.AsReadOnly(Contracts.Operations.Values.Select(x => new OperationInfo(x.Id, x.Method, x.Route, x.Scope, x.Enabled, x.Module, x.Status)).ToArray());
    public WebmaniaClient(WebmaniaCredentials credentials, WebmaniaClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credentials); this.credentials = credentials;
        options ??= new();
        if (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > int.MaxValue || options.MaximumResponseBytes is <= 0 or > int.MaxValue || options.MaximumRequestBytes is <= 0 or > int.MaxValue) throw new ArgumentException("Invalid deadline or byte limit.");
        timeout = options.Timeout; maxResponse = options.MaximumResponseBytes; maxRequest = options.MaximumRequestBytes;
        testEndpoint = options.LoopbackTestEndpoint;
        serviceEndpoint = options.ServiceEndpoint;
        if (testEndpoint is not null && serviceEndpoint is not null) throw new ArgumentException("Choose one explicit service endpoint or loopback test endpoint.");
        if (serviceEndpoint is not null) ValidateServiceEndpoint(serviceEndpoint);
        if (testEndpoint is not null && !UnicodeScalars.IsWellFormed(testEndpoint.OriginalString)) throw new ArgumentException("Test endpoint contains invalid Unicode.");
        if (testEndpoint is not null && (!testEndpoint.IsAbsoluteUri || !testEndpoint.IsLoopback || testEndpoint.Scheme is not ("http" or "https") || testEndpoint.UserInfo.Length > 0 || testEndpoint.Query.Length > 0 || testEndpoint.Fragment.Length > 0 || testEndpoint.AbsolutePath != "/")) throw new ArgumentException("Test endpoint must be a loopback HTTP(S) origin.");
        if (options.LoopbackTestRootCertificate is not null && ((testEndpoint ?? serviceEndpoint) is not { IsLoopback: true, Scheme: "https" })) throw new ArgumentException("A test root certificate requires a loopback HTTPS endpoint.");
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, Credentials = null, PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = timeout, AutomaticDecompression = DecompressionMethods.None, ActivityHeadersPropagator = null, MaxResponseDrainSize = 0, UseProxy = false, MaxConnectionsPerServer = 1 };
        RemoteCertificateValidationCallback? validate = null;
        if (options.LoopbackTestRootCertificate is not null)
        {
            var root = X509CertificateLoader.LoadCertificate(options.LoopbackTestRootCertificate.RawData); testRoot = root;
            validate = (_, certificate, _, errors) =>
            {
                if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None) return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                using var cert = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                return chain.Build(cert);
            };
        }
        var helperHost = options.DnsHelperHost;
        if (helperHost is not null && (!Path.IsPathFullyQualified(helperHost) || !File.Exists(helperHost))) throw new ArgumentException("DnsHelperHost must be the absolute path of an existing .NET host.");
        handler.ConnectCallback = (context, token) => OwnedConnection.Connect(context, token, validate, helperHost);
        handler.PlaintextStreamFilter = (context, _) => new ValueTask<Stream>(new ObservedHttpStream(context.PlaintextStream, () => activeTransfer));
        http = new(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }
    private static void ValidateServiceEndpoint(Uri endpoint)
    {
        var original = endpoint.OriginalString;
        if (!UnicodeScalars.IsWellFormed(original)) throw new ArgumentException("ServiceEndpoint contains invalid Unicode.");
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "https" || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0 || original.Contains('?') || original.Contains('#') || original.Contains('\\') || original.Any(char.IsControl) || original.Length > 4096)
            throw new ArgumentException("ServiceEndpoint requires an explicit HTTPS base without userinfo, query, fragment or control characters.");
        var start = original.IndexOf('/', original.IndexOf("://", StringComparison.Ordinal) + 3);
        var path = start >= 0 ? original[start..] : "/";
        foreach (var segment in path.Split('/'))
        {
            var decoded = Uri.UnescapeDataString(segment);
            if (decoded is "." or ".." || decoded.IndexOfAny(['/', '\\', '?', '#']) >= 0 || decoded.Any(char.IsControl)) throw new ArgumentException("ServiceEndpoint prefix must not contain dot segments or escaped separators.");
        }
    }
    public void Dispose() { http.Dispose(); testRoot?.Dispose(); }
    private async Task<ApiResponse<T>> ExecuteAsync<T>(string id, object parameters, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = await SendAsync(id, parameters, cancellationToken).ConfigureAwait(false);
        if (result.Json is null) throw new WebmaniaException(id, "protocol", "Expected a JSON response.", result.Status, result.Mutation, result.Reference);
        try
        {
            var data = WebmaniaJson.FromElement<T>(result.Json.Value);
            if (cancellationToken.IsCancellationRequested) throw new WebmaniaCancellationException(new WebmaniaException(id, "cancelled", "Operation cancelled.", result.Status, result.Mutation, result.Reference), cancellationToken);
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= timeout) throw new WebmaniaException(id, "timeout", "Total operation deadline exceeded.", result.Status, result.Mutation, result.Reference);
            return new(id, result.Status, data, result.Json.Value, result.CorrelationId);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or TargetInvocationException)
        { throw new WebmaniaException(id, "protocol", "Response does not match the selected JSON representation.", result.Status, result.Mutation, result.Reference); }
    }
    private async Task<FileResult> ExecuteFileAsync(string id, object parameters, CancellationToken cancellationToken)
    {
        var result = await SendAsync(id, parameters, cancellationToken).ConfigureAwait(false);
        return new(id, result.Status, result.Media, result.Bytes, result.Json, result.Headers, result.CorrelationId, result.RedirectLocation, result.Readiness);
    }
    private sealed record Transfer(int Status, string Media, byte[] Bytes, JsonElement? Json, bool Mutation, ProtectedReference Reference, IReadOnlyDictionary<string, string> Headers, string? CorrelationId, ProtectedLocation? RedirectLocation, FileReadiness Readiness = FileReadiness.NotFile);
    private async Task<Transfer> SendAsync(string id, object parameters, CancellationToken cancellationToken)
    {
        var op = Contracts.Operations[id];
        if (!op.Enabled) throw WebmaniaException.Local(id, "capability_unavailable", "This operation is not available in this SDK release; no request was sent.");
        if (op.Scope != credentials.Scope) throw WebmaniaException.Local(id, "credential_scope", "Credential scope does not match this operation.");
        if (op.Scope == "api2-files" && credentials.FileMode != "company" && !op.Security.EnumerateArray().Any(s => credentials.FileMode == "anonymous" ? !s.EnumerateObject().Any() : s.TryGetProperty("FiscalDocumentLinkToken", out _))) throw WebmaniaException.Local(id, "credential_scope", "This file operation requires explicit company Bearer credentials.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        var redactor = new Redactor { Strict = strictDiagnostics };
        var selectorRedactor = new Redactor { Strict = strictDiagnostics };
        JsonElement input;
        byte[]? bodyBytes = null;
        try
        {
            foreach (var header in credentials.Headers.Values)
            {
                redactor.Add(header); selectorRedactor.Add(header);
                if (header.StartsWith("Bearer ", StringComparison.Ordinal)) { redactor.Add(header[7..]); selectorRedactor.Add(header[7..]); }
            }
            redactor.Add(credentials.QueryToken); selectorRedactor.Add(credentials.QueryToken);
            foreach (var cap in capabilities) { redactor.AddCapability(cap.Key, cap.Value); selectorRedactor.AddCapability(cap.Key, cap.Value); }
            input = WebmaniaJson.ToElement(parameters);
            redactor.AddRequestJson(input, op.Schema, Contracts.Module(op.Module));
            selectorRedactor.AddJson(input, false);
            if (id is "fiscalDownloadReport" or "fiscalDownloadReportV2" && input.TryGetProperty("uuid", out var report)) { redactor.AddCapability(report.GetString(), true); selectorRedactor.AddCapability(report.GetString(), true); Remember(report.GetString()!, true); }
            Contracts.Validate(op, input, token);
            if (input.TryGetProperty("body", out var body)) bodyBytes = Encoding.UTF8.GetBytes(body.GetRawText());
        }
        catch (WebmaniaException) { throw; }
        catch (OperationCanceledException) { if (cancellationToken.IsCancellationRequested) throw new WebmaniaCancellationException(new WebmaniaException(id, "cancelled", "Request preparation stopped before network dispatch."), cancellationToken); throw new WebmaniaException(id, "timeout", "Request preparation stopped before network dispatch."); }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or TargetInvocationException or FormatException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { throw WebmaniaException.Local(id, "validation", "Request serialization failed. Check omitted/null fields and extension collisions."); }
        if (bodyBytes?.LongLength > maxRequest) throw WebmaniaException.Local(id, "request_limit", "Serialized request exceeds the configured byte limit.");
        var mutation = IsMutation(op, input);
        var reference = ProtectedReference.Diagnostic(Redactor.Fingerprint(input));
        int? status = null;
        var context = new TransferContext(token); var acquired = false;
        void ObserveMetadata()
        {
            string[] observed; bool unsafeObservation;
            lock (context) { observed = context.Locations.ToArray(); unsafeObservation = context.Observed && (!context.ObservationComplete || context.UnsafeObservation); }
            foreach (var value in observed) { redactor.AddLocation(value); selectorRedactor.AddLocation(value); }
            reference = unsafeObservation ? ProtectedReference.Diagnostic(reference.Fingerprint) : Reference(op, input, selectorRedactor, reference.Fingerprint);
        }
        try
        {
        reference = Reference(op, input, selectorRedactor, reference.Fingerprint);
        var route = op.Route; var query = new List<KeyValuePair<string, string>>();
        foreach (var p in op.Parameters.EnumerateArray())
        {
            var name = p.GetProperty("name").GetString()!;
            if (!input.TryGetProperty(name, out var value)) continue;
            var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (p.GetProperty("in").GetString() == "path") route = route.Replace("{" + name + "}", Uri.EscapeDataString(text), StringComparison.Ordinal);
            else query.Add(new(name, text));
        }
        if (credentials.QueryToken is not null) query.Add(new("token", credentials.QueryToken));
        var baseUri = testEndpoint?.GetLeftPart(UriPartial.Authority) ?? (serviceEndpoint is null ? op.Base : serviceEndpoint.GetLeftPart(UriPartial.Authority) + serviceEndpoint.AbsolutePath.TrimEnd('/'));
        var uri = new Uri(baseUri.TrimEnd('/') + route + (query.Count > 0 ? "?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))) : ""));
        redactor.Add(uri.AbsoluteUri);
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(new HttpMethod(op.Method), uri) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.ExpectContinue = false;
            request.Headers.UserAgent.ParseAdd("Webmania.Sdk/0.1.0-preview.6");
            request.Headers.Accept.ParseAdd(op.Binary ? "*/*" : "application/json");
            foreach (var header in credentials.Headers) request.Headers.Add(header.Key, header.Value);
            // An explicit zero-byte content prevents SocketsHttpHandler from replaying a bodyless request after response EOF.
            // No fiscal body is invented; the wire carries Content-Length: 0. Verify this behavior on every supported runtime.
            request.Content = new ByteArrayContent(bodyBytes ?? []);
            if (bodyBytes is not null) request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Options.Set(TransferContext.Key, context);
            await transferGate.WaitAsync(token).ConfigureAwait(false); acquired = true; activeTransfer = context;
            token.ThrowIfCancellationRequested();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            var locations = response.Headers.NonValidated.TryGetValues("Location", out var locationFields) ? locationFields.ToArray() : [];
            foreach (var value in locations) { redactor.AddLocation(value); selectorRedactor.AddLocation(value); }
            ObserveMetadata();
            var rawCorrelation = response.Headers.TryGetValues("X-Request-Id", out var rid) ? rid.FirstOrDefault() : null;
            var correlation = rawCorrelation is null || !context.ObservationComplete || context.UnsafeObservation ? null : redactor.Clean(rawCorrelation);
            string? location = null;
            if (status is >= 300 and < 400 && locations.Length > 0)
            {
                if (locations.Length != 1 || !LocationMetadata.TryValidate(locations[0], out location)) throw new WebmaniaException(id, "protocol", "Invalid or ambiguous HTTP redirect Location.", status, mutation, reference, correlation);
            }
            if (op.Binary && op.RedirectStatuses.Contains(status.Value))
            {
                if (location is null) throw new WebmaniaException(id, "protocol", "Documented file redirect omitted Location.", status, mutation, reference, correlation);
                RememberCapabilities(WebmaniaJson.ToElement(location), false);
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (response.Content.Headers.ContentType is { } type) metadata["Content-Type"] = redactor.Clean(type.ToString());
                return new(status.Value, response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "application/octet-stream", [], null, mutation, reference, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(metadata), correlation, new ProtectedLocation(location));
            }
            if (status is >= 300 and < 400) throw new WebmaniaException(id, "redirect", "Redirect refused. Reauthorize any new origin explicitly.", status, mutation, reference, correlation);
            if (response.Content.Headers.ContentLength > maxResponse) throw new WebmaniaException(id, "response_limit", "Response exceeds the configured byte limit.", status, mutation && status is not (>= 400 and < 500), reference, correlation);
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var chunk = new byte[8192];
            while (true)
            {
                var count = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
                if (count == 0) break;
                if (buffer.Length + count > maxResponse) throw new WebmaniaException(id, "response_limit", "Response exceeds the configured byte limit.", status, mutation && status is not (>= 400 and < 500), reference, correlation);
                buffer.Write(chunk, 0, count);
            }
            ObserveMetadata();
            correlation = rawCorrelation is null ? null : redactor.Clean(rawCorrelation);
            var bytes = buffer.ToArray();
            var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "application/octet-stream";
            JsonElement? json = null;
            var looksJson = media == "application/json" || media.EndsWith("+json", StringComparison.Ordinal) || bytes.AsSpan().TrimStart((byte)' ').StartsWith("{"u8) || bytes.AsSpan().TrimStart((byte)' ').StartsWith("["u8);
            if (looksJson)
            {
                try { UnicodeScalars.ValidateJson(bytes, token); using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 96 }); json = doc.RootElement.Clone(); }
                catch (JsonException) { throw new WebmaniaException(id, "protocol", "Malformed JSON response.", status, mutation && status is not (>= 400 and < 500), reference, correlation); }
                var reportResponse = id is "fiscalCreateOrPollReport" or "monitorListReports" || id == "monitorSearchDocuments" && mutation;
                RememberCapabilities(json.Value, reportResponse);
                redactor.AddJson(json.Value, false, reportResponse);
                selectorRedactor.AddJson(json.Value, false, reportResponse);
                correlation = rawCorrelation is null ? null : redactor.Clean(rawCorrelation);
                reference = Reference(op, input, selectorRedactor, reference.Fingerprint);
            }
            var selected = op.ResponseSelections.TryGetValue(status.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), out var representations) || op.ResponseSelections.TryGetValue($"{status.Value / 100}XX", out representations);
            JsonElement? selectedSchema = null;
            var declared = selected && representations!.TryGetValue(media, out selectedSchema);
            var validImport = id == "monitorGetImportStatus" && status is >= 200 and < 300 && declared && json.HasValue && selectedSchema.HasValue && Validator.Error(selectedSchema.Value, json.Value, Contracts.Module(op.Module), "response", response: true, cancellationToken: token) is null;
            var businessError = json.HasValue && HasBusinessError(json.Value, validImport);
            if (status is < 200 or >= 300 || businessError)
            {
                var details = json.HasValue ? ErrorDetails(json.Value, redactor) : [];
                var message = details.Count > 0 ? details[0].Message : "The API returned an unsuccessful result.";
                throw new WebmaniaException(id, businessError && status is >= 200 and < 300 ? "business" : "http", message, status, mutation && (status is >= 500 or >= 200 and < 300), reference, correlation, details);
            }
            token.ThrowIfCancellationRequested();
            if (!declared) throw new WebmaniaException(id, "protocol", "Response status and media are not a declared success representation.", status, mutation, reference, correlation);
            if (!op.Binary && json is not null && selectedSchema is { } responseSchema && Validator.Error(responseSchema, json.Value, Contracts.Module(op.Module), "response", response: true, cancellationToken: token) is not null) throw new WebmaniaException(id, "protocol", "Response does not match the documented success shape.", status, mutation, reference, correlation);
            if (!op.Binary && (json is null || media is "text/html")) throw new WebmaniaException(id, "protocol", "Expected JSON; received another representation.", status, mutation, reference, correlation);
            var readiness = FileReadiness.NotFile;
            if (op.Binary)
            {
                if (json is not null)
                { if (media != "application/json" || !op.ResponseMedia.Contains(media, StringComparer.OrdinalIgnoreCase)) throw new WebmaniaException(id, "protocol", "File content does not match the declared representation.", status, mutation, reference, correlation); }
                else
                {
                    // Legacy report downloads label CSV and XLSX as text/xml: classify those by their bytes.
                    if (id == "fiscalDownloadReport" && media == "text/xml") media = FileValidation.LegacyReportMedia(bytes);
                    try { readiness = FileValidation.Inspect(media, bytes, op.ResponseMedia, token); }
                    catch (InvalidDataException) { throw new WebmaniaException(id, "protocol", "File content does not match the declared media and bounded structural profile.", status, mutation, reference, correlation); }
                }
            }
            var safeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "Content-Type", "Content-Disposition", "Cache-Control", "ETag", "Last-Modified", "Content-Length" })
            {
                if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)) safeHeaders[name] = redactor.Clean(string.Join(", ", values));
            }
            token.ThrowIfCancellationRequested();
            return new(status.Value, media, bytes, json, mutation, reference, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(safeHeaders), correlation, null, readiness);
        }
        catch (WebmaniaException) { throw; }
        catch (OperationCanceledException)
        {
            ObserveMetadata();
            var diagnostic = new WebmaniaException(id, cancellationToken.IsCancellationRequested ? "cancelled" : "timeout", cancellationToken.IsCancellationRequested ? "Operation cancelled." : "Total operation deadline exceeded.", status, mutation && context.Sent && status is not (>= 400 and < 500), reference);
            if (cancellationToken.IsCancellationRequested) throw new WebmaniaCancellationException(diagnostic, cancellationToken);
            throw diagnostic;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or AuthenticationException or ObjectDisposedException or System.Text.RegularExpressions.RegexMatchTimeoutException or FormatException or OverflowException)
        { if (context.Observed && status is null) context.UnsafeObservation = true; ObserveMetadata(); throw new WebmaniaException(id, "transport", "The HTTP/TLS transfer failed.", status, mutation && context.Sent && status is not (>= 400 and < 500), reference); }
        finally
        {
            await context.ReapAsync().ConfigureAwait(false);
            if (acquired) { activeTransfer = null; transferGate.Release(); }
        }
    }
    private static bool IsMutation(ContractOperation op, JsonElement input)
    {
        if (op.Id is "fiscalDownloadReport" or "fiscalDownloadReportV2") return true; // Report downloads consume quota or record access.
        if (op.Id == "monitorSearchDocuments")
        {
            if (!input.TryGetProperty("body", out var body)) return false;
            return body.TryGetProperty("relatorio", out _) || body.TryGetProperty("filtros", out var filters) && filters.ValueKind == JsonValueKind.Object && filters.TryGetProperty("relatorio", out _);
        }
        if (op.Id == "monitorGetDocument") return input.TryGetProperty("atualizar_status", out var refresh) && (refresh.ValueKind == JsonValueKind.String ? refresh.GetString() : refresh.GetRawText()) == "1";
        if (op.Id == "nfeQueryDocument") return false;
        if (op.Effects.ValueKind == JsonValueKind.Object && op.Effects.TryGetProperty("business_mutation", out var effect)) return effect.ValueKind != JsonValueKind.False;
        return op.Method is not ("GET" or "HEAD");
    }
    private static bool HasBusinessError(JsonElement value, bool declaredImport = false)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(e => HasBusinessError(e));
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var key in new[] { "error", "errors", "erro" })
            if (value.TryGetProperty(key, out _) && !(declaredImport && key == "erro")) return true;
        return value.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False;
    }
    private static List<ApiErrorDetail> ErrorDetails(JsonElement value, Redactor redactor)
    {
        var result = new List<ApiErrorDetail>();
        void Walk(JsonElement e, int depth)
        {
            if (depth > 12 || result.Count >= 32) return;
            if (e.ValueKind == JsonValueKind.String) { result.Add(new(null, redactor.Clean(e.GetString()), null)); return; }
            if (e.ValueKind == JsonValueKind.Array) { foreach (var child in e.EnumerateArray()) Walk(child, depth + 1); return; }
            if (e.ValueKind != JsonValueKind.Object) return;
            var message = new[] { "message", "msg", "mensagem", "xMotivo", "motivo" }.Select(k => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null).FirstOrDefault(x => x is not null);
            if (message is not null)
            {
                string? Scalar(string k) => e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? redactor.Clean(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
                result.Add(new(Scalar("code") ?? Scalar("codigo"), redactor.Clean(message), Scalar("field") ?? Scalar("campo")));
            }
            foreach (var key in new[] { "error", "errors", "erro", "details", "data" }) if (e.TryGetProperty(key, out var nested)) Walk(nested, depth + 1);
        }
        Walk(value, 0); return result;
    }
    private static ProtectedReference Reference(ContractOperation operation, JsonElement input, Redactor redactor, string fingerprint)
    {
        if (operation.Binary || operation.Id.Contains("Report", StringComparison.Ordinal) || operation.Scope == "reseller") return ProtectedReference.Diagnostic(fingerprint);
        var sources = new List<JsonElement>();
        if (input.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object) sources.Add(body);
        sources.Add(input);
        foreach (var source in sources)
            foreach (var name in new[] { "uuid", "ID", "id", "chave", "identifier", "key", "fd", "company" })
                if (source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()) && !redactor.IsSensitive(value.GetString()!))
                    return ProtectedReference.Retained(name, value.GetString()!, fingerprint);
        return ProtectedReference.Diagnostic(fingerprint);
    }
    private void RememberCapabilities(JsonElement json, bool report, string? name = null)
    {
        if (json.ValueKind == JsonValueKind.Object) foreach (var p in json.EnumerateObject()) RememberCapabilities(p.Value, report, p.Name);
        else if (json.ValueKind == JsonValueKind.Array) foreach (var e in json.EnumerateArray()) RememberCapabilities(e, report, name);
        else if (json.ValueKind == JsonValueKind.String)
        {
            var s = json.GetString()!;
            if (report && name == "uuid" && Guid.TryParseExact(s, "D", out _)) Remember(s, true);
            else if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") Remember(s, Redactor.IsReportCapabilityUrl(uri));
        }
    }
}
