Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Webmania

Module Program
    Sub Main()
        RunAsync().GetAwaiter().GetResult()
    End Sub
    Private Async Function CheckAsync(Of T)(items As IAsyncEnumerable(Of T), valid As Func(Of T, Boolean)) As Task
        Dim iterator = items.GetAsyncEnumerator(CancellationToken.None)
        Dim count = 0
        While Await iterator.MoveNextAsync()
            If Not valid(iterator.Current) Then Throw New Exception("Typed DTO mismatch.")
            count += 1
        End While
        Await iterator.DisposeAsync()
        If count <> 1 Then Throw New Exception("Expected one typed pagination item.")
    End Function
    Private Async Function InvalidAsync(Of T)(items As IAsyncEnumerable(Of T)) As Task
        Dim iterator = items.GetAsyncEnumerator(CancellationToken.None)
        Dim caught = False
        Try
            Dim moved = Await iterator.MoveNextAsync()
        Catch errorResult As WebmaniaException When errorResult.Kind = "validation"
            caught = True
        End Try
        Await iterator.DisposeAsync()
        If Not caught Then Throw New Exception("Expected validation before network.")
    End Function
    Private Async Function RunAsync() As Task
        Dim options = New WebmaniaClientOptions With {.LoopbackTestEndpoint = New Uri(Environment.GetEnvironmentVariable("WEBMANIA_TEST_ENDPOINT"))}
        Using client = New WebmaniaClient(WebmaniaCredentials.Monitor(Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")), options)
            Dim page = [Optional](Of JsonNumber).FromValue(New JsonNumber("2147483648"))
            Dim perPage = [Optional](Of JsonNumber).FromValue(New JsonNumber("10"))
            Dim uuid = "ABCDEFAB-CDEF-4ABC-8DEF-ABCDEFABCDEF"
            Dim companies = New MonitorListCompaniesParameters With {.Page = page, .PerPage = perPage}
            Dim municipalities = New MonitorListCompanyMunicipalitiesParameters With {.Company = [Optional](Of String).FromValue(uuid), .Page = page, .PerPage = perPage}
            Dim reports = New MonitorListReportsParameters With {.Page = page, .PerPage = perPage}
            Await CheckAsync(client.IterateMonitorCompaniesAsync(companies, 1), Function(item As Api2MonitorReadsMonitorCompany) item.Uuid.Value = uuid)
            Await CheckAsync(client.IterateMonitorCompanyMunicipalitiesAsync(municipalities, 1), Function(item As Api2MonitorReadsMonitorMunicipalityRegistration) item.Uuid.Value = uuid)
            Await CheckAsync(client.IterateMonitorReportsAsync(reports, 1), Function(item As Api2MonitorDocumentsMonitorReport) item.Uuid.Value = uuid)
            Await InvalidAsync(client.IterateMonitorCompaniesAsync(New MonitorListCompaniesParameters With {.Page = [Optional](Of JsonNumber).Null}, 1))
            Await InvalidAsync(client.IterateMonitorCompanyMunicipalitiesAsync(New MonitorListCompanyMunicipalitiesParameters With {.Company = [Optional](Of String).FromValue(uuid), .PerPage = [Optional](Of JsonNumber).Null}, 1))
            Await InvalidAsync(client.IterateMonitorReportsAsync(New MonitorListReportsParameters With {.Page = [Optional](Of JsonNumber).Null}, 1))
        End Using
        Console.WriteLine("VB.NET exact typed pagination and null validation passed.")
    End Function
End Module
