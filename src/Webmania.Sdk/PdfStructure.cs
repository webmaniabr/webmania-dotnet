using System.Globalization;
using System.Text;
namespace Webmania;

// Bounded classic-xref recognition only. Streams are skipped by declared length;
// strings/comments are parsed as such, never searched for structural keywords.
internal static class PdfStructure
{
    private sealed record Name(string Text);
    private sealed record Ref(int Id, int Generation);
    private sealed record Str(int Length);
    private sealed record Entry(int Offset, int Generation, bool Live);
    private sealed class Parser(byte[] raw, CancellationToken token)
    {
        internal int At, Budget = 500000;
        internal static bool White(byte c) => c is 0 or 9 or 10 or 12 or 13 or 32;
        private static bool Delimiter(byte c) => White(c) || "()<>[]{}/%".Contains((char)c);
        internal void Space()
        {
            while (At < raw.Length)
            {
                if (White(raw[At])) { At++; continue; }
                if (raw[At] != '%') break;
                while (At < raw.Length && raw[At] is not (10 or 13)) At++;
            }
        }
        internal bool Word(string word)
        {
            Space();
            if (At + word.Length > raw.Length || !raw.AsSpan(At, word.Length).SequenceEqual(Encoding.ASCII.GetBytes(word)) || At + word.Length < raw.Length && !Delimiter(raw[At + word.Length])) return false;
            At += word.Length; return true;
        }
        internal int UInt()
        {
            Space(); var start = At;
            while (At < raw.Length && raw[At] is >= 48 and <= 57) At++;
            if (At == start || At < raw.Length && !Delimiter(raw[At]) || !int.TryParse(Encoding.ASCII.GetString(raw, start, At-start), NumberStyles.None, CultureInfo.InvariantCulture, out var result)) throw new InvalidDataException();
            return result;
        }
        internal object? Value(int depth = 0)
        {
            token.ThrowIfCancellationRequested(); if (depth > 64 || --Budget < 0) throw new InvalidDataException(); Space(); if (At >= raw.Length) throw new InvalidDataException();
            if (raw[At] == '<' && At + 1 < raw.Length && raw[At+1] == '<')
            {
                At += 2; var dictionary = new Dictionary<string,object?>(StringComparer.Ordinal);
                while (true)
                {
                    Space(); if (At+1 < raw.Length && raw[At] == '>' && raw[At+1] == '>') { At+=2; return dictionary; }
                    if (Value(depth+1) is not Name name || !dictionary.TryAdd(name.Text, Value(depth+1))) throw new InvalidDataException();
                }
            }
            if (raw[At] == '[')
            {
                At++; var values = new List<object?>();
                while (true) { Space(); if (At >= raw.Length) throw new InvalidDataException(); if (raw[At] == ']') { At++; return values; } values.Add(Value(depth+1)); }
            }
            if (raw[At] == '/')
            {
                At++; var name = new List<byte>();
                while (At < raw.Length && !Delimiter(raw[At])) { var c = raw[At++]; if (c == '#') { if (At+2 > raw.Length) throw new InvalidDataException(); c = Convert.ToByte(Encoding.ASCII.GetString(raw,At,2),16); At+=2; } name.Add(c); }
                return new Name(Encoding.Latin1.GetString(name.ToArray()));
            }
            if (raw[At] == '(')
            {
                At++; var nesting = 1; var count = 0;
                while (At < raw.Length)
                {
                    var c = raw[At++];
                    if (c == '\\')
                    {
                        if (At >= raw.Length) throw new InvalidDataException(); c = raw[At++];
                        if (c == 13 && At < raw.Length && raw[At] == 10) At++;
                        if (c is >= 48 and <= 55) for (var i=0; i<2 && At < raw.Length && raw[At] is >= 48 and <= 55; i++) At++;
                        if (c is not (10 or 13)) count++; continue;
                    }
                    if (c == '(') { if (++nesting > 64) throw new InvalidDataException(); }
                    if (c == ')') nesting--;
                    if (c == ')' && nesting == 0) return new Str(count);
                    count++;
                }
                throw new InvalidDataException();
            }
            if (raw[At] == '<')
            {
                At++; var count = 0;
                while (At < raw.Length) { var c = raw[At++]; if (c == '>') return new Str((count+1)/2); if (White(c)) continue; if (!Uri.IsHexDigit((char)c)) throw new InvalidDataException(); count++; }
                throw new InvalidDataException();
            }
            var start = At;
            if (raw[At] is >= 48 and <= 57)
            {
                try { var id=UInt(); var generation=UInt(); if (id>0 && generation<=65535 && Word("R")) return new Ref(id,generation); } catch (InvalidDataException) { }
                At = start;
            }
            while (At < raw.Length && !Delimiter(raw[At])) At++;
            var word = Encoding.ASCII.GetString(raw,start,At-start);
            if (word is "true" or "false" or "null") return null;
            if (word.Length == 0 || word.Count(c=>c=='.')>1 || !word.Any(char.IsAsciiDigit) || word.TrimStart('+','-').Any(c=>!char.IsAsciiDigit(c)&&c!='.')) throw new InvalidDataException();
            if (!word.Contains('.')) return long.Parse(word, CultureInfo.InvariantCulture);
            var number = double.Parse(word, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number)) throw new InvalidDataException();
            return number;
        }
    }
    internal static FileReadiness Inspect(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length < 9 || !bytes.AsSpan().StartsWith("%PDF-"u8) || bytes[8] is not (10 or 13) || !new[]{"1.0","1.1","1.2","1.3","1.4","1.5","1.6","1.7","2.0"}.Contains(Encoding.ASCII.GetString(bytes,5,3))) throw new InvalidDataException();
        var end=bytes.Length; while(end>0&&Parser.White(bytes[end-1]))end--;
        if(end<5||!bytes.AsSpan(end-5,5).SequenceEqual("%%EOF"u8))throw new InvalidDataException();
        var n=end-5;while(n>0&&Parser.White(bytes[n-1]))n--;var numberEnd=n;while(n>0&&bytes[n-1] is >=48 and <=57)n--;
        if(n==numberEnd||!int.TryParse(Encoding.ASCII.GetString(bytes,n,numberEnd-n),out var xref))throw new InvalidDataException();
        var keyword=n;while(keyword>0&&Parser.White(bytes[keyword-1]))keyword--;var tail=keyword-9;
        if(tail<9||!bytes.AsSpan(tail,9).SequenceEqual("startxref"u8)||!Parser.White(bytes[tail-1])||xref<9||xref>=tail)throw new InvalidDataException();
        var p=new Parser(bytes,token){At=xref};if(!bytes.AsSpan(xref).StartsWith("xref"u8)||!p.Word("xref"))throw new InvalidDataException();
        var entries=new Dictionary<int,Entry>();
        while(!p.Word("trailer"))
        {
            var first=p.UInt();var count=p.UInt();if(count<1||first>100000-count||entries.Count>100000-count)throw new InvalidDataException();
            for(var id=first;id<first+count;id++)
            {
                var offset=p.UInt();var generation=p.UInt();var live=p.Word("n");if(!live&&!p.Word("f")||generation>65535||live&&(id==0||generation==65535||offset<9||offset>=xref)||!entries.TryAdd(id,new(offset,generation,live)))throw new InvalidDataException();
            }
        }
        if(p.Value() is not Dictionary<string,object?> trailer || trailer.GetValueOrDefault("Size") is not long size || size<1||size>100000||entries.Any(e=>e.Key>=size||!e.Value.Live&&e.Value.Offset>=size)||!entries.TryGetValue(0,out var zero)||zero.Live||zero.Generation!=65535)throw new InvalidDataException();
        // Newer xref streams, hybrid or incremental updates have a separate honest unsupported state.
        if(trailer.ContainsKey("Prev")||trailer.ContainsKey("XRefStm"))return FileReadiness.Unsupported;
        p.Space();if(p.At!=tail)throw new InvalidDataException();
        var objects=new Dictionary<int,object?>();p.At=0;
        while(true)
        {
            p.Space();if(p.At==xref)break;if(p.At>=xref)throw new InvalidDataException();var start=p.At;var id=p.UInt();var generation=p.UInt();if(id<1||generation>=65535||!p.Word("obj"))throw new InvalidDataException();
            var value=p.Value();var isStream=p.Word("stream");
            if(value is Dictionary<string,object?> dictionary && (dictionary.GetValueOrDefault("Type") is Name type && type.Text is "XRef" or "ObjStm" || dictionary.ContainsKey("Linearized")))return FileReadiness.Unsupported;
            if(isStream)
            {
                if(value is not Dictionary<string,object?> dictionary2)throw new InvalidDataException();
                var length=dictionary2.GetValueOrDefault("Length");
                if(length is Ref r)
                {
                    if(!entries.TryGetValue(r.Id,out var le)||!le.Live||le.Generation!=r.Generation)throw new InvalidDataException();
                    var saved=p.At;p.At=le.Offset;if(p.UInt()!=r.Id||p.UInt()!=r.Generation||!p.Word("obj"))throw new InvalidDataException();length=p.Value();if(!p.Word("endobj"))throw new InvalidDataException();p.At=saved;
                }
                if(length is not long len||len<0||len>bytes.Length)throw new InvalidDataException();
                if(p.At<bytes.Length&&bytes[p.At]==13){p.At++;if(p.At<bytes.Length&&bytes[p.At]==10)p.At++;}else if(p.At<bytes.Length&&bytes[p.At]==10)p.At++;else throw new InvalidDataException();
                p.At=checked(p.At+(int)len);if(!p.Word("endstream"))throw new InvalidDataException();value=null;
            }
            if(!p.Word("endobj"))throw new InvalidDataException();
            if(entries.TryGetValue(id,out var e)&&e.Live&&e.Offset==start&&(e.Generation!=generation||!objects.TryAdd(id,value)))throw new InvalidDataException();
        }
        if(entries.Any(e=>e.Value.Live&&!objects.ContainsKey(e.Key)))throw new InvalidDataException();
        object? Resolve(object? value)
        {
            if(value is not Ref r)return value;
            if(!entries.TryGetValue(r.Id,out var e)||!e.Live||e.Generation!=r.Generation||!objects.TryGetValue(r.Id,out value))throw new InvalidDataException();return value;
        }
        Dictionary<string,object?> Dict(object? value)=>Resolve(value) as Dictionary<string,object?>??throw new InvalidDataException();
        var root=Dict(trailer.GetValueOrDefault("Root"));if(root.GetValueOrDefault("Type") is not Name {Text:"Catalog"}||root.GetValueOrDefault("Pages") is not Ref pages)throw new InvalidDataException();
        var visited=new HashSet<Ref>();
        int Count(Ref node,Ref? parent,object? box,object? resources,int depth)
        {
            if(depth>64||!visited.Add(node))throw new InvalidDataException();var d=Dict(node);
            if(parent is null ? d.ContainsKey("Parent") : !Equals(d.GetValueOrDefault("Parent"),parent))throw new InvalidDataException();
            box=d.GetValueOrDefault("MediaBox")??box;resources=d.GetValueOrDefault("Resources")??resources;
            if(d.GetValueOrDefault("Type") is Name {Text:"Page"})
            {
                if(parent is null||Resolve(box) is not List<object?> rect||rect.Count!=4||rect.Any(v=>Resolve(v) is not (long or double))||Resolve(resources) is not Dictionary<string,object?>)throw new InvalidDataException();return 1;
            }
            if(d.GetValueOrDefault("Type") is not Name {Text:"Pages"}||d.GetValueOrDefault("Kids") is not List<object?> kids)throw new InvalidDataException();
            var total=0;foreach(var kid in kids)total=checked(total+Count(kid as Ref??throw new InvalidDataException(),node,box,resources,depth+1));
            if(d.GetValueOrDefault("Count") is not long declared||declared!=total)throw new InvalidDataException();return total;
        }
        Count(pages,null,null,null,0);
        if(!trailer.ContainsKey("Encrypt"))return FileReadiness.Ready;
        var encrypt=Dict(trailer["Encrypt"]);
        if(encrypt.GetValueOrDefault("Filter") is not Name {Text:"Standard"}||encrypt.GetValueOrDefault("V") is not long v||encrypt.GetValueOrDefault("R") is not long revision||!((v==1&&revision==2)||(v==2&&revision==3)||(v==4&&revision==4)||(v==5&&revision is 5 or 6))||encrypt.GetValueOrDefault("P") is not long||encrypt.GetValueOrDefault("O") is not Str o||encrypt.GetValueOrDefault("U") is not Str u||o.Length<(revision>=5?48:32)||u.Length<(revision>=5?48:32))throw new InvalidDataException();
        return FileReadiness.Protected;
    }
}
