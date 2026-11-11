// Exemplo neutro de homologação (ambiente 2) com o SDK .NET.
// Lê a configuração somente de variáveis WEBMANIA_*, imprime uma linha JSON mascarada por passo
// e nunca repete uma mutação. Sem WEBMANIA_LIVE=1 não faz nenhuma requisição (saída 77). Os escopos
// nfe, nfse e all exigem WEBMANIA_LIVE_LEDGER gravável; sem ele também sai com 77 antes de qualquer requisição.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webmania;

const int Blocked = 77;
const int PollAttempts = 24;
var pollInterval = TimeSpan.FromSeconds(5);
var failed = 0;

static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
static string? Ref(string? value) => string.IsNullOrEmpty(value) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];
static int Block(string? reason)
{
    Console.WriteLine(reason is null ? "{\"result\":\"blocked\"}" : JsonSerializer.Serialize(new { result = "blocked", reason }));
    return Blocked;
}
void Emit(string step, string? operation, bool ok, int? status = null, string? outcome = null, string? kind = null, string? reference = null, object? file = null)
{
    if (!ok) failed++;
    var line = new JsonObject { ["step"] = step, ["operation"] = operation, ["ok"] = ok, ["http_status"] = status, ["outcome"] = outcome, ["kind"] = kind, ["ref"] = reference };
    if (file is not null) line["file"] = JsonSerializer.SerializeToNode(file);
    Console.WriteLine(line.ToJsonString());
}
void Ledger(string model, string? identifier, string status, string operation)
{
    if (Env("WEBMANIA_LIVE_LEDGER") is not { } path || string.IsNullOrEmpty(identifier)) return;
    File.AppendAllText(path, JsonSerializer.Serialize(new { model, identifier, status, operation }) + "\n");
}
static object FileSummary(FileResult file)
{
    if (file.RedirectLocation is not null) return new { redirect = true };
    var bytes = file.Bytes.Span;
    var format = bytes.StartsWith("%PDF-"u8) ? "pdf" : bytes.StartsWith("PK"u8) ? "zip" : Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 64)]).TrimStart('\uFEFF', ' ', '\t', '\r', '\n').StartsWith('<') ? "xml" : "other";
    return new { size = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), format };
}
static JsonElement Fields(JsonElement json) => json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0 ? json[0] : json;
static string Text(JsonElement json, string name) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
static P Params<P>(object value) where P : class => WebmaniaJson.FromElement<P>(JsonSerializer.SerializeToElement(value)) ?? throw new InvalidOperationException("Invalid parameters.");

