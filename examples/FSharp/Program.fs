open System
open System.Threading
open Webmania

[<EntryPoint>]
let main _ =
    let run = task {
        let token =
            match Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN") |> Option.ofObj with
            | Some value when not (String.IsNullOrWhiteSpace value) -> value
            | _ -> invalidOp "Set WEBMANIA_MONITOR_TOKEN with a Monitor token."
        let options = WebmaniaClientOptions()
        match Environment.GetEnvironmentVariable("WEBMANIA_API_BASE_URL") |> Option.ofObj with
        | Some endpoint when not (String.IsNullOrWhiteSpace endpoint) -> options.ServiceEndpoint <- Uri(endpoint)
        | _ -> ()
        match Environment.GetEnvironmentVariable("WEBMANIA_TEST_ENDPOINT") |> Option.ofObj with
        | Some endpoint when not (String.IsNullOrWhiteSpace endpoint) -> options.LoopbackTestEndpoint <- Uri(endpoint)
        | _ -> ()
        use client = new WebmaniaClient(WebmaniaCredentials.Monitor(token), options)
        use cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15.0))
        let parameters = MonitorListCompaniesParameters()
        parameters.Page <- Optional<JsonNumber>.Of(JsonNumber("1"))
        parameters.PerPage <- Optional<JsonNumber>.Of(JsonNumber("10"))
        try
            let! response = client.MonitorListCompaniesAsync(parameters, cancellation.Token)
            printfn "F# received HTTP %d." response.StatusCode
            return 0
        with
        | :? WebmaniaException as error ->
            eprintfn "%s: %s, status %O. %s %s" error.OperationId error.Kind error.StatusCode error.Message error.RecoveryAdvice
            return 1
    }
    run.GetAwaiter().GetResult()
