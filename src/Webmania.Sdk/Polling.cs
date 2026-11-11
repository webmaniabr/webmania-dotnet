using System.Runtime.CompilerServices;
using System.Numerics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
namespace Webmania;
public sealed class PollOptions
{
    public int MaximumAttempts { get; set; } = 12;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan Deadline { get; set; } = TimeSpan.FromMinutes(1);
}
public sealed partial class WebmaniaClient
{
    /// <summary>Polls only the documented report request, frozen once; never retries an API error or follows its URL.</summary>
    public async Task<ApiResponse<FiscalCreateOrPollReportResult>> PollFiscalReportAsync(FiscalCreateOrPollReportParameters parameters, PollOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var maximum = options.MaximumAttempts; var interval = options.Interval; var deadlineValue = options.Deadline;
        if (maximum is < 1 or > 10000 || interval < TimeSpan.FromSeconds(5) || interval.TotalMilliseconds > int.MaxValue || deadlineValue <= TimeSpan.Zero || deadlineValue.TotalMilliseconds > int.MaxValue) throw WebmaniaException.Local("fiscalCreateOrPollReport", "validation", "Polling requires a positive attempt/deadline limit and an interval of at least 5 seconds.");
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        JsonElement frozen;
        try { frozen = WebmaniaJson.ToElement(parameters); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { throw WebmaniaException.Local("fiscalCreateOrPollReport", "validation", "Report serialization failed."); }
        Contracts.Validate(Contracts.Operations["fiscalCreateOrPollReport"], frozen);
        var reference = ProtectedReference.Diagnostic(Redactor.Fingerprint(frozen));
        var remaining = deadlineValue - System.Diagnostics.Stopwatch.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero) throw WebmaniaException.Local("fiscalCreateOrPollReport", "timeout", "Report preparation exceeded the polling deadline.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(remaining);
        var attempted = false;
        try
        {
            for (var attempt = 0; attempt < maximum; attempt++)
            {
                if (attempt > 0) await Task.Delay(interval, deadline.Token).ConfigureAwait(false);
                var result = await ExecuteAsync<FiscalCreateOrPollReportResult>("fiscalCreateOrPollReport", frozen, deadline.Token).ConfigureAwait(false);
                attempted = true;
                var state = result.Json.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                if (state == "concluido") return result;
                if (state != "processando") throw new WebmaniaException("fiscalCreateOrPollReport", "protocol", "Report returned an unrecognized processing state. Inspect it with an explicit consultation.", result.StatusCode, true, reference);
            }
            throw new WebmaniaException("fiscalCreateOrPollReport", "poll_limit", "Report attempt limit reached.", uncertain: attempted, reference: reference);
        }
        catch (WebmaniaException e) when (e.Kind == "cancelled" && !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new WebmaniaException("fiscalCreateOrPollReport", "timeout", "Report polling deadline exceeded.", uncertain: attempted, reference: reference); }
        catch (OperationCanceledException e)
        {
            var diagnostic = (e as WebmaniaCancellationException)?.Diagnostic;
            var uncertain = attempted || diagnostic?.OutcomeUncertain == true;
            if (cancellationToken.IsCancellationRequested) throw new WebmaniaCancellationException(new WebmaniaException("fiscalCreateOrPollReport", "cancelled", "Report polling stopped.", diagnostic?.StatusCode, uncertain, diagnostic?.SafeReference ?? reference, diagnostic?.CorrelationId, diagnostic?.Details), cancellationToken);
            throw new WebmaniaException("fiscalCreateOrPollReport", "timeout", "Report polling deadline exceeded.", diagnostic?.StatusCode, uncertain, diagnostic?.SafeReference ?? reference);
        }
    }

    /// <summary>Waits on an existing import UUID only. invalido and concluido are typed terminal states, including declared erro.</summary>
    public async Task<ApiResponse<Api2MonitorDocumentsMonitorImportStatus>> WaitMonitorImportAsync(MonitorGetImportStatusParameters parameters, PollOptions? options = null, CancellationToken cancellationToken = default)
    {
        const string id = "monitorGetImportStatus";
        options ??= new(); var maximum = options.MaximumAttempts; var interval = options.Interval; var limit = options.Deadline;
        if (maximum is < 1 or > 10000 || interval < TimeSpan.FromSeconds(5) || interval.TotalMilliseconds > int.MaxValue || limit <= TimeSpan.Zero || limit.TotalMilliseconds > int.MaxValue) throw WebmaniaException.Local(id, "validation", "Polling requires bounded attempts/deadline and an interval of at least 5 seconds.");
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        JsonElement frozen;
        try { frozen = WebmaniaJson.ToElement(parameters); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { throw WebmaniaException.Local(id, "validation", "Import lookup serialization failed."); }
        Contracts.Validate(Contracts.Operations[id], frozen);
        var remaining = limit - System.Diagnostics.Stopwatch.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero) throw WebmaniaException.Local(id, "timeout", "Import lookup preparation exceeded the deadline.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(remaining);
        try
        {
            for (var attempt = 0; attempt < maximum; attempt++)
            {
                if (attempt > 0) await Task.Delay(interval, deadline.Token).ConfigureAwait(false);
                var result = await ExecuteAsync<Api2MonitorDocumentsMonitorImportStatus>(id, frozen, deadline.Token).ConfigureAwait(false);
                var state = result.Json.GetProperty("status").GetString();
                if (state is "concluido" or "invalido") return result;
                if (state != "processando") throw new WebmaniaException(id, "protocol", "Import returned an unknown processing state.", result.StatusCode);
            }
            throw WebmaniaException.Local(id, "poll_limit", "Import attempt limit reached.");
        }
        catch (OperationCanceledException e)
        {
            if (cancellationToken.IsCancellationRequested) throw new WebmaniaCancellationException(e is WebmaniaCancellationException typed ? typed.Diagnostic : WebmaniaException.Local(id, "cancelled", "Import waiting stopped."), cancellationToken);
            throw WebmaniaException.Local(id, "timeout", "Import waiting deadline exceeded.");
        }
    }

    /// <summary>Iterates bounded company pages. Explicit initial page JSON is preserved; omitted page stays omitted in the first request.</summary>
    public IAsyncEnumerable<Api2MonitorReadsMonitorCompany> IterateMonitorCompaniesAsync(MonitorListCompaniesParameters parameters, int maximumPages = 100, CancellationToken cancellationToken = default) =>
        IteratePagesAsync<Api2MonitorReadsMonitorCompanyPage, Api2MonitorReadsMonitorCompany>("monitorListCompanies", parameters, page => page.Data.Value!, maximumPages, cancellationToken);

    public IAsyncEnumerable<Api2MonitorReadsMonitorMunicipalityRegistration> IterateMonitorCompanyMunicipalitiesAsync(MonitorListCompanyMunicipalitiesParameters parameters, int maximumPages = 100, CancellationToken cancellationToken = default) =>
        IteratePagesAsync<Api2MonitorReadsMonitorMunicipalityPage, Api2MonitorReadsMonitorMunicipalityRegistration>("monitorListCompanyMunicipalities", parameters, page => page.Data.Value!, maximumPages, cancellationToken);

    public IAsyncEnumerable<Api2MonitorDocumentsMonitorReport> IterateMonitorReportsAsync(MonitorListReportsParameters parameters, int maximumPages = 100, CancellationToken cancellationToken = default) =>
        IteratePagesAsync<Api2MonitorDocumentsMonitorReportPage, Api2MonitorDocumentsMonitorReport>("monitorListReports", parameters, page => page.Data.Value!, maximumPages, cancellationToken);

    private async IAsyncEnumerable<T> IteratePagesAsync<TPage, T>(string operationId, object parameters, Func<TPage, IEnumerable<T>> items, int maximumPages, [EnumeratorCancellation] CancellationToken cancellationToken) where TPage : class
    {
        if (maximumPages is < 1 or > 10000) throw WebmaniaException.Local(operationId, "validation", "A positive bounded page limit is required.");
        JsonElement frozen;
        try { frozen = WebmaniaJson.ToElement(parameters); }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or TargetInvocationException or FormatException or OverflowException)
        { throw WebmaniaException.Local(operationId, "validation", "Pagination request serialization failed. Check omitted/null fields and extension collisions."); }
        // Validate the caller's snapshot before applying defaults or deriving pagination state.
        Contracts.Validate(Contracts.Operations[operationId], frozen);
        var page = BigInteger.One;
        if (frozen.TryGetProperty("page", out var requested) && !TryPage(requested, out page))
            throw WebmaniaException.Local(operationId, "validation", "Pagination helper supports positive exact integer pages of at most 4096 expanded decimal digits. No request was sent.");
        BigInteger? expectedPerPage = null;
        if (frozen.TryGetProperty("per_page", out var requestedPerPage))
        {
            if (!TryPage(requestedPerPage, out var size)) throw WebmaniaException.Local(operationId, "validation", "Pagination per_page must be a supported positive exact integer. No request was sent.");
            expectedPerPage = size;
        }
        for (var count = 0; count < maximumPages; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = frozen.EnumerateObject().ToDictionary(x => x.Name, x => x.Value);
            // Preserve the explicit first page byte representation and the difference between omission and null.
            if (count > 0) input["page"] = WebmaniaJson.ToElement(new JsonNumber(page.ToString(CultureInfo.InvariantCulture)));
            var response = await ExecuteAsync<TPage>(operationId, input, cancellationToken).ConfigureAwait(false);
            var meta = response.Json.GetProperty("meta");
            if (!meta.TryGetProperty("current_page", out var current) || !TryPage(current, out var actual) || actual != page || !meta.TryGetProperty("last_page", out var last) || !TryPage(last, out var end))
                throw new WebmaniaException(operationId, "protocol", "Pagination metadata did not advance as requested or exceeded the helper's exact integer range.", response.StatusCode);
            if (!meta.TryGetProperty("per_page", out var perPage) || !TryPage(perPage, out var actualPerPage) || expectedPerPage.HasValue && actualPerPage != expectedPerPage.Value)
                throw new WebmaniaException(operationId, "protocol", "Pagination per_page changed from the explicit or initially observed page size.", response.StatusCode);
            expectedPerPage ??= actualPerPage;
            var itemCount = response.Json.GetProperty("data").GetArrayLength();
            if (itemCount > actualPerPage || end < page && itemCount != 0)
                throw new WebmaniaException(operationId, "protocol", "Pagination data is inconsistent with its page size or last page.", response.StatusCode);
            // Laravel-style pagination returns an empty page for a requested page beyond last_page.
            if (end < page) yield break;
            foreach (var item in items(response.Data!))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            if (page == end) yield break;
            page += BigInteger.One;
        }
        throw WebmaniaException.Local(operationId, "page_limit", "Pagination page limit reached.");
    }
    private static bool TryPage(JsonElement value, out BigInteger page)
    {
        page = default;
        return value.ValueKind == JsonValueKind.Number && ExactDecimal.TryParse(value.GetRawText(), out var exact) && exact.TryToInteger(4096, out page) && page >= BigInteger.One;
    }
}