// Runs one step; a sent mutation whose outcome is unknown stops the whole run.
async Task<T?> Call<T>(string step, string operation, Func<Task<T>> action, string? reference = null, bool mutation = false) where T : class
{
    try
    {
        var result = await action();
        if (result is FileResult file) Emit(step, operation, file.IsFile || file.RedirectLocation is not null, file.StatusCode, reference: Ref(reference), kind: file.IsFile || file.RedirectLocation is not null ? null : file.Representation.ToString(), file: FileSummary(file));
        else Emit(step, operation, true, 200, reference: Ref(reference));
        return result;
    }
    catch (WebmaniaException error)
    {
        Emit(step, operation, false, error.StatusCode, error.OutcomeUncertain ? "unknown" : null, error.Kind, Ref(reference));
        if (mutation && error.OutcomeUncertain) throw new UnknownOutcome();
        return null;
    }
}
async Task<JsonElement> Poll<T>(string step, string operation, Func<Task<ApiResponse<T>>> fetch, Func<string, bool> done, string reference)
{
    var last = default(JsonElement);
    for (var attempt = 0; attempt < PollAttempts; attempt++)
    {
        if (await Call(step, operation, fetch, reference) is { } response)
        {
            last = Fields(response.Json);
            if (done(Text(last, "status"))) return last;
        }
        if (attempt < PollAttempts - 1) await Task.Delay(pollInterval);
    }
    return last;
}
static JsonNode? Payload(string name)
{
    if (Env(name) is not { } path || !File.Exists(path)) return null;
    var node = JsonNode.Parse(File.ReadAllText(path));
    return node is JsonObject obj && obj["ambiente"] is JsonValue v && v.TryGetValue<int>(out var ambiente) && ambiente == 2 ? node : null;
}
WebmaniaClientOptions Options(string url)
{
    var uri = new Uri(url);
    // Plain HTTP is accepted only for a loopback test server (origin only).
    return uri.Scheme == "http" && uri.IsLoopback ? new() { LoopbackTestEndpoint = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/") } : new() { ServiceEndpoint = uri };
}

if (Env("WEBMANIA_LIVE") != "1") return Block(null);
var api1 = Env("WEBMANIA_API1_BASE_URL") ?? "https://webmania.com.br/api";
var filesBase = Env("WEBMANIA_FILES_BASE_URL") ?? "https://nfe.webmania.com.br";
var api2 = Env("WEBMANIA_API2_BASE_URL") ?? "https://api.webmania.com.br";
var scope = Env("WEBMANIA_LIVE_SCOPE") ?? "readonly";
if (scope is not ("readonly" or "nfe" or "nfse" or "all")) return Block("scope");
// The allow list is required: without it no origin is trusted (fail closed, before any credential).
var allowed = (Env("WEBMANIA_LIVE_ALLOWED_HOSTS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(h => h.ToLowerInvariant()).ToHashSet();
if (allowed.Count == 0) return Block("allowed_hosts");
if (new[] { api1, filesBase, api2 }.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var x) || !allowed.Contains(x.Host.ToLowerInvariant()))) return Block("origin_not_allowed");
string[] names = ["WEBMANIA_CONSUMER_KEY", "WEBMANIA_CONSUMER_SECRET", "WEBMANIA_ACCESS_TOKEN", "WEBMANIA_ACCESS_TOKEN_SECRET", "WEBMANIA_ISSUER_TOKEN"];
if (names.Any(n => Env(n) is null)) return Block("credentials");
var nfeDocument = scope is "nfe" or "all" ? Payload("WEBMANIA_LIVE_NFE_PAYLOAD") : null;
var nfseDocument = scope is "nfse" or "all" ? Payload("WEBMANIA_LIVE_NFSE_PAYLOAD") : null;
if ((scope is "nfe" or "all" && nfeDocument is null) || (scope is "nfse" or "all" && nfseDocument is null)) return Block("payload");
if (nfeDocument is not null || nfseDocument is not null)
{
    // An unknown outcome is recorded only in the ledger: it must be writable before any mutation.
    if (Env("WEBMANIA_LIVE_LEDGER") is not { } ledgerPath) return Block("ledger");
    var ledgerOptions = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows()) ledgerOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    try { using var probe = new FileStream(ledgerPath, ledgerOptions); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Block("ledger"); }
}
var (key, secret, access, accessSecret, issuerToken) = (Env(names[0])!, Env(names[1])!, Env(names[2])!, Env(names[3])!, Env(names[4])!);

