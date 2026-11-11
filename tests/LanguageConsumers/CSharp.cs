using Webmania;

var options = new WebmaniaClientOptions { LoopbackTestEndpoint = new Uri(Environment.GetEnvironmentVariable("WEBMANIA_TEST_ENDPOINT")!) };
using var client = new WebmaniaClient(WebmaniaCredentials.Monitor(Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")!), options);
var page = new JsonNumber("2147483648");
var perPage = new JsonNumber("10");
const string uuid = "ABCDEFAB-CDEF-4ABC-8DEF-ABCDEFABCDEF";
static async Task Check<T>(IAsyncEnumerable<T> items, Func<T, bool> valid)
{
    var count = 0;
    await foreach (var item in items) { if (!valid(item)) throw new Exception("Typed DTO mismatch."); count++; }
    if (count != 1) throw new Exception("Expected one typed pagination item.");
}
static async Task Invalid<T>(IAsyncEnumerable<T> items)
{
    try { await foreach (var _ in items) { } }
    catch (WebmaniaException error) when (error.Kind == "validation") { return; }
    throw new Exception("Expected validation before network.");
}
await Check(client.IterateMonitorCompaniesAsync(new() { Page = page, PerPage = perPage }, 1), company => company.Uuid.Value == uuid);
await Check(client.IterateMonitorCompanyMunicipalitiesAsync(new() { Company = uuid, Page = page, PerPage = perPage }, 1), municipality => municipality.Uuid.Value == uuid);
await Check(client.IterateMonitorReportsAsync(new() { Page = page, PerPage = perPage }, 1), report => report.Uuid.Value == uuid);
await Invalid(client.IterateMonitorCompaniesAsync(new() { Page = Optional<JsonNumber>.Null }, 1));
await Invalid(client.IterateMonitorCompanyMunicipalitiesAsync(new() { Company = uuid, PerPage = Optional<JsonNumber>.Null }, 1));
await Invalid(client.IterateMonitorReportsAsync(new() { Page = Optional<JsonNumber>.Null }, 1));
Console.WriteLine("C# exact typed pagination and null validation passed.");
