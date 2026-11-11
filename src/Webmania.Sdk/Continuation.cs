using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Webmania;

/// <summary>In-memory opaque continuation, bound to the exact serialized original filters and page size. Ordinary JSON never exports its retained data.</summary>
[JsonConverter(typeof(MonitorContinuationConverterFactory))]
public sealed class MonitorContinuation<T>
{
    internal string Operation { get; }
    internal string Binding { get; }
    internal JsonElement? Buffered { get; }
    internal BigInteger PageValue { get; }
    internal BigInteger? PerPage { get; }
    public JsonNumber Page => new(PageValue.ToString(CultureInfo.InvariantCulture));
    public int ItemIndex { get; }
    internal MonitorContinuation(string operation,string binding,BigInteger page,int index,BigInteger? perPage,JsonElement? buffered)
    { Operation=operation;Binding=binding;PageValue=page;ItemIndex=index;PerPage=perPage;Buffered=buffered; }
    public override string ToString()=>"MonitorContinuation [redacted]";
}
public sealed class MonitorBatch<T>
{
    public IReadOnlyList<T> Items { get; }
    public MonitorContinuation<T>? Continuation { get; }
    internal MonitorBatch(List<T> items,MonitorContinuation<T>? continuation){Items=items.AsReadOnly();Continuation=continuation;}
    public override string ToString()=>$"MonitorBatch({Items.Count} items, more={Continuation is not null}) [redacted]";
}
public sealed class MonitorContinuationConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type)=>type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(MonitorContinuation<>);
    public override JsonConverter CreateConverter(Type type,JsonSerializerOptions options)=>(JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(type.GetGenericArguments()))!;
    private sealed class Converter<T>:JsonConverter<MonitorContinuation<T>>
    {
        public override MonitorContinuation<T> Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>throw new JsonException("Continuations cannot be restored from diagnostic JSON.");
        public override void Write(Utf8JsonWriter writer,MonitorContinuation<T> value,JsonSerializerOptions options){writer.WriteStartObject();writer.WriteString("value","[redacted]");writer.WriteEndObject();}
    }
}
public sealed partial class WebmaniaClient
{
    public Task<MonitorBatch<Api2MonitorReadsMonitorCompany>> ReadMonitorCompaniesAsync(MonitorListCompaniesParameters parameters,int maximumItems,MonitorContinuation<Api2MonitorReadsMonitorCompany>? continuation=null,int maximumPages=100,CancellationToken cancellationToken=default)=>
        ReadMonitorBatchAsync<Api2MonitorReadsMonitorCompany>("monitorListCompanies",parameters,maximumItems,continuation,maximumPages,cancellationToken);
    public Task<MonitorBatch<Api2MonitorReadsMonitorMunicipalityRegistration>> ReadMonitorCompanyMunicipalitiesAsync(MonitorListCompanyMunicipalitiesParameters parameters,int maximumItems,MonitorContinuation<Api2MonitorReadsMonitorMunicipalityRegistration>? continuation=null,int maximumPages=100,CancellationToken cancellationToken=default)=>
        ReadMonitorBatchAsync<Api2MonitorReadsMonitorMunicipalityRegistration>("monitorListCompanyMunicipalities",parameters,maximumItems,continuation,maximumPages,cancellationToken);
    public Task<MonitorBatch<Api2MonitorDocumentsMonitorReport>> ReadMonitorReportsAsync(MonitorListReportsParameters parameters,int maximumItems,MonitorContinuation<Api2MonitorDocumentsMonitorReport>? continuation=null,int maximumPages=100,CancellationToken cancellationToken=default)=>
        ReadMonitorBatchAsync<Api2MonitorDocumentsMonitorReport>("monitorListReports",parameters,maximumItems,continuation,maximumPages,cancellationToken);
    private async Task<MonitorBatch<T>> ReadMonitorBatchAsync<T>(string id,object parameters,int maximumItems,MonitorContinuation<T>? continuation,int maximumPages,CancellationToken token)
    {
        if(maximumItems is <1 or >1000000||maximumPages is <1 or >10000)throw WebmaniaException.Local(id,"validation","Positive bounded item and page limits are required.");
        token.ThrowIfCancellationRequested();JsonElement frozen;
        try{frozen=WebmaniaJson.ToElement(parameters);}catch(Exception e)when(e is JsonException or ArgumentException or InvalidOperationException or NotSupportedException){throw WebmaniaException.Local(id,"validation","Pagination serialization failed.");}
        Contracts.Validate(Contracts.Operations[id],frozen,token);
        var binding=frozen.GetRawText();var page=BigInteger.One;BigInteger? perPage=null;
        if(frozen.TryGetProperty("page",out var initial)&&!TryPage(initial,out page))throw WebmaniaException.Local(id,"validation","Unsupported exact initial page.");
        if(frozen.TryGetProperty("per_page",out var requested)){if(!TryPage(requested,out var size))throw WebmaniaException.Local(id,"validation","Unsupported page size.");perPage=size;}
        if(continuation is not null&&(continuation.Operation!=id||continuation.Binding!=binding||perPage.HasValue&&perPage!=continuation.PerPage))throw WebmaniaException.Local(id,"validation","Continuation inputs differ from the original serialized filters or page size. No request was sent.");
        page=continuation?.PageValue??page;perPage=continuation?.PerPage??perPage;var buffered=continuation?.Buffered;var skip=continuation?.ItemIndex??0;var output=new List<T>();var requests=0;
        while(true)
        {
            token.ThrowIfCancellationRequested();JsonElement json;
            if(buffered is {} saved){json=saved;buffered=null;}
            else
            {
                if(requests==maximumPages)throw WebmaniaException.Local(id,"page_limit","Pagination request limit reached.");
                var input=frozen.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value);
                if(continuation is not null||requests>0)input["page"]=WebmaniaJson.ToElement(new JsonNumber(page.ToString(CultureInfo.InvariantCulture)));
                var response=await ExecuteAsync<JsonElement>(id,input,token).ConfigureAwait(false);json=response.Json;requests++;
            }
            var meta=json.GetProperty("meta");
            if(!TryPage(meta.GetProperty("current_page"),out var actual)||actual!=page||!TryPage(meta.GetProperty("last_page"),out var last)||!TryPage(meta.GetProperty("per_page"),out var size2)||perPage.HasValue&&perPage!=size2)throw WebmaniaException.Local(id,"protocol","Pagination metadata changed or failed to advance.");
            perPage??=size2;var rows=json.GetProperty("data");var count=rows.GetArrayLength();
            if(count>size2||last<page&&count!=0||skip>count)throw WebmaniaException.Local(id,"protocol","Pagination data is inconsistent.");
            if(last<page)return new(output,null);
            for(var index=skip;index<count;index++)
            {
                token.ThrowIfCancellationRequested();output.Add(WebmaniaJson.FromElement<T>(rows[index])!);
                if(output.Count==maximumItems)
                {
                    MonitorContinuation<T>? next=index+1<count?new(id,binding,page,index+1,perPage,json.Clone()):page<last?new(id,binding,page+1,0,perPage,null):null;
                    return new(output,next);
                }
            }
            if(page==last)return new(output,null);
            page++;skip=0;
        }
    }
}