try
{
    using var nfe = new WebmaniaClient(WebmaniaCredentials.Company(key, secret, access, accessSecret), Options(api1));
    using var emitter = new WebmaniaClient(WebmaniaCredentials.Issuer(issuerToken), Options(api2));
    using var nfeFiles = new WebmaniaClient(WebmaniaCredentials.Api1FilesCompany(key, secret, access, accessSecret), Options(filesBase));
    using var emitterFiles = new WebmaniaClient(WebmaniaCredentials.Api2FilesCompanyBearer(issuerToken), Options(api2));
    await Call("readonly.sefaz", "nfeGetSefazStatus", () => nfe.NfeGetSefazStatusAsync(new()));
    await Call("readonly.certificate", "nfeGetCertificateValidity", () => nfe.NfeGetCertificateValidityAsync(new()));
    await Call("readonly.tax_classes", "nfeListTaxClasses", () => nfe.NfeListTaxClassesAsync(new()));
    await Call("readonly.nfse_provider", "emitterGetNfseProviderStatus", () => emitter.EmitterGetNfseProviderStatusAsync(new()));

    if (nfeDocument is not null)
    {
        var id = nfeDocument["ID"]?.ToString();
        var issuedJson = default(JsonElement);
        try { if (await Call("nfe.issue", "nfeIssueDocument", () => nfe.NfeIssueDocumentAsync(Params<NfeIssueDocumentParameters>(new { body = nfeDocument })), id, true) is { } r) issuedJson = Fields(r.Json); }
        catch (UnknownOutcome) { Ledger("nfe", id ?? "unidentified", "unknown", "nfeIssueDocument"); throw; }
        var uuid = Text(issuedJson, "uuid");
        if (uuid.Length > 0)
        {
            Ledger("nfe", uuid, Text(issuedJson, "status") is { Length: > 0 } issuedStatus ? issuedStatus : "processando", "nfeIssueDocument");
            Task<JsonElement> Consult(string step, Func<string, bool> done) => Poll(step, "nfeGetDocument", () => nfe.NfeGetDocumentAsync(Params<NfeGetDocumentParameters>(new { uuid })), done, uuid);
            var current = await Consult("nfe.consult", value => value is "aprovado" or "reprovado" or "denegado" or "cancelado");
            if (Text(current, "status") != "aprovado") Ledger("nfe", uuid, Text(current, "status") is { Length: > 0 } st ? st : "unknown", "nfeGetDocument");
            else
            {
                var cancelled = false;
                try
                {
                    await Call("nfe.query", "nfeQueryDocument", () => nfe.NfeQueryDocumentAsync(Params<NfeQueryDocumentParameters>(new { body = new { uuid } })), uuid);
                    await Call("nfe.danfe", "nfeDownloadDanfe", () => nfeFiles.NfeDownloadDanfeAsync(Params<NfeDownloadDanfeParameters>(new { identifier = uuid })), uuid);
                    await Call("nfe.danfe_simple", "nfeDownloadSimpleDanfe", () => nfeFiles.NfeDownloadSimpleDanfeAsync(Params<NfeDownloadSimpleDanfeParameters>(new { identifier = uuid })), uuid);
                    await Call("nfe.xml", "nfeDownloadXml", () => nfeFiles.NfeDownloadXmlAsync(Params<NfeDownloadXmlParameters>(new { identifier = uuid })), uuid);
                }
                finally
                {
                    // An approved document is always cancelled, even after a failed step above.
                    try { cancelled = await Call("nfe.cancel", "nfeCancelDocument", () => nfe.NfeCancelDocumentAsync(Params<NfeCancelDocumentParameters>(new { body = new { uuid, motivo = "Cancelamento de teste em homologacao." } })), uuid, true) is not null; }
                    catch (UnknownOutcome) { Ledger("nfe", uuid, "unknown", "nfeCancelDocument"); throw; }
                }
                if (!cancelled) Ledger("nfe", uuid, "aprovado", "nfeCancelDocument");
                else
                {
                    var final = await Consult("nfe.consult_cancelled", value => value == "cancelado");
                    var status = Text(final, "status") is { Length: > 0 } fs ? fs : "unknown";
                    Ledger("nfe", uuid, status, "nfeCancelDocument");
                    // Only a consult that shows the cancellation counts as success.
                    if (status != "cancelado") Emit("nfe.consult_cancelled", "nfeGetDocument", false, kind: "not_cancelled", reference: Ref(uuid));
                    else await Call("nfe.cancellation_xml", "nfeDownloadCancellationXml", () => nfeFiles.NfeDownloadCancellationXmlAsync(Params<NfeDownloadCancellationXmlParameters>(new { identifier = uuid })), uuid);
                }
            }
        }
    }

    if (nfseDocument is not null)
    {
        var id = nfseDocument["ID"]?.ToString();
        var json = default(JsonElement);
        try { if (await Call("nfse.issue", "emitterIssueNfse", () => emitter.EmitterIssueNfseAsync(Params<EmitterIssueNfseParameters>(new { body = nfseDocument })), id, true) is { } r) json = Fields(r.Json); }
        catch (UnknownOutcome) { Ledger("nfse", id ?? "unidentified", "unknown", "emitterIssueNfse"); throw; }
        var uuid = Text(json, "uuid");
        if (uuid.Length == 0 && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("info_nfse", out var info) && info.ValueKind == JsonValueKind.Array && info.GetArrayLength() > 0) uuid = Text(info[0], "uuid");
        if (uuid.Length > 0)
        {
            Ledger("nfse", uuid, Text(json, "status") is { Length: > 0 } issuedStatus ? issuedStatus : "processando", "emitterIssueNfse");
            Task<JsonElement> Consult(string step, Func<string, bool> done) => Poll(step, "emitterGetNfse", () => emitter.EmitterGetNfseAsync(Params<EmitterGetNfseParameters>(new { identifier = uuid })), done, uuid);
            var current = await Consult("nfse.consult", value => value is not ("" or "processando" or "pendente"));
            if (Text(current, "status") != "aprovado") Ledger("nfse", uuid, Text(current, "status") is { Length: > 0 } st ? st : "unknown", "emitterGetNfse");
            else
            {
                var cancelled = false;
                try
                {
                    await Call("nfse.xml", "fileGetNfseXml", () => emitterFiles.FileGetNfseXmlAsync(Params<FileGetNfseXmlParameters>(new { identifier = uuid })), uuid);
                    if (Text(current, "pdf_nfse").Length > 0) await Call("nfse.pdf", "fileGetNfsePdf", () => emitterFiles.FileGetNfsePdfAsync(Params<FileGetNfsePdfParameters>(new { identifier = uuid })), uuid);
                    else Emit("nfse.pdf", "fileGetNfsePdf", true, kind: "not_offered", reference: Ref(uuid));
                    if (Text(current, "pdf_rps").Length > 0) await Call("nfse.rps_pdf", "fileGetNfseRpsPdf", () => emitterFiles.FileGetNfseRpsPdfAsync(Params<FileGetNfseRpsPdfParameters>(new { identifier = uuid })), uuid);
                    else Emit("nfse.rps_pdf", "fileGetNfseRpsPdf", true, kind: "not_offered", reference: Ref(uuid));
                }
                finally
                {
                    try { cancelled = await Call("nfse.cancel", "emitterCancelNfse", () => emitter.EmitterCancelNfseAsync(Params<EmitterCancelNfseParameters>(new { body = new { uuid, motivo = "1", justificativa = "Cancelamento de teste em homologacao." } })), uuid, true) is not null; }
                    catch (UnknownOutcome) { Ledger("nfse", uuid, "unknown", "emitterCancelNfse"); throw; }
                }
                if (!cancelled) Ledger("nfse", uuid, "aprovado", "emitterCancelNfse");
                else
                {
                    var final = await Consult("nfse.consult_cancelled", value => value == "cancelado");
                    var status = Text(final, "status") is { Length: > 0 } fs ? fs : "unknown";
                    Ledger("nfse", uuid, status, "emitterCancelNfse");
                    if (status != "cancelado") Emit("nfse.consult_cancelled", "emitterGetNfse", false, kind: "not_cancelled", reference: Ref(uuid));
                }
            }
        }
    }
}
catch (UnknownOutcome) { return 1; }
catch (Exception error) when (error is ArgumentException or UriFormatException) { Emit("configuration", null, false, kind: "configuration"); return 1; }
return failed > 0 ? 1 : 0;

sealed class UnknownOutcome : Exception;
