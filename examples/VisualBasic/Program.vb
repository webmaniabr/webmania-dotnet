Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports Webmania

Module Program
    Sub Main()
        Environment.ExitCode = RunAsync().GetAwaiter().GetResult()
    End Sub

    Private Async Function RunAsync() As Task(Of Integer)
        Dim token = Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")
        If String.IsNullOrWhiteSpace(token) Then Throw New InvalidOperationException("Set WEBMANIA_MONITOR_TOKEN with a Monitor token.")
        Dim options = New WebmaniaClientOptions()
        Dim serviceEndpoint = Environment.GetEnvironmentVariable("WEBMANIA_API_BASE_URL")
        If Not String.IsNullOrWhiteSpace(serviceEndpoint) Then options.ServiceEndpoint = New Uri(serviceEndpoint)
        Dim endpoint = Environment.GetEnvironmentVariable("WEBMANIA_TEST_ENDPOINT")
        If Not String.IsNullOrWhiteSpace(endpoint) Then options.LoopbackTestEndpoint = New Uri(endpoint)
        Using client = New WebmaniaClient(WebmaniaCredentials.Monitor(token), options)
            Using cancellation = New CancellationTokenSource(TimeSpan.FromSeconds(15))
                Dim parameters = New MonitorListCompaniesParameters With {
                    .Page = [Optional](Of JsonNumber).FromValue(New JsonNumber("1")),
                    .PerPage = [Optional](Of JsonNumber).FromValue(New JsonNumber("10"))
                }
                Try
                    Dim response = Await client.MonitorListCompaniesAsync(parameters, cancellation.Token)
                    Console.WriteLine($"VB.NET received HTTP {response.StatusCode}.")
                    Return 0
                Catch errorResult As WebmaniaException
                    Console.Error.WriteLine($"{errorResult.OperationId}: {errorResult.Kind}, status {errorResult.StatusCode}. {errorResult.Message} {errorResult.RecoveryAdvice}")
                    Return 1
                End Try
            End Using
        End Using
    End Function
End Module
