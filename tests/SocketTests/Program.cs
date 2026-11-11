using System.Collections.Concurrent;
using System.IO.Compression;
using System.Numerics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webmania;

static class Program
{
    const string U = "ABCDEFAB-CDEF-4ABC-8DEF-ABCDEFABCDEF";
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    static readonly Dictionary<string, JsonElement> Fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures.json"))).RootElement.EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!,x=>x.Clone());
    static readonly Dictionary<string, JsonElement> Ops = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"operations.json"))).RootElement.EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetString()!,x=>x.Clone());
    static int checks;
    static void Assert(bool condition, string message) { checks++; if(!condition) throw new Exception(message); }
    static WebmaniaCredentials Credentials(string scope, string fileMode="company") => scope switch
    {
        "company" => WebmaniaCredentials.Company("synthetic-key","synthetic-secret","synthetic-access","synthetic-access-secret"),
        "reseller" => WebmaniaCredentials.Reseller("synthetic-key","synthetic-secret","synthetic-access","synthetic-access-secret"),
        "issuer" => WebmaniaCredentials.Issuer("synthetic-issuer"),
        "monitor" => WebmaniaCredentials.Monitor("synthetic-monitor"),
        "report-file" => WebmaniaCredentials.Api1ReportCapability(),
        "api1-files" => fileMode=="token" ? WebmaniaCredentials.Api1FilesDocumentToken("synthetic-document+token/=") : WebmaniaCredentials.Api1FilesCompany("synthetic-key","synthetic-secret","synthetic-access","synthetic-access-secret"),
        "api2-files" => fileMode=="token" ? WebmaniaCredentials.Api2FilesDocumentToken("synthetic-document+token/=") : fileMode=="anonymous" ? WebmaniaCredentials.Api2FilesAnonymous() : WebmaniaCredentials.Api2FilesCompanyBearer("synthetic-files"),
        _ => throw new Exception("Unknown scope")
    };
    static object Input(string id, JsonElement? input = null) => JsonSerializer.Deserialize((input??Fixtures[id].GetProperty("input")).GetRawText(),typeof(WebmaniaClient).Assembly.GetType("Webmania."+Ops[id].GetProperty("parametersType").GetString())!,WebmaniaJson.Options)!;
    static async Task<object?> Call(WebmaniaClient c,string id,JsonElement? input=null,CancellationToken token=default)
    {
        var name=char.ToUpperInvariant(id[0])+id[1..]+"Async";
        Task task;
        try { task=(Task)typeof(WebmaniaClient).GetMethod(name)!.Invoke(c,[Input(id,input),token])!; }
        catch(TargetInvocationException e) { throw e.InnerException!; }
        await task;return task.GetType().GetProperty("Result")?.GetValue(task);
    }
    static async Task<WebmaniaException> Error(Func<Task> action,string kind)
    {
        try { await action(); } catch(WebmaniaCancellationException e) { Assert(kind=="cancelled"&&e.Kind==kind,"Typed native cancellation");Assert(e.InnerException is null,"No inner exception");return e.Diagnostic; } catch(WebmaniaException e) { Assert(e.Kind==kind,$"Expected {kind}, received {e.Kind}: {e.Message}");Assert(e.InnerException is null,"No inner exception");return e; }
        throw new Exception("Expected exception "+kind);
    }
    static JsonElement Element(string text) => JsonDocument.Parse(text).RootElement.Clone();
    static byte[] Pdf() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"canonical_plain.pdf"));
    static byte[] LegacyInvalidMarkerPdf() => "%PDF-1.7\n% synthetic fixture\n"u8.ToArray().Concat(new byte[]{0,255,13,10}).Concat("%%EOF\n"u8.ToArray()).ToArray();
    static byte[] Zip(bool spreadsheet=false)
    {
        if(spreadsheet)return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"canonical_xlsx.xlsx"));
        using var memory=new MemoryStream();
        using(var archive=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            foreach(var name in spreadsheet?new[]{"[Content_Types].xml","xl/workbook.xml"}:new[]{"synthetic.txt"})
            {using var writer=new StreamWriter(archive.CreateEntry(name).Open());writer.Write("synthetic");}
        }
        return memory.ToArray();
    }
    static Reply BinaryReply(JsonElement operation)
    {
        var media=operation.GetProperty("responseMedia").EnumerateArray().Select(x=>x.GetString()!).First(x=>x is not ("application/json" or "text/html"));
        return Reply.Bytes(media switch {"application/pdf"=>Pdf(),"application/xml" or "text/xml"=>"<?xml version=\"1.0\"?><synthetic/>"u8.ToArray(),"text/csv"=>"a,b\n1,2\n"u8.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"=>Zip(true),_=>Zip()},media);
    }
    static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--consumers") { await PackageConsumers(args); return; }

        SchemaCases();
        foreach(var tls in new[]{false,true})
        {
            await using var server=new SocketServer(tls);
            foreach(var pair in Ops)
            {
                var id=pair.Key;var op=pair.Value;var scope=op.GetProperty("scope").GetString()!;
                var modes=scope=="api1-files"?new[]{"company","token"}:scope=="api2-files"&& !op.GetProperty("route").GetString()!.Contains("mdfe")?new[]{"company","token","anonymous"}:new[]{"company"};
                foreach(var mode in modes)
                {
                    using var client=new WebmaniaClient(Credentials(scope,mode),server.Options());
                    var before=server.Requests.Count;
                    var fixture=Fixtures[id];Assert(fixture.GetProperty("requestValid").GetBoolean(),"Invalid request fixture "+id);Assert(fixture.GetProperty("responseValid").GetBoolean(),"Invalid response fixture "+id);
                    var binary=op.GetProperty("binary").GetBoolean();
                    server.Response = binary ? BinaryReply(op) : Reply.Json(fixture.GetProperty("response").GetRawText());
                    var successCode=op.GetProperty("responseSelections").EnumerateObject().Select(p=>int.TryParse(p.Name,out var code)?code:0).Where(code=>code>=200&&code<300).Min();
                    server.Response=server.Response with { Status=successCode };
                    var expectedFileBytes=server.Response.Body;
                    if(!op.GetProperty("enabled").GetBoolean())
                    { await Error(async()=>await Call(client,id),"capability_unavailable");Assert(server.Requests.Count==before,"Disabled operation made network request");continue; }
                    try { await Call(client,id); } catch(Exception e) { throw new Exception($"Operation {id}, TLS={tls}, mode={mode} failed: {e}"); }
                    Assert(server.Requests.Count==before+1,"One request per operation");var request=server.Requests.Last();
                    Assert(request.Method==op.GetProperty("method").GetString(),"Exact method "+id);
                    var input=fixture.GetProperty("input");var route=op.GetProperty("route").GetString()!;
                    foreach(var p in op.GetProperty("parameters").EnumerateArray())
                    {
                        var n=p.GetProperty("name").GetString()!;
                        if(p.GetProperty("in").GetString()=="path" && input.TryGetProperty(n,out var v))route=route.Replace("{"+n+"}",Uri.EscapeDataString(v.ValueKind==JsonValueKind.String?v.GetString()!:v.GetRawText()));
                    }
                    Assert(request.Target.Split('?')[0]==route,"Exact route "+id);
                    var queryPairs=request.Target.Contains('?')?request.Target.Split('?',2)[1].Split('&').ToDictionary(p=>Uri.UnescapeDataString(p.Split('=',2)[0]),p=>Uri.UnescapeDataString(p.Split('=',2)[1])):new Dictionary<string,string>();
                    foreach(var parameter in op.GetProperty("parameters").EnumerateArray())
                    {
                        var key=parameter.GetProperty("name").GetString()!;
                        if(parameter.GetProperty("in").GetString()=="query" && input.TryGetProperty(key,out var v)) Assert(queryPairs[key]==(v.ValueKind==JsonValueKind.String?v.GetString():v.GetRawText()),"Exact query "+id+" "+key);
                    }
                    if(input.TryGetProperty("body",out var expected))Assert(JsonElement.DeepEquals(expected,Element(request.Body)),"Serialized body preserved "+id);
                    else { Assert(request.Body.Length==0,"No invented body "+id);Assert(request.Headers["content-length"]=="0"&&!request.Headers.ContainsKey("content-type"),"Bodyless zero-byte wire framing "+id); }
                    var four=scope is "company" or "reseller" || scope=="api1-files"&&mode=="company";
                    foreach(var h in new[]{"x-consumer-key","x-consumer-secret","x-access-token","x-access-token-secret"})Assert(request.Headers.ContainsKey(h)==four,"Four-header scope "+id);
                    if(four)foreach(var h in new Dictionary<string,string>{{"x-consumer-key","synthetic-key"},{"x-consumer-secret","synthetic-secret"},{"x-access-token","synthetic-access"},{"x-access-token-secret","synthetic-access-secret"}})Assert(request.Headers[h.Key]==h.Value,"Exact company/reseller auth "+id);
                    var bearer=scope is "issuer" or "monitor" || scope=="api2-files"&&mode=="company";
                    Assert(request.Headers.ContainsKey("authorization")==bearer,"Bearer isolated "+id);
                    if(bearer)Assert(request.Headers["authorization"]=="Bearer synthetic-"+(scope=="api2-files"?"files":scope),"Correct Bearer "+id);
                    Assert(!request.Headers.ContainsKey("x-token"),"No X-Token substitution");
                    Assert(request.Target.Contains("token=synthetic-document%2Btoken%2F%3D")== (mode=="token"),"Token encoded once "+id);
                    if(binary) { var result=(FileResult)(await Call(client,id))!;Assert(result.Bytes.Span.SequenceEqual(expectedFileBytes),"Binary bytes intact"); }
                }
            }
            Console.WriteLine($"operation matrix {(tls?"TLS":"HTTP")} passed ({server.Requests.Count} real socket requests)");
        }
        await Regressions();
        await LocationDiagnostics(false);
        await LocationDiagnostics(true);
        Console.WriteLine(JsonSerializer.Serialize(new { status="pass",operations=Ops.Count,enabled=Ops.Values.Count(o=>o.GetProperty("enabled").GetBoolean()),checks,runtime=Environment.Version.ToString(),os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() }));
    }
    static void SchemaCases()
    {
        var cases=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"schema-cases.json"))).RootElement;
        var count=0;var roundtrips=0;
        foreach(var test in cases.EnumerateArray())
        {
            var module=test.GetProperty("module").GetString()!;var doc=Contracts.Module(module);
            var pointer=test.TryGetProperty("schema_pointer",out var p)?p.GetString()!:"#/components/schemas/"+test.GetProperty("schema").GetString();
            var schema=doc;foreach(var segment in pointer[2..].Split('/'))schema=schema.ValueKind==JsonValueKind.Array?schema[int.Parse(segment)]:schema.GetProperty(segment.Replace("~1","/").Replace("~0","~"));
            var instance=test.GetProperty("instance");var expected=test.GetProperty("valid").GetBoolean();var error=Validator.Error(schema,instance,doc,"schema-case");
            Assert((error is null)==expected,"Pinned synthetic schema case failed: "+test.GetProperty("source").GetString()+" "+(test.TryGetProperty("id",out var id)?id.GetString():test.GetProperty("name").GetString())+" "+error);count++;
            if(expected && test.TryGetProperty("schema",out var schemaName))
            {
                static string Ident(string text)=>string.Concat(System.Text.RegularExpressions.Regex.Replace(text,"[^A-Za-z0-9_]","_").Split('_',StringSplitOptions.RemoveEmptyEntries).Select(w=>char.ToUpperInvariant(w[0])+w[1..]));
                var prefix=Ident(module.Replace("contracts/","").Replace(".openapi.json","").Replace('/', '_').Replace('-', '_'));
                var type=typeof(WebmaniaClient).Assembly.GetType("Webmania."+prefix+Ident(schemaName.GetString()!));
                if(type is not null)
                {
                    var dto=JsonSerializer.Deserialize(instance.GetRawText(),type,WebmaniaJson.Options)!;
                    Assert(JsonElement.DeepEquals(instance,JsonSerializer.SerializeToElement(dto,type,WebmaniaJson.Options)),"Typed DTO roundtrip "+type.Name);roundtrips++;
                }
            }
        }
        Console.WriteLine($"{count} pinned schema cases and {roundtrips} typed DTO roundtrips passed");
    }
    static async Task PackageConsumers(string[] args)
    {
        if (args.Length != 4) throw new Exception("Arguments: --consumers dotnet repository feed");
        var dotnet=Path.GetFullPath(args[1]);var repository=Path.GetFullPath(args[2]);var feed=Path.GetFullPath(args[3]);
        var package=Directory.GetFiles(feed,"Webmania.Sdk.0.1.0-preview.6.nupkg").Single();
        var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(package))).ToLowerInvariant();
        var destination=Path.Combine(repository,"artifacts","consumers-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(destination);
        await using var server=new SocketServer(false);
        server.Response=Reply.Json(Fixtures["monitorListCompanies"].GetProperty("response").GetRawText());
        var sdkVersion=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(repository,"global.json")))!["sdk"]!["version"]!.ToString();
        var fsharpFeed=Path.Combine(Path.GetDirectoryName(dotnet)!,"sdk",sdkVersion,"FSharp","library-packs");
        if (!Directory.Exists(fsharpFeed)) fsharpFeed=Path.Combine("/usr/share/dotnet/sdk",sdkVersion,"FSharp/library-packs");
        Assert(Directory.Exists(fsharpFeed),"Bundled FSharp.Core feed found");
        var results=new List<object>();
        foreach(var language in new[]{"CSharp","FSharp","VisualBasic"})
        {
            var measured=System.Diagnostics.Stopwatch.StartNew();
            var source=Path.Combine(repository,"examples",language);var target=Path.Combine(destination,language);Directory.CreateDirectory(target);
            foreach(var file in Directory.GetFiles(source)) if(!file.EndsWith(".lock.json")) File.Copy(file,Path.Combine(target,Path.GetFileName(file)));
            var project=Directory.GetFiles(target,"*.*proj").Single();
            var config=Path.Combine(target,"NuGet.Config");
            var xml=new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("configuration",new System.Xml.Linq.XElement("packageSources",new System.Xml.Linq.XElement("clear"),new System.Xml.Linq.XElement("add",new System.Xml.Linq.XAttribute("key","candidate"),new System.Xml.Linq.XAttribute("value",feed)),new System.Xml.Linq.XElement("add",new System.Xml.Linq.XAttribute("key","sdk-fsharp"),new System.Xml.Linq.XAttribute("value",fsharpFeed)))));
            xml.Save(config);
            var cache=Path.Combine(destination,"packages");
            async Task<string> Run(params string[] arguments)
            {
                var psi=new System.Diagnostics.ProcessStartInfo(dotnet){WorkingDirectory=target,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var a in arguments)psi.ArgumentList.Add(a);
                psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";
                psi.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"]="false";
                psi.Environment.Remove("WEBMANIA_API_BASE_URL");
                psi.Environment["WEBMANIA_MONITOR_TOKEN"]="synthetic-monitor";
                psi.Environment["WEBMANIA_TEST_ENDPOINT"]=$"http://127.0.0.1:{server.Port}/";
                using var process=System.Diagnostics.Process.Start(psi)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();var output=(await stdout)+(await stderr);
                await File.AppendAllTextAsync(Path.Combine(destination,language+".log"),output);
                Assert(process.ExitCode==0,language+" process failed: "+output);return output;
            }
            await Run("restore",project,"--configfile",config,"--packages",cache,"--no-cache","--force");
            var assets=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target,"obj","project.assets.json"))).RootElement;
            var entry=assets.GetProperty("libraries").GetProperty("Webmania.Sdk/0.1.0-preview.6");Assert(entry.GetProperty("type").GetString()=="package","NuGet package, not project reference");
            Assert(!File.ReadAllText(project).Contains("ProjectReference"),"Clean consumer has no ProjectReference");
            var before=server.Requests.Count;var output=await Run("run","--project",project,"-c","Release","--no-restore");
            Assert(server.Requests.Count==before+1,"Consumer called SDK through a real socket");var request=server.Requests.Last();
            Assert(request.Method=="GET"&&request.Target=="/2/monitor/empresas?page=1&per_page=10","Consumer exact route");Assert(request.Headers["authorization"]=="Bearer synthetic-monitor","Consumer auth scope");
            var installed=Path.Combine(cache,"webmania.sdk","0.1.0-preview.6","webmania.sdk.0.1.0-preview.6.nupkg");Assert(File.Exists(installed),"Package restored from feed");
            Assert(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(installed))).ToLowerInvariant()==hash,"All consumers use identical nupkg");
            var consumerSource=Path.Combine(repository,"tests","LanguageConsumers",language+(language=="CSharp"?".cs":language=="FSharp"?".fs":".vb"));
            File.Copy(consumerSource,Path.Combine(target,language=="CSharp"?"Program.cs":language=="FSharp"?"Program.fs":"Program.vb"),true);
            server.DynamicReply=request=>
            {
                var route=request.Target.Split('?')[0];
                var operation=route.Contains("municipios-nfse")?"monitorListCompanyMunicipalities":route.Contains("relatorios")?"monitorListReports":"monitorListCompanies";
                return Reply.Json(PageResponse(operation,"2147483648","2147483648"));
            };
            await Run("build",project,"-c","Release","--no-restore","--no-incremental");
            var paginationBefore=server.Requests.Count;var paginationOutput=await Run("run","--project",project,"-c","Release","--no-restore","--no-build");
            Assert(server.Requests.Count==paginationBefore+3,"Three typed pagination routes and invalid nulls cause exactly three requests "+language);
            Assert(server.Requests.TakeLast(3).All(r=>r.Target.Contains("page=2147483648")&&r.Target.Contains("per_page=10")),"Typed consumer preserves large page "+language);
            server.DynamicReply=null;
            results.Add(new{language,status="pass",packageSha256=hash,elapsedMilliseconds=measured.ElapsedMilliseconds,output=output.Trim(),paginationOutput=paginationOutput.Trim(),paginationRequests=3});
            Console.WriteLine(language+" clean package consumer passed");
        }
        var homologation=await HomologationExample(dotnet,destination,Path.Combine(repository,"examples","homologacao"),feed);
        Console.WriteLine("homologation example consumer passed");
        await File.WriteAllTextAsync(Path.Combine(repository,"artifacts","package-consumers.json"),JsonSerializer.Serialize(new{status="pass",packageSha256=hash,results,homologation},new JsonSerializerOptions{WriteIndented=true}));
    }
    // The neutral homologation example, built from the candidate package, against a loopback fixture:
    // blocked without WEBMANIA_LIVE=1, masked lines, ledger, one request per mutation, unknown outcome never repeated.
    static async Task<object> HomologationExample(string dotnet,string destination,string source,string feed)
    {
        const string nfe="11111111-1111-4111-8111-1111111111a1",nfse="11111111-1111-4111-8111-1111111111b2";
        var target=Path.Combine(destination,"Homologacao");Directory.CreateDirectory(target);
        File.Copy(Path.Combine(source,"Program.cs"),Path.Combine(target,"Program.cs"));
        const string reference="<ProjectReference Include=\"../../src/Webmania.Sdk/Webmania.Sdk.csproj\" />";
        var csproj=await File.ReadAllTextAsync(Path.Combine(source,"Homologacao.csproj"));
        Assert(csproj.Contains(reference),"Example references the SDK project");
        var project=Path.Combine(target,"Homologacao.csproj");
        await File.WriteAllTextAsync(project,csproj.Replace(reference,"<PackageReference Include=\"Webmania.Sdk\" Version=\"0.1.0-preview.6\" />"));
        var config=Path.Combine(target,"NuGet.Config");
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("configuration",new System.Xml.Linq.XElement("packageSources",new System.Xml.Linq.XElement("clear"),new System.Xml.Linq.XElement("add",new System.Xml.Linq.XAttribute("key","candidate"),new System.Xml.Linq.XAttribute("value",feed))))).Save(config);
        var log=Path.Combine(destination,"Homologacao.log");var logGate=new object();
        async Task<(int Code,string Stdout,string Stderr)> Exec(IEnumerable<string> arguments,Dictionary<string,string>? environment=null)
        {
            var psi=new System.Diagnostics.ProcessStartInfo(dotnet){WorkingDirectory=target,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var a in arguments)psi.ArgumentList.Add(a);
            foreach(var key in psi.Environment.Keys.Where(k=>k.StartsWith("WEBMANIA_",StringComparison.Ordinal)).ToList())psi.Environment.Remove(key);
            psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";psi.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"]="false";
            if(environment is not null)foreach(var pair in environment)psi.Environment[pair.Key]=pair.Value;
            using var process=System.Diagnostics.Process.Start(psi)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
            var result=(process.ExitCode,await stdout,await stderr);lock(logGate)File.AppendAllText(log,result.Item2+result.Item3);return result;
        }
        var restore=await Exec(["restore",project,"--configfile",config,"--packages",Path.Combine(destination,"packages"),"--no-cache","--force"]);Assert(restore.Code==0,"Homologation example restore failed: "+restore.Stdout+restore.Stderr);
        var build=await Exec(["build",project,"-c","Release","--no-restore","--nologo"]);Assert(build.Code==0,"Homologation example build failed: "+build.Stdout+build.Stderr);
        var dll=Path.Combine(target,"bin","Release","net10.0","Homologacao.dll");
        await using var server=new SocketServer(false);
        var blocked=await Exec([dll]);
        Assert(blocked.Code==77&&blocked.Stdout.Trim()=="{\"result\":\"blocked\"}"&&server.Requests.IsEmpty,"Example without WEBMANIA_LIVE=1 is blocked before any request");
        string[] ids=["nfeGetSefazStatus","nfeGetCertificateValidity","nfeListTaxClasses","emitterGetNfseProviderStatus","nfeIssueDocument","nfeGetDocument","nfeQueryDocument","nfeDownloadDanfe","nfeDownloadSimpleDanfe","nfeDownloadXml","nfeCancelDocument","nfeDownloadCancellationXml","emitterIssueNfse","emitterGetNfse","fileGetNfseXml","fileGetNfsePdf","fileGetNfseRpsPdf","emitterCancelNfse"];
        var routes=ids.Select(id=>(Id:id,Method:Ops[id].GetProperty("method").GetString()!,Pattern:new System.Text.RegularExpressions.Regex("^"+string.Join("/",Ops[id].GetProperty("route").GetString()!.Split('/').Select(segment=>segment.StartsWith('{')?"[^/]+":System.Text.RegularExpressions.Regex.Escape(segment)))+"$"))).ToArray();
        var state=new ConcurrentDictionary<string,string>{["nfe"]="aprovado",["nfse"]="aprovado"};
        string Document(string fixture,string uuid,string status){var node=JsonNode.Parse(Fixtures[fixture].GetProperty("response").GetRawText())!.AsObject();node["uuid"]=uuid;node["status"]=status;return node.ToJsonString();}
        var stuckCancel=false;
        string Cancel(string model,string id){if(!stuckCancel)state[model]="cancelado";return Fixtures[id].GetProperty("response").GetRawText();}
        var failIssue=false;var failCancel=false;
        server.DynamicReply=request=>
        {
            var path=request.Target.Split('?')[0];
            var matched=routes.Where(r=>r.Method==request.Method&&r.Pattern.IsMatch(path)).Select(r=>r.Id).ToArray();
            if(matched.Length!=1)return Reply.Json("{}",404);
            var id=matched[0];
            if(failIssue&&id=="nfeIssueDocument")return Reply.Json("{}",500);
            if(failCancel&&id=="nfeCancelDocument")return Reply.Json("{}",500);
            if(id.Contains("Pdf")||id.Contains("Danfe"))return Reply.Bytes(Pdf(),"application/pdf");
            if(id.Contains("Xml"))return Reply.Bytes("<?xml version=\"1.0\"?><synthetic/>"u8.ToArray(),id=="fileGetNfseXml"?"text/xml":"application/xml");
            return Reply.Json(id switch
            {
                "nfeIssueDocument"=>Document("nfeGetDocument",nfe,"processando"),
                "nfeGetDocument"=>Document(id,nfe,state["nfe"]),
                "nfeCancelDocument"=>Cancel("nfe",id),
                "emitterIssueNfse"=>Document(id,nfse,"processando"),
                "emitterGetNfse"=>Document(id,nfse,state["nfse"]),
                "emitterCancelNfse"=>Cancel("nfse",id),
                _=>Fixtures[id].GetProperty("response").GetRawText()
            });
        };
        var nfePayload=JsonNode.Parse(Fixtures["nfeIssueDocument"].GetProperty("input").GetProperty("body").GetRawText())!.AsObject();nfePayload["ambiente"]=2;
        var nfsePayload=JsonNode.Parse(Fixtures["emitterIssueNfse"].GetProperty("input").GetProperty("body").GetRawText())!.AsObject();nfsePayload["ambiente"]=2;
        await File.WriteAllTextAsync(Path.Combine(target,"nfe.json"),nfePayload.ToJsonString());await File.WriteAllTextAsync(Path.Combine(target,"nfse.json"),nfsePayload.ToJsonString());
        var origin=$"http://127.0.0.1:{server.Port}";var ledger=Path.Combine(target,"ledger.jsonl");
        var secrets=new Dictionary<string,string>{["WEBMANIA_CONSUMER_KEY"]="example-key-value",["WEBMANIA_CONSUMER_SECRET"]="example-secret-value",["WEBMANIA_ACCESS_TOKEN"]="example-access-value",["WEBMANIA_ACCESS_TOKEN_SECRET"]="example-access-secret-value",["WEBMANIA_ISSUER_TOKEN"]="example-issuer-value"};
        var environment=new Dictionary<string,string>(secrets){["WEBMANIA_LIVE"]="1",["WEBMANIA_LIVE_SCOPE"]="all",["WEBMANIA_API1_BASE_URL"]=origin+"/api",["WEBMANIA_FILES_BASE_URL"]=origin,["WEBMANIA_API2_BASE_URL"]=origin,["WEBMANIA_LIVE_ALLOWED_HOSTS"]="127.0.0.1",["WEBMANIA_LIVE_LEDGER"]=ledger,["WEBMANIA_LIVE_NFE_PAYLOAD"]=Path.Combine(target,"nfe.json"),["WEBMANIA_LIVE_NFSE_PAYLOAD"]=Path.Combine(target,"nfse.json")};
        var run=await Exec([dll],environment);
        var lines=run.Stdout.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        Assert(run.Code==0&&lines.All(line=>line.GetProperty("ok").GetBoolean()),"Homologation example full cycle: "+run.Stdout+run.Stderr);
        var steps=lines.Select(line=>line.GetProperty("step").GetString()).ToHashSet();
        foreach(var step in new[]{"readonly.sefaz","readonly.certificate","readonly.tax_classes","readonly.nfse_provider","nfe.issue","nfe.consult","nfe.query","nfe.danfe","nfe.danfe_simple","nfe.xml","nfe.cancel","nfe.consult_cancelled","nfe.cancellation_xml","nfse.issue","nfse.consult","nfse.xml","nfse.pdf","nfse.rps_pdf","nfse.cancel","nfse.consult_cancelled"})Assert(steps.Contains(step),"Homologation step "+step);
        foreach(var value in secrets.Values.Concat(new[]{nfe,nfse,origin}))Assert(!(run.Stdout+run.Stderr).Contains(value,StringComparison.Ordinal),"No private value in example output");
        var entries=File.ReadAllLines(ledger).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        Assert(entries.Select(e=>(e.GetProperty("model").GetString(),e.GetProperty("identifier").GetString())).ToHashSet().SetEquals(new[]{((string?)"nfe",(string?)nfe),("nfse",nfse)}),"Ledger records both documents");
        Assert(entries.Where(e=>e.GetProperty("operation").GetString() is "nfeCancelDocument" or "emitterCancelNfse").Select(e=>e.GetProperty("status").GetString()).SequenceEqual(["cancelado","cancelado"]),"Ledger records both cancellations");
        Assert(server.Requests.Count(r=>r.Method=="POST"&&r.Target=="/1/nfe/emissao/")==1&&server.Requests.Count(r=>r.Method=="POST"&&r.Target=="/2/nfse/emissao")==1,"One request per issue");
        Assert(server.Requests.Count(r=>r.Method=="PUT")==2,"One request per cancellation");
        var before=server.Requests.Count;
        var outside=await Exec([dll],new Dictionary<string,string>(environment){["WEBMANIA_API1_BASE_URL"]="https://other.example.invalid/api"});
        Assert(outside.Code==77&&JsonDocument.Parse(outside.Stdout).RootElement.GetProperty("reason").GetString()=="origin_not_allowed"&&server.Requests.Count==before,"Origin outside the allow list is refused before any request");
        foreach(var hosts in new string?[]{null," , "})
        {
            var unlisted=new Dictionary<string,string>(environment);unlisted.Remove("WEBMANIA_LIVE_ALLOWED_HOSTS");if(hosts is not null)unlisted["WEBMANIA_LIVE_ALLOWED_HOSTS"]=hosts;
            var refused=await Exec([dll],unlisted);
            Assert(refused.Code==77&&refused.Stdout.Trim()=="{\"result\":\"blocked\",\"reason\":\"allowed_hosts\"}"&&server.Requests.Count==before,"Missing or empty allow list is refused before credentials or any request");
        }
        // A mutation scope needs a writable ledger (missing, empty, a directory, a missing parent): 77 before any request.
        foreach(var scope in new[]{"nfe","nfse","all"})foreach(var value in new string?[]{null,"",target,Path.Combine(target,"absent","ledger.jsonl")})
        {
            var unledgered=new Dictionary<string,string>(environment){["WEBMANIA_LIVE_SCOPE"]=scope};unledgered.Remove("WEBMANIA_LIVE_LEDGER");if(value is not null)unledgered["WEBMANIA_LIVE_LEDGER"]=value;
            var refused=await Exec([dll],unledgered);
            Assert(refused.Code==77&&refused.Stdout.Trim()=="{\"result\":\"blocked\",\"reason\":\"ledger\"}"&&server.Requests.Count==before,"A mutation scope without a writable ledger is refused before any request: "+scope+" "+(value is null?"unset":value.Length==0?"empty":value==target?"directory":"missing parent")+" "+refused.Stdout+refused.Stderr);
        }
        Assert(!Directory.Exists(Path.Combine(target,"absent")),"The missing ledger parent is not created");
        failIssue=true;File.Delete(ledger);
        var unknown=await Exec([dll],new Dictionary<string,string>(environment){["WEBMANIA_LIVE_SCOPE"]="nfe"});
        var issueLines=unknown.Stdout.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).Where(line=>line.GetProperty("step").GetString()=="nfe.issue").ToArray();
        Assert(unknown.Code==1&&issueLines.Length==1&&issueLines[0].GetProperty("outcome").GetString()=="unknown","Unknown issue outcome stops the example");
        Assert(server.Requests.Skip(before).Count(r=>r.Method=="POST"&&r.Target=="/1/nfe/emissao/")==1&&!server.Requests.Skip(before).Any(r=>r.Method=="PUT"),"Unknown outcome is never repeated or followed by a cancellation");
        var pending=JsonDocument.Parse(File.ReadAllText(ledger).Trim()).RootElement;
        Assert(pending.GetProperty("status").GetString()=="unknown"&&pending.GetProperty("operation").GetString()=="nfeIssueDocument","Unknown outcome recorded in the ledger");
        failIssue=false;failCancel=true;state["nfe"]="aprovado";File.Delete(ledger);var beforeCancel=server.Requests.Count;
        var uncertainCancel=await Exec([dll],new Dictionary<string,string>(environment){["WEBMANIA_LIVE_SCOPE"]="nfe"});
        var cancelLines=uncertainCancel.Stdout.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).Where(line=>line.GetProperty("step").GetString()=="nfe.cancel").ToArray();
        Assert(uncertainCancel.Code==1&&cancelLines.Length==1&&cancelLines[0].GetProperty("outcome").GetString()=="unknown"&&server.Requests.Skip(beforeCancel).Count(r=>r.Method=="PUT")==1,"Unknown cancellation outcome stops the example without a repeat");
        var cancelEntries=File.ReadAllLines(ledger).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        Assert(cancelEntries.Any(e=>e.GetProperty("identifier").GetString()==nfe&&e.GetProperty("status").GetString()=="unknown"&&e.GetProperty("operation").GetString()=="nfeCancelDocument"),"Unknown cancellation outcome recorded in the ledger");
        Assert(!(uncertainCancel.Stdout+uncertainCancel.Stderr).Contains(nfe,StringComparison.Ordinal),"No private value in example output");
        // An accepted cancellation whose consult never shows "cancelado" fails the run (both cycles run at once).
        failCancel=false;stuckCancel=true;state["nfe"]="aprovado";state["nfse"]="aprovado";var beforeStuck=server.Requests.Count;
        string[] stuckScopes=["nfe","nfse"];
        var stuck=await Task.WhenAll(stuckScopes.Select(scope=>Exec([dll],new Dictionary<string,string>(environment){["WEBMANIA_LIVE_SCOPE"]=scope,["WEBMANIA_LIVE_LEDGER"]=Path.Combine(target,"stuck-"+scope+".jsonl")})));
        stuckCancel=false;
        foreach(var (scope,result) in stuckScopes.Zip(stuck))
        {
            var verdict=result.Stdout.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonDocument.Parse(line).RootElement.Clone()).Where(line=>line.GetProperty("step").GetString()==scope+".consult_cancelled"&&!line.GetProperty("ok").GetBoolean()).ToArray();
            Assert(result.Code==1&&verdict.Length==1&&verdict[0].GetProperty("kind").GetString()=="not_cancelled"&&verdict[0].GetProperty("operation").GetString()==(scope=="nfe"?"nfeGetDocument":"emitterGetNfse"),"A document that never shows cancelado fails the run: "+result.Stdout+result.Stderr);
            Assert(!result.Stdout.Contains("nfe.cancellation_xml",StringComparison.Ordinal),"No cancellation XML without cancelado");
            var last=JsonDocument.Parse(File.ReadAllLines(Path.Combine(target,"stuck-"+scope+".jsonl")).Last()).RootElement;
            Assert(last.GetProperty("status").GetString()=="aprovado"&&last.GetProperty("operation").GetString()==(scope=="nfe"?"nfeCancelDocument":"emitterCancelNfse"),"Ledger keeps the status the final consult observed");
        }
        Assert(server.Requests.Skip(beforeStuck).Count(r=>r.Method=="PUT")==2,"One request per cancellation");
        server.DynamicReply=null;
        return new{status="pass",steps=lines.Length,requests=before};
    }
    static async Task Regressions()
    {
        await using var s=new SocketServer(false);
        using var read=new WebmaniaClient(Credentials("monitor"),s.Options());
        var query="monitorListCompanies";var mutation="monitorCreateCompany";
        s.Response=Reply.Json(Fixtures[query].GetProperty("response").GetRawText()) with{Headers=new(){["X-Request-ID"]="synthetic-trace-correlation"}};
        var correlated=(ApiResponse<Api2MonitorReadsMonitorCompanyPage>)(await Call(read,query))!;Assert(correlated.CorrelationId=="synthetic-trace-correlation"&&WebmaniaClient.Version.StartsWith("0.1.0-preview.6"),"Successful correlation and SDK version exposed");
        for(var status=400;status<=500;status+=100)
        {
            s.Response=Reply.Json("{\"error\":\"synthetic-monitor\",\"message\":\"Bearer synthetic-monitor failed https://example.invalid/a?token=private\"}",status);
            var e=await Error(async()=>await Call(read,query),"http");Assert(!e.OutcomeUncertain,"Reads do not become uncertain");Assert(!e.ToString().Contains("synthetic-monitor"),"Bare bearer redacted");
            using var write=new WebmaniaClient(Credentials("monitor"),s.Options());var m=await Error(async()=>await Call(write,mutation),"http");Assert(m.OutcomeUncertain==(status==500),"Clear 4xx vs uncertain 5xx");
        }
        foreach(var marker in new[]{"false","null","\"\"","[]","{}"})
        {
            s.Response=Reply.Json("{\"error\":"+marker+"}");var error=await Error(async()=>await Call(read,query),"business");Assert(!error.OutcomeUncertain,"Error property presence fails a read without uncertainty");
        }
        using(var write=new WebmaniaClient(Credentials("monitor"),s.Options()))
        {
            s.Response=Reply.Json("{\"error\":\"failed\"}");var e=await Error(async()=>await Call(write,mutation),"business");Assert(e.OutcomeUncertain&&e.SafeReference?.Fingerprint.StartsWith("sha256:")==true,"Mutation 200 error outcome");
            s.Response=Reply.Json("{\"unrecognized\":true}");e=await Error(async()=>await Call(write,mutation),"protocol");Assert(e.OutcomeUncertain,"Invalid success shape uncertain");
        }
        s.Response=Reply.Json("{}",302) with { Headers=new(){["Location"]="https://example.invalid/private"} };var before=s.Requests.Count;
        await Error(async()=>await Call(read,query),"redirect");Assert(s.Requests.Count==before+1,"No redirect request");
        using(var wrong=new WebmaniaClient(Credentials("issuer"),s.Options()))
        { before=s.Requests.Count;await Error(async()=>await Call(wrong,query),"credential_scope");Assert(s.Requests.Count==before,"No scope fallback"); }
        using(var shortClient=new WebmaniaClient(Credentials("monitor"),s.Options(TimeSpan.FromMilliseconds(80))))
        {
            s.Response=Reply.Json("{}") with { DelayBody=TimeSpan.FromMilliseconds(300) };var e=await Error(async()=>await Call(shortClient,mutation),"timeout");Assert(e.OutcomeUncertain,"Timeout includes streamed body");
            using var cancel=new CancellationTokenSource(30);e=await Error(async()=>await Call(shortClient,query,token:cancel.Token),"cancelled");Assert(!e.OutcomeUncertain,"Cancelled read certain");
        }
        s.Response=Reply.Json(Fixtures[query].GetProperty("response").GetRawText());
        using(var bounded=new WebmaniaClient(Credentials("monitor"),s.Options(maxBytes:2)))await Error(async()=>await Call(bounded,query),"response_limit");
        using(var f=new WebmaniaClient(Credentials("api2-files"),s.Options(maxBytes:4)))
        {s.Response=Reply.Bytes([0,1,2,3,4,5],"application/pdf") with { OmitContentLength=true };await Error(async()=>await Call(f,"fileGetNfseXml"),"response_limit");}
        // Typed optional/extension behavior is checked through the actual socket.
        using(var company=new WebmaniaClient(Credentials("company"),s.Options()))
        {
            var p=(FiscalUpdateCompanyParameters)Input("fiscalUpdateCompany",Element("{\"body\":{\"cnae\":null,\"informacoes_fisco\":\"synthetic\",\"future\":{\"object\":{},\"array\":[],\"zero\":0,\"false\":false,\"decimal\":\"9007199254740999.9900\"}}}"));
            s.Response=Reply.Json(Fixtures["fiscalUpdateCompany"].GetProperty("response").GetRawText());await company.FiscalUpdateCompanyAsync(p);
            var body=Element(s.Requests.Last().Body);Assert(body.GetProperty("cnae").ValueKind==JsonValueKind.Null,"Explicit null retained");Assert(!body.TryGetProperty("email",out _),"Missing omitted");Assert(body.GetProperty("future").GetProperty("object").ValueKind==JsonValueKind.Object,"Empty object retained");Assert(body.GetProperty("future").GetProperty("array").ValueKind==JsonValueKind.Array,"Empty array retained");
            p.Body.Value!.Extras["cnae"]=Element("1");before=s.Requests.Count;await Error(async()=>await company.FiscalUpdateCompanyAsync(p),"validation");Assert(before==s.Requests.Count,"Extras cannot shadow declared fields");
        }
        // Validate both flat and nested numbers, retaining arbitrary precision and never accepting rounded bounds.
        s.Response=Reply.Json(Fixtures["monitorSearchDocuments"].GetProperty("response").GetRawText());
        foreach(var input in new[]{"{\"body\":{\"serie\":\"999.00000000000000000000000000001\"}}","{\"body\":{\"serie\":1,\"filtros\":{\"serie\":\"999.00000000000000000000000000001\"}}}","{\"body\":{\"filtros\":{\"valor\":\"-0.00000000000000000000001\"}}}","{\"body\":{\"data_inicial\":\"2026-10-01\",\"data_final\":\"2026-10-02\",\"filtros\":{\"data_inicial\":\"2026-10-02\",\"data_final\":\"2026-10-01\"}}}"})
        {before=s.Requests.Count;await Error(async()=>await Call(read,"monitorSearchDocuments",Element(input)),"validation");Assert(before==s.Requests.Count,"Nested invalid input blocked");}
        await Call(read,"monitorSearchDocuments",Element("{\"body\":{\"serie\":\"999.00000000000000000000000000000\",\"valor\":\"9007199254740999.9900\",\"novos_documentos\":false}}"));Assert(Element(s.Requests.Last().Body).GetProperty("valor").GetString()=="9007199254740999.9900","Exact numeric text unchanged");
        using(var company=new WebmaniaClient(Credentials("company"),s.Options()))
        { before=s.Requests.Count;await Error(async()=>await Call(company,"nfeGetDocument",Element("{}")),"validation");Assert(s.Requests.Count==before,"Lookup selector checked before network"); }
        before=s.Requests.Count;await Error(async()=>await Call(read,"monitorGetDownloadStatistics",Element("{\"ano\":2026}")),"validation");Assert(s.Requests.Count==before,"Statistics year/month pair");
        using(var cancelled=new CancellationTokenSource())
        {cancelled.Cancel();before=s.Requests.Count;await Error(async()=>await Call(read,query,token:cancelled.Token),"cancelled");Assert(s.Requests.Count==before,"Pre-cancelled request never dispatched");}
        s.Response=Reply.Json("{}") with { DisconnectBeforeResponse=true };
        foreach(var operation in new[]{query,"monitorDeleteCompanyMunicipality",mutation,"monitorUpdateCompany"})
        {
            before=s.Requests.Count;var dropped=await Error(async()=>await Call(read,operation),"transport");Assert(s.Requests.Count==before+1,"No replay after a captured "+operation+" loses the connection");Assert(dropped.OutcomeUncertain==(operation!=query),"Captured mutation failure remains uncertain");
        }
        foreach(var pair in new[]{("b2bV1ListCompanies","reseller"),("emitterGetNfse","issuer"),("fileGetNfsePdf","api2-files")})
        {
            using var scoped=new WebmaniaClient(Credentials(pair.Item2),s.Options());before=s.Requests.Count;var effectError=await Error(async()=>await Call(scoped,pair.Item1),"transport");Assert(effectError.OutcomeUncertain,"Declared side-effect GET keeps uncertain outcome "+pair.Item1);Assert(s.Requests.Count==before+1,"No replay for effect-capable GET "+pair.Item1);
        }
        await ReusedAndStaleConnections(s);
        using(var report=new WebmaniaClient(WebmaniaCredentials.Api1ReportCapability(),s.Options()))
        {
            before=s.Requests.Count;await Error(async()=>await Call(report,"nfeDownloadDanfe"),"credential_scope");Assert(s.Requests.Count==before,"Report capability cannot substitute company auth");
            s.Response=Reply.Bytes([65,44,66,10,49,44,50,10],"text/csv");var csv=(FileResult)(await Call(report,"fiscalDownloadReport"))!;Assert(csv.Representation==FileRepresentation.Csv&&csv.IsFile,"CSV report recognized");Assert(!s.Requests.Last().Headers.ContainsKey("authorization")&&!s.Requests.Last().Headers.ContainsKey("x-consumer-key")&&!s.Requests.Last().Target.Contains("token="),"Public report capability sends no company headers or token");
            s.Response=Reply.Bytes(Zip(true),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");var xlsx=(FileResult)(await Call(report,"fiscalDownloadReport"))!;Assert(xlsx.IsFile&&xlsx.Representation==FileRepresentation.Spreadsheet,"XLSX report recognized");
        }
        using(var wrongFileAuth=new WebmaniaClient(Credentials("api1-files"),s.Options())){before=s.Requests.Count;await Error(async()=>await Call(wrongFileAuth,"fiscalDownloadReport"),"credential_scope");Assert(s.Requests.Count==before,"Company headers never automatically attached to report capability");}
        using(var overlap=new WebmaniaClient(WebmaniaCredentials.Company("prefix","prefix-secret-canary","synthetic-access","synthetic-access-secret"),s.Options()))
        {
            s.Response=Reply.Json("{\"error\":\"prefix-secret-canary\"}",401);var overlapping=await Error(async()=>await Call(overlap,"nfeGetCertificateValidity"),"http");Assert(overlapping.Message=="[redacted]","Longest literal secret is removed before a shorter overlapping credential");
        }
        await ExactPagination(s);
        await SnapshotAndFileValidation(s);
        await ServiceEndpointConfiguration();
        await ProtectedReferences(s);
        await PollingAndRedaction(s);
        // Private CA is accepted only with matching hostname, and trust is never silently bypassed.
        await using var tls=new SocketServer(true);using(var client=new WebmaniaClient(Credentials("monitor"),tls.Options())){tls.Response=Reply.Json(Fixtures[query].GetProperty("response").GetRawText());await Call(client,query);}
        var bad=tls.Options();bad.LoopbackTestEndpoint=new Uri($"https://127.0.0.1:{tls.Port}/");using(var client=new WebmaniaClient(Credentials("monitor"),bad))await Error(async()=>await Call(client,query),"transport");
        var noTrust=tls.Options();noTrust.LoopbackTestRootCertificate=null;using(var client=new WebmaniaClient(Credentials("monitor"),noTrust))await Error(async()=>await Call(client,query),"transport");
        Console.WriteLine("transport, schema, decimal, redaction, polling and TLS regressions passed");
    }
    static object PageInput(string id, string? page = null, string? perPage = "10")
    {
        var input=new JsonObject();
        if(id=="monitorListCompanyMunicipalities")input["company"]=U;
        if(page is not null)input["page"]=JsonNode.Parse(page);
        if(perPage is not null)input["per_page"]=JsonNode.Parse(perPage);
        return Input(id,Element(input.ToJsonString()));
    }
    static IAsyncEnumerable<object> PageItems(WebmaniaClient client,string id,object parameters,int maximum=100,CancellationToken token=default) => id switch
    {
        "monitorListCompanies"=>client.IterateMonitorCompaniesAsync((MonitorListCompaniesParameters)parameters,maximum,token),
        "monitorListCompanyMunicipalities"=>client.IterateMonitorCompanyMunicipalitiesAsync((MonitorListCompanyMunicipalitiesParameters)parameters,maximum,token),
        "monitorListReports"=>client.IterateMonitorReportsAsync((MonitorListReportsParameters)parameters,maximum,token),
        _=>throw new Exception("Unknown pagination operation")
    };
    static string PageResponse(string id,string current,string last,string perPage="10",int itemCount=1)
    {
        var item=id switch
        {
            "monitorListCompanies"=>$"{{\"uuid\":\"{U}\"}}",
            "monitorListCompanyMunicipalities"=>$"{{\"uuid\":\"{U}\",\"tipo_download\":1,\"municipio\":\"Synthetic\",\"uf\":\"SP\",\"codigo_ibge\":\"0000000\"}}",
            _=>$"{{\"uuid\":\"{U}\",\"relatorio\":\"csv\",\"modelo\":\"nfe\",\"status\":\"concluido\"}}"
        };
        return $"{{\"data\":[{string.Join(",",Enumerable.Repeat(item,itemCount))}],\"meta\":{{\"current_page\":{current},\"last_page\":{last},\"per_page\":{perPage},\"total\":{itemCount}}}}}";
    }
    static async Task<int> CountPages(IAsyncEnumerable<object> items)
    {var count=0;await foreach(var item in items){Assert(item is JsonModel,"Pagination yields typed DTO");count++;}return count;}
    static async Task ExactPagination(SocketServer server)
    {
        using var client=new WebmaniaClient(Credentials("monitor"),server.Options());
        foreach(var id in new[]{"monitorListCompanies","monitorListCompanyMunicipalities","monitorListReports"})
        {
            foreach(var page in new[]{"2147483648","9223372036854775808","9007199254740993","1e3","1.0"})
            {
                server.Response=Reply.Json(PageResponse(id,page,page));var before=server.Requests.Count;
                Assert(await CountPages(PageItems(client,id,PageInput(id,page),1))==1,"Exact page yields one typed item "+id);
                var query=new Uri("http://localhost"+server.Requests.Last().Target).Query;
                Assert(server.Requests.Count==before+1&&query.Contains("page="+Uri.EscapeDataString(page)),"Initial integer page preserved exactly "+id+" "+page);
            }
            server.Response=Reply.Json(PageResponse(id,"1","1"));var omittedBefore=server.Requests.Count;
            await CountPages(PageItems(client,id,PageInput(id,page:null,perPage:null),1));
            Assert(server.Requests.Count==omittedBefore+1&&!server.Requests.Last().Target.Contains("page="),"Omitted page/per_page remain omitted in initial request "+id);
            foreach(var page in new[]{"null","0","-1","1.5","1e4096"})
            {var before=server.Requests.Count;await Error(async()=>await CountPages(PageItems(client,id,PageInput(id,page),1)),"validation");Assert(server.Requests.Count==before,"Invalid/null/out-of-helper-range page blocked before I/O "+id);}
            foreach(var perPage in new[]{"null","0","9","201","10.5"})
            {var before=server.Requests.Count;await Error(async()=>await CountPages(PageItems(client,id,PageInput(id,"1",perPage),1)),"validation");Assert(server.Requests.Count==before,"Invalid/null per_page blocked before I/O "+id);}
            var collision=(JsonModel)PageInput(id,"1");collision.Extras["page"]=Element("2");
            var beforeCollision=server.Requests.Count;await Error(async()=>await CountPages(PageItems(client,id,collision,1)),"validation");Assert(server.Requests.Count==beforeCollision,"Pagination serialization collision normalized "+id);
            server.Response=Reply.Json(PageResponse(id,"1","3","20",20));var explicitSizeBefore=server.Requests.Count;var sizeYields=0;
            await Error(async()=>{await foreach(var _ in PageItems(client,id,PageInput(id,"1","40"),3))sizeYields++;},"protocol");
            Assert(sizeYields==0&&server.Requests.Count==explicitSizeBefore+1,"Explicit per_page mismatch rejected before yielding incompatible data "+id);
            var sizeReplies=0;server.DynamicReply=_=>++sizeReplies==1?Reply.Json(PageResponse(id,"1","2","20",20)):Reply.Json(PageResponse(id,"2","2","40",40));
            var observedYields=0;var observedBefore=server.Requests.Count;
            await Error(async()=>{await foreach(var _ in PageItems(client,id,PageInput(id,"1",perPage:null),3))observedYields++;},"protocol");
            Assert(observedYields==20&&server.Requests.Count==observedBefore+2&&server.Requests.TakeLast(2).All(r=>!r.Target.Contains("per_page=")),"Omitted per_page locks observed metadata and rejects changed size before second-page yield "+id);
            server.DynamicReply=null;server.Response=Reply.Json(PageResponse(id,"2147483648","1","10",0));var emptyBefore=server.Requests.Count;
            Assert(await CountPages(PageItems(client,id,PageInput(id,"2147483648"),3))==0&&server.Requests.Count==emptyBefore+1&&server.Requests.Last().Target.Contains("page=2147483648"),"Empty requested page beyond last_page terminates without rewriting "+id);
            server.Response=Reply.Json(PageResponse(id,"2147483648","1"));var inconsistentYields=0;
            await Error(async()=>{await foreach(var _ in PageItems(client,id,PageInput(id,"2147483648"),3))inconsistentYields++;},"protocol");Assert(inconsistentYields==0,"Nonempty data beyond last_page is rejected before yield "+id);
            var pages=0;server.DynamicReply=_=>Reply.Json(PageResponse(id,(BigInteger.Parse("9223372036854775808")+pages++).ToString(),"9223372036854775809"));
            var input=PageInput(id,"9223372036854775808");var beforeTwo=server.Requests.Count;await using(var iterator=PageItems(client,id,input,2).GetAsyncEnumerator())
            {
                Assert(await iterator.MoveNextAsync(),"First exact page "+id);
                input.GetType().GetProperty("PerPage")!.SetValue(input,Optional<JsonNumber>.Of(new JsonNumber("200")));
                Assert(await iterator.MoveNextAsync()&&!await iterator.MoveNextAsync(),"Second exact page terminates "+id);
            }
            var captured=server.Requests.TakeLast(2).ToArray();Assert(server.Requests.Count==beforeTwo+2&&captured[0].Target.Contains("page=9223372036854775808")&&captured[1].Target.Contains("page=9223372036854775809")&&captured.All(x=>x.Target.Contains("per_page=10")),"Exact increment and frozen per_page "+id);
            server.DynamicReply=null;server.Response=Reply.Json(PageResponse(id,"1","2"));var limitBefore=server.Requests.Count;await Error(async()=>await CountPages(PageItems(client,id,PageInput(id,"1"),1)),"page_limit");Assert(server.Requests.Count==limitBefore+1,"Bounded page count "+id);
            var stuck=0;server.DynamicReply=_=>{stuck++;return Reply.Json(PageResponse(id,"1","2"));};await Error(async()=>await CountPages(PageItems(client,id,PageInput(id,"1"),3)),"protocol");Assert(stuck==2,"Non-advancing metadata stops "+id);server.DynamicReply=null;
            using var cancellation=new CancellationTokenSource();server.Response=Reply.Json(PageResponse(id,"1","2"));var cancelBefore=server.Requests.Count;
            await using(var iterator=PageItems(client,id,PageInput(id,"1"),3,cancellation.Token).GetAsyncEnumerator())
            {
                Assert(await iterator.MoveNextAsync(),"First page before cancellation "+id);cancellation.Cancel();
                try{await iterator.MoveNextAsync();throw new Exception("Cancellation ignored");}catch(OperationCanceledException){checks++;}
            }
            Assert(server.Requests.Count==cancelBefore+1,"Cancelled pagination sends no further request "+id);
        }
        Console.WriteLine("B1 exact pagination, omission/null, typed bounds, limits and cancellation regressions passed for three routes");
    }
    static async Task SnapshotAndFileValidation(SocketServer server)
    {
        using(var company=new WebmaniaClient(Credentials("company"),server.Options()))
        {
            server.Response=Reply.Json("{\"error\":\"generic-extra-canary\",\"message\":\"generic-extra-canary\"}",400) with{Headers=new(){["X-Request-ID"]="generic-extra-canary"}};
            var error=await Error(async()=>await Call(company,"nfeCancelDocument",Element("{\"body\":{\"uuid\":\""+U+"\",\"motivo\":\"Synthetic cancellation justification.\",\"extra_data\":{\"memo\":\"generic-extra-canary\"}}}")),"http");
            Assert(!JsonSerializer.Serialize(error).Contains("generic-extra-canary")&&error.CorrelationId=="[redacted]","Complete serialized snapshot redacts generic extras in message/details/correlation");
            Assert(error.SafeReference?.Reveal()==U,"Diagnostic snapshot does not disqualify safe fiscal reference");
        }
        using var files=new WebmaniaClient(Credentials("api1-files"),server.Options());
        foreach(var reply in new[]{Reply.Bytes("<html><body>login</body></html>"u8.ToArray(),"application/pdf"),Reply.Json("{\"status\":\"pending\"}") with{Media="application/pdf"}})
        {server.Response=reply;await Error(async()=>await Call(files,"nfeDownloadDanfe"),"protocol");}
        // Malformed XML and ZIP on operations that declare those media reach the structural validators.
        server.Response=Reply.Bytes("not XML"u8.ToArray(),"application/xml");
        await Error(async()=>await Call(files,"nfeDownloadXml"),"protocol");
        using(var report=new WebmaniaClient(Credentials("report-file"),server.Options()))
        {
            server.Response=Reply.Bytes([80,75,3,4],"application/zip");
            await Error(async()=>await Call(report,"fiscalDownloadReport"),"protocol");
            // Per-entry ZIP64 markers are Unsupported, and an oversized classic field is a protocol error, never an overflow.
            static byte[] Patched(int field,uint value,int width)
            {
                var zip=Zip();var eof=zip.Length-22;var entry=(int)BitConverter.ToUInt32(zip,eof+16)+field;
                if(width==2)BitConverter.GetBytes((ushort)value).CopyTo(zip,entry);else BitConverter.GetBytes(value).CopyTo(zip,entry);
                return zip;
            }
            foreach(var (field,width) in new[]{(20,4),(24,4),(42,4),(34,2)})
            {
                server.Response=Reply.Bytes(Patched(field,0xFFFFFFFF,width),"application/zip");
                var zip64=(FileResult)(await Call(report,"fiscalDownloadReport"))!;
                Assert(zip64.Readiness==FileReadiness.Unsupported&&!zip64.IsFile,$"ZIP64 marker at central-directory offset {field} is Unsupported");
            }
            server.Response=Reply.Bytes(Patched(20,0x80000000,4),"application/zip");
            await Error(async()=>await Call(report,"fiscalDownloadReport"),"protocol");
            // FILE-07: CSV and XLSX labeled text/xml are classified by bytes; XML, HTML and JSON are refused.
            var latin1=new byte[]{(byte)'a',(byte)';',(byte)'b',10,(byte)'J',(byte)'o',0xE3,(byte)'o',10};
            server.Response=Reply.Bytes(latin1,"text/xml");
            var csv=(FileResult)(await Call(report,"fiscalDownloadReport"))!;
            Assert(csv.MediaType=="text/csv"&&csv.Bytes.Span.SequenceEqual(latin1)&&csv.IsFile,"Legacy CSV labeled text/xml is byte-exact, Latin-1 included");
            server.Response=Reply.Bytes(Zip(true),"text/xml");
            var book=(FileResult)(await Call(report,"fiscalDownloadReport"))!;
            Assert(book.MediaType=="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"&&book.Representation==FileRepresentation.Spreadsheet,"Legacy XLSX labeled text/xml is identified by the ZIP signature");
            foreach(var body in new[]{"<?xml version=\"1.0\"?><nfeProc/>","<!doctype html><html></html>","  {\"status\":\"pending\"}"})
            {server.Response=Reply.Bytes(Encoding.UTF8.GetBytes(body),"text/xml");var refused=await Error(async()=>await Call(report,"fiscalDownloadReport"),"protocol");Assert(refused.OutcomeUncertain,"Refused report keeps its possibly quota-consuming outcome unknown");}
            // A JSON error object is the server's explicit refusal: a business error, never a CSV file.
            server.Response=Reply.Bytes("  {\"error\":\"x\"}"u8.ToArray(),"text/xml");await Error(async()=>await Call(report,"fiscalDownloadReport"),"business");
            server.Response=Reply.Bytes(latin1,"text/csv");
            var current=(FileResult)(await Call(report,"fiscalDownloadReportV2"))!;
            Assert(current.Bytes.Span.SequenceEqual(latin1)&&!server.Requests.Last().Headers.ContainsKey("authorization")&&!server.Requests.Last().Headers.ContainsKey("x-consumer-key"),"Current report download is byte-exact and sends no credential");
            Assert(server.Requests.Last().Target.StartsWith("/relatorios/",StringComparison.Ordinal),"Current report route");
        }
        using(var api2Files=new WebmaniaClient(Credentials("api2-files"),server.Options()))
        {
            const string location="https://municipality.example.invalid/pdf?token=protected-location-canary";
            server.Response=Reply.Json("{}",302) with{Headers=new(){["Location"]=location}};var before=server.Requests.Count;
            var redirect=(FileResult)(await Call(api2Files,"fileGetNfsePdf"))!;
            Assert(redirect.StatusCode==302&&redirect.Representation==FileRepresentation.Redirect&&!redirect.IsFile&&redirect.RedirectLocation?.Reveal()==location,"Documented file redirect preserved through explicit protected metadata");
            Assert(server.Requests.Count==before+1&&!JsonSerializer.Serialize(redirect).Contains("protected-location-canary")&&!redirect.ToString().Contains("protected-location-canary"),"Documented redirect never followed or exposed by ordinary JSON/ToString");
        }
        server.Response=Reply.Bytes(Pdf(),"application/pdf");var pdf=(FileResult)(await Call(files,"nfeDownloadDanfe"))!;Assert(pdf.IsFile&&pdf.Bytes.Span.SequenceEqual(Pdf()),"Valid PDF control preserved byte-for-byte");
    }
    static async Task ServiceEndpointConfiguration()
    {
        await using var server=new SocketServer(true);server.Response=Reply.Json(Fixtures["monitorListCompanies"].GetProperty("response").GetRawText());
        var options=server.Options();options.LoopbackTestEndpoint=null;options.ServiceEndpoint=new Uri($"https://localhost:{server.Port}/controlled-prefix/");
        using(var client=new WebmaniaClient(Credentials("monitor"),options))await Call(client,"monitorListCompanies");
        Assert(server.Requests.Last().Target.StartsWith("/controlled-prefix/2/monitor/empresas?"),"Explicit HTTPS service origin/prefix used by the public client");Assert(server.Requests.Last().Headers["authorization"]=="Bearer synthetic-monitor","Explicit endpoint retains credential scope");
        foreach(var endpoint in new[]{"http://example.invalid/","https://user:pass@example.invalid/","https://example.invalid/base?x=1","https://example.invalid/base#frag","https://example.invalid/base?","https://example.invalid/a/../b","https://example.invalid/a%2Fb"})
        {
            try{using var client=new WebmaniaClient(Credentials("monitor"),new(){ServiceEndpoint=new Uri(endpoint)});throw new Exception("Unsafe endpoint accepted");}catch(ArgumentException){checks++;}
        }
    }
    static async Task LocationDiagnostics(bool tls)
    {
        await using var server=new SocketServer(tls);
        const string pathUuid="FEDCBAFE-ABCD-4ABC-8ABC-FEDCBAFEDCBA";
        const string opaqueUuid="DEADBEEF-ABCD-4ABC-8ABC-DEADBEEFDEAD";
        const string token="LocationOpaqueAbC123";
        var count=0;
        async Task Check(string label,string location,string correlation,string expected,bool valid=true,bool duplicate=false)
        {
            foreach(var documented in new[]{true,false})
            {
                using var client=new WebmaniaClient(Credentials(documented?"api2-files":"company"),server.Options());
                server.Response=Reply.Json("{}",302) with{Headers=new(){["Location"]=location,["X-Request-ID"]=correlation},HeaderLines=duplicate?[new("Location","/duplicate/"+pathUuid)]:[]};
                var before=server.Requests.Count;
                if(documented&&valid&&!duplicate)
                {
                    FileResult result;
                    try { result=(FileResult)(await Call(client,"fileGetNfsePdf"))!; }
                    catch(WebmaniaException error) { throw new Exception($"Expected valid Location: {label}; received {error.Kind}."); }
                    Assert(result.Representation==FileRepresentation.Redirect&&!result.IsFile&&result.RedirectLocation?.Reveal()==location.Trim(' ','\t'),"Protected deliberate Location reveal: "+label);
                    Assert(result.CorrelationId==expected,"Documented Location correlation: "+label);
                    Assert(!JsonSerializer.Serialize(result.RedirectLocation).Contains(pathUuid,StringComparison.OrdinalIgnoreCase)&&!result.ToString().Contains(token),"Ordinary redirect diagnostics are protected: "+label);
                }
                else
                {
                    var error=await Error(async()=>await Call(client,documented?"fileGetNfsePdf":"fiscalUpdateCompany"),valid&&!duplicate?"redirect":"protocol");
                    Assert(error.CorrelationId==expected,"Refused Location correlation: "+label);
                    Assert(error.OutcomeUncertain,"Potential mutation receipt remains uncertain: "+label);
                    if(!expected.Contains(pathUuid,StringComparison.OrdinalIgnoreCase))Assert(!JsonSerializer.Serialize(error).Contains(pathUuid,StringComparison.OrdinalIgnoreCase),"Exception JSON masks Location path UUID: "+label);
                }
                Assert(server.Requests.Count==before+1,"One request without redirect replay: "+label);count++;
            }
        }
        await Check("relative-path-UUID","/private/"+pathUuid+"?token="+token,pathUuid.ToLowerInvariant(),"[redacted]");
        await Check("absolute-path-UUID","https://files.example.invalid/private/"+pathUuid,pathUuid.ToLowerInvariant(),"[redacted]");
        await Check("network-relative-path-UUID","//files.example.invalid/private/"+pathUuid,pathUuid.ToLowerInvariant(),"[redacted]");
        await Check("encoded-path-UUID","/private/%46"+pathUuid[1..],pathUuid.ToLowerInvariant(),"[redacted]");
        await Check("malformed-authority-path-UUID","https://[invalid/private/"+pathUuid,pathUuid.ToLowerInvariant(),"[redacted]",false);
        await Check("malformed-network-authority-path-UUID","//[invalid/private/"+pathUuid,pathUuid.ToLowerInvariant(),"[redacted]",false);
        await Check("malformed-escape-path-UUID","/private/"+pathUuid+"?bad=%ZZ",pathUuid.ToLowerInvariant(),"[redacted]",false);
        await Check("encoded-query-token","/private/file?token=LocationOpaque%41bC123",token,"[redacted]");
        await Check("opaque-query-case-control","/private/file?token="+token,token.ToLowerInvariant(),token.ToLowerInvariant());
        await Check("opaque-query-UUID-control","/private/file?token="+opaqueUuid,opaqueUuid.ToLowerInvariant(),opaqueUuid.ToLowerInvariant());
        await Check("authority-UUID-control","https://"+opaqueUuid+".example.invalid/private/file",opaqueUuid.ToLowerInvariant(),opaqueUuid.ToLowerInvariant());
        await Check("malformed-authority-UUID-control","https://["+opaqueUuid+"/private/file",opaqueUuid.ToLowerInvariant(),opaqueUuid.ToLowerInvariant(),false);
        await Check("userinfo-UUID-control","https://"+opaqueUuid+"@files.example.invalid/private/file",opaqueUuid.ToLowerInvariant(),opaqueUuid.ToLowerInvariant(),false);
        await Check("userinfo-exact-literal","https://user:"+token+"@files.example.invalid/private/file",token,"[redacted]",false);
        await Check("encoded-authority-delimiter-control","https://[invalid%2f"+opaqueUuid,opaqueUuid.ToLowerInvariant(),opaqueUuid.ToLowerInvariant(),false);
        await Check("query-encoded-path-control","/file?token=%2f"+opaqueUuid,opaqueUuid.ToLowerInvariant(),opaqueUuid.ToLowerInvariant());
        await Check("fragment-literal","/private/file#"+token,token,"[redacted]");
        await Check("absolute-fragment-literal","https://files.example.invalid/file#"+token,token,"[redacted]");
        await Check("fragment-only-literal","#"+token,token,"[redacted]");
        await Check("query-only-literal","?token="+token,token,"[redacted]");
        await Check("relative-OWS"," \tprivate/"+pathUuid+" \t",pathUuid.ToLowerInvariant(),"[redacted]");
        await Check("IPv6-authority","https://[::1]/private/"+pathUuid,pathUuid.ToLowerInvariant(),"[redacted]");
        await Check("opaque-query-UUID-exact","/private/file?token="+opaqueUuid,opaqueUuid,"[redacted]");
        await Check("ordinary-credential-case-control","/private/file","SYNTHETIC-SECRET","SYNTHETIC-SECRET");
        await Check("embedded-UUID-boundary-control","/private/A"+pathUuid+"B",pathUuid.ToLowerInvariant(),pathUuid.ToLowerInvariant());
        await Check("duplicate-singleton","/first/file",pathUuid.ToLowerInvariant(),"[redacted]",false,true);
        foreach(var invalid in new[]{"", "https://files.example.invalid:","https://files.example.invalid:99999/file","https:///file","///file","/file%0d%0a","/file with space","https://a.example.invalid/x,https://b.example.invalid/y","ftp://files.example.invalid/file","https://files_.example.invalid/file","https://files.example.invalid../file","/file#fragment#extra"})
            await Check("invalid-reference",invalid,"safe-trace","safe-trace",false);
        using(var read=new WebmaniaClient(Credentials("monitor"),server.Options()))
        {
            server.Response=Reply.Json(Fixtures["monitorListCompanies"].GetProperty("response").GetRawText()) with{Headers=new(){["Location"]="/private/"+pathUuid,["X-Request-ID"]=pathUuid.ToLowerInvariant()}};
            var result=(ApiResponse<Api2MonitorReadsMonitorCompanyPage>)(await Call(read,"monitorListCompanies"))!;
            Assert(result.CorrelationId=="[redacted]","Location is observed on nonredirect JSON success");count++;
        }
        using(var files=new WebmaniaClient(Credentials("api2-files"),server.Options()))
        {
            server.Response=Reply.Bytes(Pdf(),"application/pdf") with{Headers=new(){["Location"]="/private/file?token="+token,["X-Request-ID"]=token,["Content-Disposition"]="attachment; filename=\""+token+".pdf\""}};
            var result=(FileResult)(await Call(files,"fileGetNfsePdf"))!;
            Assert(result.IsFile&&result.CorrelationId=="[redacted]"&&!JsonSerializer.Serialize(result.Headers).Contains(token),"Location components mask nonredirect file correlation and metadata");count++;
        }
        using(var company=new WebmaniaClient(Credentials("company"),server.Options()))
        {
            server.Response=Reply.Json("{\"error\":{\"message\":\""+pathUuid.ToLowerInvariant()+" "+token+"\",\"code\":\""+token+"\",\"field\":\""+pathUuid.ToLowerInvariant()+"\"}}",400) with{Headers=new(){["Location"]="https://[invalid/private/"+pathUuid+"?token="+token,["X-Request-ID"]=pathUuid.ToLowerInvariant()}};
            var error=await Error(async()=>await Call(company,"fiscalUpdateCompany"),"http");
            Assert(error.CorrelationId=="[redacted]"&&!JsonSerializer.Serialize(error).Contains(token)&&!JsonSerializer.Serialize(error).Contains(pathUuid,StringComparison.OrdinalIgnoreCase),"Location is observed before nonredirect error message, details and correlation");count++;
            server.Response=Reply.Json("{}",302) with{Headers=new(){["Location"]="/private/"+U}};
            error=await Error(async()=>await Call(company,"nfeCancelDocument",Element("{\"body\":{\"uuid\":\""+U+"\",\"motivo\":\"Synthetic cancellation justification.\"}}")),"redirect");Assert(error.SafeReference?.CanReveal==false,"Same-response private Location UUID is excluded from recovery selectors");count++;
        }
        await using var destination=new SocketServer(tls);
        using(var client=new WebmaniaClient(Credentials("api2-files"),server.Options()))
        {
            var location=$"{(tls?"https://localhost":"http://127.0.0.1")}:{destination.Port}/private/{pathUuid}?token={token}";
            server.Response=Reply.Json("{}",302) with{Headers=new(){["Location"]=location}};
            var before=server.Requests.Count;var result=(FileResult)(await Call(client,"fileGetNfsePdf"))!;
            Assert(result.RedirectLocation?.Reveal()==location&&destination.Requests.IsEmpty,"Revealing Location makes no request and forwards no credentials");
            server.Response=Reply.Bytes(Pdf(),"application/pdf");await Call(client,"fileGetNfsePdf");
            Assert(server.Requests.Count==before+2&&destination.Requests.IsEmpty&&server.Requests.Last().Headers["authorization"]=="Bearer synthetic-files","Returned Location never changes endpoint trust or credential destination");count+=2;
        }
        Console.WriteLine($"A7 Location diagnostics {(tls?"TLS":"HTTP")} passed ({count} real requests; private path UUIDs, opaque controls, singleton validation and origin isolation)");
    }
    static async Task ReusedAndStaleConnections(SocketServer server)
    {
        var query="monitorListCompanies";
        using var client=new WebmaniaClient(Credentials("monitor"),server.Options());
        var calls=0;server.DynamicReply=_=>++calls==1?Reply.Json(Fixtures[query].GetProperty("response").GetRawText()) with{KeepAlive=true}:Reply.Json("{}") with{DisconnectBeforeResponse=true};
        var before=server.Requests.Count;await Call(client,query);await Error(async()=>await Call(client,query),"transport");var captured=server.Requests.TakeLast(2).ToArray();
        Assert(server.Requests.Count==before+2&&calls==2,"No replay after EOF on reused connection");Assert(captured[0].ConnectionId==captured[1].ConnectionId,"Fixture exercised actual connection reuse");
        server.DynamicReply=null;server.Response=Reply.Json(Fixtures[query].GetProperty("response").GetRawText()) with{KeepAlive=true,CloseSilentlyAfterReply=true};
        before=server.Requests.Count;await Call(client,query);await Call(client,query);Assert(server.Requests.Count==before+2,"Stale keep-alive does not duplicate HTTP submissions");
        server.Response=Reply.Json("{}") with{DelayBody=TimeSpan.FromSeconds(1)};
        using var cancellation=new CancellationTokenSource();before=server.Requests.Count;var mutation=Call(client,"monitorCreateCompany",token:cancellation.Token);
        for(var wait=0;server.Requests.Count==before&&wait<200;wait++)await Task.Delay(1);Assert(server.Requests.Count==before+1,"Mutation captured before cancellation");cancellation.Cancel();
        var error=await Error(async()=>await mutation,"cancelled");Assert(error.OutcomeUncertain,"Cancellation after mutation receipt is uncertain");Assert(server.Requests.Count==before+1,"Cancelled mutation never replayed");
    }
    static async Task ProtectedReferences(SocketServer server)
    {
        using var company=new WebmaniaClient(Credentials("company"),server.Options());
        var mutation=Element("{\"body\":{\"uuid\":\""+U+"\",\"motivo\":\"Synthetic cancellation justification.\"}}");
        server.Response=Reply.Json("{\"error\":\"Synthetic failure\"}",500);
        var e=await Error(async()=>await Call(company,"nfeCancelDocument",mutation),"http");
        Assert(e.SafeReference?.CanReveal==true && e.SafeReference.Reveal()==U,"Fiscal UUID retained for explicit recovery");
        Assert(!JsonSerializer.Serialize(e).Contains(U,StringComparison.OrdinalIgnoreCase),"Default error JSON protects UUID");
        Assert(!JsonSerializer.Serialize(e.SafeReference).Contains(U,StringComparison.OrdinalIgnoreCase),"Default reference JSON protects UUID");
        Assert(!e.SafeReference!.ToString().Contains(U,StringComparison.OrdinalIgnoreCase),"Reference ToString protects UUID");
        server.Response=Reply.Json(Fixtures["nfeGetDocument"].GetProperty("response").GetRawText());
        await company.NfeGetDocumentAsync(e.SafeReference.ToLookupParameters());
        Assert(server.Requests.Last().Target.Contains("uuid="+U),"Retained reference used for an actual consultation socket");
        // An authenticated company URL does not turn an ordinary UUID into a public report capability.
        var response=JsonNode.Parse(Fixtures["nfeGetDocument"].GetProperty("response").GetRawText())!.AsObject();response["url"]="https://api.webmania.com.br/2/monitor/empresas/"+U;
        server.Response=Reply.Json(response.ToJsonString());await company.NfeGetDocumentAsync(e.SafeReference.ToLookupParameters());
        server.Response=Reply.Json("{\"error\":false}",500);e=await Error(async()=>await Call(company,"nfeCancelDocument",mutation),"http");Assert(e.SafeReference?.CanReveal==true,"Authenticated URL does not suppress a usable fiscal reference");
        using var colliding=new WebmaniaClient(WebmaniaCredentials.Company(U,"synthetic-secret","synthetic-access","synthetic-access-secret"),server.Options());
        e=await Error(async()=>await Call(colliding,"nfeCancelDocument",mutation),"http");Assert(e.SafeReference?.CanReveal==false,"Credential collision is excluded from retained references");
        var secret=JsonNode.Parse(mutation.GetRawText())!;secret["body"]!["password"]=U;
        e=await Error(async()=>await Call(company,"nfeCancelDocument",Element(secret.ToJsonString())),"http");Assert(e.SafeReference?.CanReveal==false,"Sensitive body collision excluded from reference");
    }
    static async Task PollingAndRedaction(SocketServer server)
    {
        using var company=new WebmaniaClient(Credentials("company"),server.Options());
        var report=(FiscalCreateOrPollReportParameters)Input("fiscalCreateOrPollReport");
        using(var firstReport=new WebmaniaClient(Credentials("company"),server.Options()))
        {
            server.Response=Reply.Json($"{{\"error\":\"failure\",\"uuid\":\"{U}\"}}",500) with{Headers=new(){["X-Request-ID"]=U.ToLowerInvariant()}};
            var firstError=await Error(async()=>await Call(firstReport,"fiscalCreateOrPollReport"),"http");Assert(firstError.CorrelationId=="[redacted]"&&!JsonSerializer.Serialize(firstError).Contains(U,StringComparison.OrdinalIgnoreCase),"New report capability redacts correlation in the same response");
        }
        server.Response=Reply.Json($"{{\"uuid\":\"{U}\",\"status\":\"processando\"}}");
        var calls=0;
        server.DynamicReply=_=>++calls==1 ? Reply.Json($"{{\"uuid\":\"{U}\",\"status\":\"processando\"}}") : Reply.Json($"{{\"error\":\"Report {U.ToLowerInvariant()} failed\",\"message\":\"Report aBcDeFaB-cDeF-4aBc-8dEf-AbCdEfAbCdEf failed\"}}",500) with { Headers=new(){["X-Request-ID"]=U.ToLowerInvariant()} };
        var e=await Error(async()=>await company.PollFiscalReportAsync(report,new(){MaximumAttempts=2,Deadline=TimeSpan.FromSeconds(8)}),"http");
        Assert(e.OutcomeUncertain,"Polling uncertain mutation");Assert(!e.ToString().ToLowerInvariant().Contains(U.ToLowerInvariant()),"UUID capabilities case insensitive");Assert(e.CorrelationId=="[redacted]","Correlation redacted");Assert(calls==2,"No API error retry");
        server.DynamicReply=null;
        server.Response=Reply.Json("{\"error\":\"failure\"}",500);
        var referenceError=await Error(async()=>await Call(company,"nfeCancelDocument",Element("{\"body\":{\"uuid\":\""+U.ToLowerInvariant()+"\",\"motivo\":\"Synthetic cancellation justification.\"}}")),"http");
        Assert(referenceError.SafeReference?.CanReveal==false,"Known report capability excluded from fiscal references case insensitively");
        var snapshot=server.Requests.TakeLast(3).Take(2).ToArray();Assert(snapshot[0].Body==snapshot[1].Body,"Report frozen identical serialized request");
        server.Response=Reply.Json($"{{\"uuid\":\"{U}\",\"status\":\"processando\"}}");
        e=await Error(async()=>await company.PollFiscalReportAsync(report,new(){Deadline=TimeSpan.FromMilliseconds(80)}),"timeout");Assert(e.OutcomeUncertain,"Polling delay inside total deadline");
        server.Response=Reply.Json("{\"error\":\"failure\"}");
        JsonNode deep=JsonValue.Create("deep-sensitive-canary")!;deep=new JsonObject{["Ｓｅｎｈａ"]=deep};for(var depth=0;depth<45;depth++)deep=new JsonObject{["layer"]=deep};
        var deepInput=new JsonObject{["body"]=new JsonObject{["future"]=deep}};
        server.Response=Reply.Json("{\"error\":\"deep-sensitive-canary\"}",400);
        var deepError=await Error(async()=>await Call(company,"fiscalUpdateCompany",Element(deepInput.ToJsonString())),"http");Assert(!JsonSerializer.Serialize(deepError).Contains("deep-sensitive-canary"),"Body secrets covered at every accepted nesting depth and compatibility-normalized key");
        // Sensitive values are gathered from the serialized payload, including numeric Unicode echoes.
        using var sensitive=new WebmaniaClient(WebmaniaCredentials.Company("key","a+b[1]","access","last"),server.Options());
        var input=Element("{\"body\":{\"certificado\":\"Zml4dHVyZQ==\",\"certificado_senha\":\"CaseSensitivePass\",\"passwords\":{\"number\":12345678901234,\"uuid\":\""+U+"\"}}}");
        server.Response=Reply.Json("{\"error\":\"a+b[1] CaseSensitivePass ١٢٣٤٥٦٧٨٩٠١٢٣٤ ABCDEFAB-CDEF-4ABC-8DEF-ABCDEFABCDEF https://example.invalid/cap?token=private-canary\",\"message\":\"private-canary failed\"}",400);
        e=await Error(async()=>await Call(sensitive,"fiscalUpdateCompany",input),"http");var exposed=JsonSerializer.Serialize(e.Details)+e.Message+e.ToString();
        foreach(var secret in new[]{"a+b[1]","CaseSensitivePass","١٢٣٤٥٦٧٨٩٠١٢٣٤",U,"private-canary"})Assert(!exposed.Contains(secret),"Error redaction "+secret);
    }
}
sealed record Request(string Method,string Target,Dictionary<string,string> Headers,string Body,int ConnectionId);
sealed record Reply(int Status,string Media,byte[] Body)
{
    public TimeSpan DelayBody { get; init; }
    public bool DisconnectBeforeResponse { get; init; }
    public bool KeepAlive { get; init; }
    public bool CloseSilentlyAfterReply { get; init; }
    public bool OmitContentLength { get; init; }
    public Dictionary<string,string> Headers { get; init; }=new();
    public List<KeyValuePair<string,string>> HeaderLines { get; init; }=[];
    public static Reply Json(string json,int status=200)=>new(status,"application/json",Encoding.UTF8.GetBytes(json));
    public static Reply Bytes(byte[] bytes,string media,int status=200)=>new(status,media,bytes);
}
sealed class SocketServer : IAsyncDisposable
{
    readonly TcpListener listener=new(IPAddress.Loopback,0);
    readonly CancellationTokenSource stop=new();
    readonly List<Task> connections=[];
    readonly bool tls;
    readonly X509Certificate2? leaf;
    readonly X509Certificate2? root;
    readonly Task accept;
    int connectionCount;
    public ConcurrentQueue<Request> Requests { get; }=new();
    public Reply Response { get; set; }=Reply.Json("{}");
    public Func<Request,Reply>? DynamicReply { get; set; }
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public SocketServer(bool tls)
    {
        this.tls=tls;
        if(tls)
        {
            using var rootKey=RSA.Create(2048);var cr=new CertificateRequest("CN=Webmania synthetic test root",rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            cr.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));cr.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
            root=cr.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(1));
            using var leafKey=RSA.Create(2048);var lr=new CertificateRequest("CN=localhost",leafKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");lr.CertificateExtensions.Add(san.Build());lr.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
            lr.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new System.Security.Cryptography.OidCollection {new("1.3.6.1.5.5.7.3.1")},false));
            using var issued=lr.Create(root,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(12),RandomNumberGenerator.GetBytes(16));leaf=issued.CopyWithPrivateKey(leafKey);
        }
        listener.Start();accept=Accept();
    }
    public WebmaniaClientOptions Options(TimeSpan? timeout=null,long? maxBytes=null)=>new(){LoopbackTestEndpoint=new Uri($"{(tls?"https://localhost":"http://127.0.0.1")}:{Port}/"),LoopbackTestRootCertificate=root,Timeout=timeout??TimeSpan.FromSeconds(2),MaximumResponseBytes=maxBytes??16*1024*1024};
    async Task Accept()
    {
        try { while(!stop.IsCancellationRequested) {var c=await listener.AcceptTcpClientAsync(stop.Token);lock(connections)connections.Add(Handle(c,Interlocked.Increment(ref connectionCount)));} }
        catch(OperationCanceledException){}catch(ObjectDisposedException){}
    }
    async Task Handle(TcpClient client,int connectionId)
    {
        using(client)
        try
        {
            await using Stream stream=tls?new SslStream(client.GetStream(),false):client.GetStream();
            if(tls)await ((SslStream)stream).AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=leaf,EnabledSslProtocols=SslProtocols.Tls12|SslProtocols.Tls13},stop.Token);
            while(!stop.IsCancellationRequested)
            {
            var header=new List<byte>();var one=new byte[1];
            while(header.Count<65536)
            {if(await stream.ReadAsync(one,stop.Token)==0)return;header.Add(one[0]);if(header.Count>=4 && header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
            var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var first=lines[0].Split(' ');var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var line in lines.Skip(1)){var i=line.IndexOf(':');if(i>0)headers[line[..i].ToLowerInvariant()]=line[(i+1)..].Trim();}
            var len=headers.TryGetValue("content-length",out var length)?int.Parse(length):0;var bytes=new byte[len];await stream.ReadExactlyAsync(bytes,stop.Token);
            var request=new Request(first[0],first[1],headers,Encoding.UTF8.GetString(bytes),connectionId);Requests.Enqueue(request);var reply=DynamicReply?.Invoke(request)??Response;
            if(reply.DisconnectBeforeResponse)return;
            var sb=new StringBuilder($"HTTP/1.1 {reply.Status} Synthetic\r\nContent-Type: {reply.Media}\r\n");if(!reply.OmitContentLength)sb.Append($"Content-Length: {reply.Body.Length}\r\n");sb.Append($"Connection: {(reply.KeepAlive?"keep-alive":"close")}\r\n");foreach(var h in reply.Headers.Concat(reply.HeaderLines))sb.Append($"{h.Key}: {h.Value}\r\n");sb.Append("\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()),stop.Token);await stream.FlushAsync(stop.Token);
            if(reply.DelayBody>TimeSpan.Zero)await Task.Delay(reply.DelayBody,stop.Token);
            await stream.WriteAsync(reply.Body,stop.Token);await stream.FlushAsync(stop.Token);
            if(!reply.KeepAlive||reply.CloseSilentlyAfterReply)return;
            }
        }
        catch(Exception e) when(e is IOException or OperationCanceledException or AuthenticationException or SocketException){}
    }
    public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await accept;Task[] tasks;lock(connections)tasks=connections.ToArray();await Task.WhenAll(tasks);leaf?.Dispose();root?.Dispose();stop.Dispose();}
}
