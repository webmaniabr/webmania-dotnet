using Webmania;

// Required: WEBMANIA_MONITOR_TOKEN. Optional for a controlled local test: WEBMANIA_TEST_ENDPOINT.
var token = Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")
    ?? throw new InvalidOperationException("Set WEBMANIA_MONITOR_TOKEN with a Monitor token.");
var options = new WebmaniaClientOptions();
if (Environment.GetEnvironmentVariable("WEBMANIA_API_BASE_URL") is { } serviceEndpoint && !string.IsNullOrWhiteSpace(serviceEndpoint))
    options.ServiceEndpoint = new Uri(serviceEndpoint);
if (Environment.GetEnvironmentVariable("WEBMANIA_TEST_ENDPOINT") is { } endpoint)
    options.LoopbackTestEndpoint = new Uri(endpoint);
using var client = new WebmaniaClient(WebmaniaCredentials.Monitor(token), options);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    var response = await client.MonitorListCompaniesAsync(new MonitorListCompaniesParameters
    {
        Page = new JsonNumber("1"), PerPage = new JsonNumber("10")
    }, cancellation.Token);
    Console.WriteLine($"C# received HTTP {response.StatusCode}; {response.Data?.Data.Value?.Count ?? 0} company records.");
}
catch (WebmaniaException error)
{
    Console.Error.WriteLine($"{error.OperationId}: {error.Kind}, status {error.StatusCode}. {error.Message} {error.RecoveryAdvice}");
    Environment.ExitCode = 1;
}
