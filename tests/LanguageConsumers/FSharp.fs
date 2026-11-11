open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Webmania

let check (items: IAsyncEnumerable<'T>) (valid: 'T -> bool) = task {
    use iterator = items.GetAsyncEnumerator(CancellationToken.None)
    let mutable count = 0
    let mutable running = true
    while running do
        let! moved = iterator.MoveNextAsync()
        if moved then
            if not (valid iterator.Current) then invalidOp "Typed DTO mismatch."
            count <- count + 1
        else running <- false
    if count <> 1 then invalidOp "Expected one typed pagination item."
}
let invalid (items: IAsyncEnumerable<'T>) = task {
    use iterator = items.GetAsyncEnumerator(CancellationToken.None)
    let mutable caught = false
    try
        let! _ = iterator.MoveNextAsync()
        ()
    with :? WebmaniaException as error when error.Kind = "validation" -> caught <- true
    if not caught then invalidOp "Expected validation before network."
}
[<EntryPoint>]
let main _ =
    let run = task {
        let required name = Environment.GetEnvironmentVariable(name) |> Option.ofObj |> Option.defaultWith (fun () -> invalidOp "Missing controlled-test environment.")
        let options = WebmaniaClientOptions(LoopbackTestEndpoint = Uri(required "WEBMANIA_TEST_ENDPOINT"))
        use client = new WebmaniaClient(WebmaniaCredentials.Monitor(required "WEBMANIA_MONITOR_TOKEN"), options)
        let page = Optional<JsonNumber>.FromValue(JsonNumber("2147483648"))
        let perPage = Optional<JsonNumber>.FromValue(JsonNumber("10"))
        let uuid = "ABCDEFAB-CDEF-4ABC-8DEF-ABCDEFABCDEF"
        let companies = MonitorListCompaniesParameters(Page = page, PerPage = perPage)
        let municipalities = MonitorListCompanyMunicipalitiesParameters(Company = Optional<string>.FromValue(uuid), Page = page, PerPage = perPage)
        let reports = MonitorListReportsParameters(Page = page, PerPage = perPage)
        do! check (client.IterateMonitorCompaniesAsync(companies, 1)) (fun item -> item.Uuid.Value = uuid)
        do! check (client.IterateMonitorCompanyMunicipalitiesAsync(municipalities, 1)) (fun item -> item.Uuid.Value = uuid)
        do! check (client.IterateMonitorReportsAsync(reports, 1)) (fun item -> item.Uuid.Value = uuid)
        do! invalid (client.IterateMonitorCompaniesAsync(MonitorListCompaniesParameters(Page = Optional<JsonNumber>.Null), 1))
        do! invalid (client.IterateMonitorCompanyMunicipalitiesAsync(MonitorListCompanyMunicipalitiesParameters(Company = Optional<string>.FromValue(uuid), PerPage = Optional<JsonNumber>.Null), 1))
        do! invalid (client.IterateMonitorReportsAsync(MonitorListReportsParameters(Page = Optional<JsonNumber>.Null), 1))
        printfn "F# exact typed pagination and null validation passed."
    }
    run.GetAwaiter().GetResult()
    0
