using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace Webmania;
internal static class ZipStructure
{
    private const int MaximumEntries=10000, MaximumExpanded=64*1024*1024;
    private sealed record Member(string Name,int Start,int Compressed,int Expanded,uint Crc,ushort Method,bool Encrypted);
    private sealed class ExactDeflateInput(Stream inner) : Stream
    {
        public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,Math.Min(1,count));
        public override int Read(Span<byte> buffer)=>inner.Read(buffer[..Math.Min(1,buffer.Length)]);
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>throw new NotSupportedException();}
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    private static ushort U16(byte[] b,int at) { if(at<0||at>b.Length-2)throw new InvalidDataException();return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)); }
    private static uint U32(byte[] b,int at) { if(at<0||at>b.Length-4)throw new InvalidDataException();return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)); }
    // A classic 32-bit field beyond the bounded profile is structural corruption, never an overflow.
    private static int Size(uint n)=>n<=int.MaxValue?(int)n:throw new InvalidDataException();
    internal static FileReadiness Inspect(byte[] b,bool spreadsheet,CancellationToken token)
    {
        var eof=-1;
        for(var i=b.Length-22;i>=Math.Max(0,b.Length-65557);i--)if(U32(b,i)==0x06054b50&&i+22+U16(b,i+20)==b.Length){eof=i;break;}
        if(eof<0)throw new InvalidDataException();
        // ZIP64 is outside the bounded classic profile: its markers (or a ZIP64 locator before the
        // classic end record) are reported as Unsupported before any size is converted.
        if((eof>=20&&U32(b,eof-20)==0x07064b50)||U16(b,eof+4)==0xFFFF||U16(b,eof+6)==0xFFFF||U16(b,eof+8)==0xFFFF||U16(b,eof+10)==0xFFFF||U32(b,eof+12)==0xFFFFFFFF||U32(b,eof+16)==0xFFFFFFFF)return FileReadiness.Unsupported;
        if(U16(b,eof+4)!=0||U16(b,eof+6)!=0||U16(b,eof+8)!=U16(b,eof+10))throw new InvalidDataException();
        var count=U16(b,eof+10);var cd=Size(U32(b,eof+16));var cdSize=Size(U32(b,eof+12));
        if(count>MaximumEntries||cd<0||cdSize<0||(long)cd+cdSize!=eof)throw new InvalidDataException();
        var members=new List<Member>();var spans=new List<(int Start,int End)>();var names=new HashSet<string>(StringComparer.Ordinal);var total=0L;var p=cd;var unsupportedFlags=false;
        for(var i=0;i<count;i++)
        {
            token.ThrowIfCancellationRequested();
            if(U32(b,p)!=0x02014b50)throw new InvalidDataException();
            // Per-entry ZIP64 markers (sizes, local offset, start disk) are Unsupported, checked on the raw fields.
            if(U32(b,p+20)==0xFFFFFFFF||U32(b,p+24)==0xFFFFFFFF||U32(b,p+42)==0xFFFFFFFF||U16(b,p+34)==0xFFFF)return FileReadiness.Unsupported;
            if(U16(b,p+34)!=0)throw new InvalidDataException();
            var flags=U16(b,p+8);var method=U16(b,p+10);var crc=U32(b,p+16);var compressed=Size(U32(b,p+20));var expanded=Size(U32(b,p+24));var local=Size(U32(b,p+42));
            var nameLength=U16(b,p+28);var extra=U16(b,p+30);var comment=U16(b,p+32);var next=checked(p+46+nameLength+extra+comment);
            if(next>eof||nameLength==0||U32(b,local)!=0x04034b50||U16(b,local+6)!=flags||U16(b,local+8)!=method||U16(b,local+26)!=nameLength)throw new InvalidDataException();
            var localName=checked(local+30);var data=checked(localName+nameLength+U16(b,local+28));
            if(data>cd||!b.AsSpan(p+46,nameLength).SequenceEqual(b.AsSpan(localName,nameLength)))throw new InvalidDataException();
            var name=new UTF8Encoding(false,true).GetString(b,p+46,nameLength);
            if(!names.Add(name)||name.Contains('\0'))throw new InvalidDataException();
            unsupportedFlags |= (flags & ~0x084f) != 0 || method == 0 && (flags & 6) != 0 || (flags & 0x40) != 0 && (flags & 1) == 0;
            var localCrc=U32(b,local+14);var localCompressed=U32(b,local+18);var localExpanded=U32(b,local+22);var end=checked(data+compressed);
            if((flags&8)==0)
            {if(localCrc!=crc||localCompressed!=compressed||localExpanded!=expanded)throw new InvalidDataException();}
            else
            {
                if(localCrc!=0&&localCrc!=crc||localCompressed!=0&&localCompressed!=compressed||localExpanded!=0&&localExpanded!=expanded)throw new InvalidDataException();
                if(U32(b,end)==0x08074b50)end+=4;
                if(U32(b,end)!=crc||U32(b,end+4)!=compressed||U32(b,end+8)!=expanded)throw new InvalidDataException();end=checked(end+12);
            }
            if(end>cd||local<0)throw new InvalidDataException();
            total+=expanded;if(total>MaximumExpanded)throw new InvalidDataException();
            spans.Add((local,end));members.Add(new(name,data,compressed,expanded,crc,method,(flags&0x41)!=0));p=next;
        }
        if(p!=eof||count==0&&cd!=0)throw new InvalidDataException();
        spans.Sort((a,z)=>a.Start.CompareTo(z.Start));for(var i=1;i<spans.Count;i++)if(spans[i-1].End>spans[i].Start)throw new InvalidDataException();
        if(unsupportedFlags)return FileReadiness.Unsupported;
        if(members.Any(e=>e.Encrypted))return FileReadiness.Protected;
        if(members.Any(e=>e.Method is not (0 or 8)))return FileReadiness.Unsupported;
        // Validate every member, not only metadata or the first relationship part.
        var dataParts=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var member in members)
        {
            token.ThrowIfCancellationRequested();
            using var compressed=new MemoryStream(b,member.Start,member.Compressed,false);
            using Stream input=member.Method==0?compressed:new DeflateStream(new ExactDeflateInput(compressed),CompressionMode.Decompress);
            using var output=new MemoryStream();var buffer=new byte[8192];var crc=0xffffffffu;
            while(true)
            {
                token.ThrowIfCancellationRequested();var n=input.Read(buffer,0,buffer.Length);if(n==0)break;
                if(output.Length+n>member.Expanded||output.Length+n>MaximumExpanded)throw new InvalidDataException();
                for(var i=0;i<n;i++){crc^=buffer[i];for(var bit=0;bit<8;bit++)crc=(crc&1)!=0?0xedb88320^(crc>>1):crc>>1;}
                output.Write(buffer,0,n);
            }
            if(member.Method==8&&compressed.Position!=member.Compressed||output.Length!=member.Expanded||(crc^0xffffffff)!=member.Crc||member.Method==0&&member.Compressed!=member.Expanded)throw new InvalidDataException();
            if(spreadsheet)dataParts.Add(member.Name,output.ToArray());
        }
        if(spreadsheet)InspectWorkbook(dataParts,token);
        return FileReadiness.Ready;
    }
    internal static XDocument Xml(byte[] b,CancellationToken token)
    {
        using var input=new MemoryStream(b,false);
        using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumExpanded});
        // Load through a depth-checking first pass so XDocument cannot recurse on unbounded input.
        while(reader.Read()){token.ThrowIfCancellationRequested();if(reader.Depth>128)throw new InvalidDataException();}
        input.Position=0;
        using var again=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumExpanded});
        return XDocument.Load(again,LoadOptions.None);
    }
    private static void InspectWorkbook(Dictionary<string,byte[]> parts,CancellationToken token)
    {
        const string ct="http://schemas.openxmlformats.org/package/2006/content-types",rel="http://schemas.openxmlformats.org/package/2006/relationships",wbType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",wsType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",relType="application/vnd.openxmlformats-package.relationships+xml";
        string[] main=["http://schemas.openxmlformats.org/spreadsheetml/2006/main","http://purl.oclc.org/ooxml/spreadsheetml/main"],office=["http://schemas.openxmlformats.org/officeDocument/2006/relationships","http://purl.oclc.org/ooxml/officeDocument/relationships"];
        bool Clean(string name)=>name.Length>0&&!name.StartsWith('/')&&!name.Any(c=>c<=32||"\\%?#:".Contains(c))&&name.Split('/').All(s=>s.Length>0&&s is not("." or ".."));
        if(parts.Keys.Any(n=>!Clean(n.TrimEnd('/')))||parts.Keys.Select(n=>n.ToUpperInvariant()).Distinct().Count()!=parts.Count)throw new InvalidDataException();
        var docs=new Dictionary<string,XDocument>(StringComparer.Ordinal);
        XElement Part(string name)
        {
            if(!docs.TryGetValue(name,out var doc)){if(!parts.TryGetValue(name,out var bytes))throw new InvalidDataException();docs[name]=doc=Xml(bytes,token);}
            return doc.Root??throw new InvalidDataException();
        }
        static string Attr(XElement e,string name)=>(string?)e.Attribute(name)??"";
        var types=Part("[Content_Types].xml");if(types.Name!=XName.Get("Types",ct))throw new InvalidDataException();
        var defaults=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var overrides=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in types.Elements())
        {
            var type=Attr(e,"ContentType");if(type.Length==0||!type.Contains('/')||type.Any(char.IsWhiteSpace)||e.HasElements)throw new InvalidDataException();
            if(e.Name==XName.Get("Default",ct)){var ext=Attr(e,"Extension");if(ext.Length==0||!ext.All(char.IsAsciiLetterOrDigit)||!defaults.TryAdd(ext,type))throw new InvalidDataException();}
            else if(e.Name==XName.Get("Override",ct)){var name=Attr(e,"PartName");if(!name.StartsWith('/')||!Clean(name[1..])||!parts.ContainsKey(name[1..])||!overrides.TryAdd(name[1..],type))throw new InvalidDataException();}
            else throw new InvalidDataException();
        }
        string? Type(string name)=>overrides.GetValueOrDefault(name)??defaults.GetValueOrDefault(name[(name.LastIndexOf('.')+1)..]);
        foreach(var name in parts.Keys.Where(n=>!n.EndsWith('/')&&n!="[Content_Types].xml"))if(Type(name)is null||name.EndsWith(".rels",StringComparison.Ordinal)&&Type(name)!=relType)throw new InvalidDataException();
        string Target(string baseName,string value)
        {
            if(value.Length==0||value.Any(c=>c<=32||"\\%?#:".Contains(c))||value.Contains("//",StringComparison.Ordinal))throw new InvalidDataException();
            var joined=value.StartsWith('/')?value[1..]:baseName+value;var stack=new List<string>();
            foreach(var item in joined.Split('/')){if(item==".")continue;if(item==".."){if(stack.Count==0)throw new InvalidDataException();stack.RemoveAt(stack.Count-1);}else stack.Add(item);}
            var name=string.Join('/',stack);if(!Clean(name)||!parts.ContainsKey(name))throw new InvalidDataException();return name;
        }
        Dictionary<string,(string Target,string Type)> Relations(string name,string baseName)
        {
            var root=Part(name);if(root.Name!=XName.Get("Relationships",rel))throw new InvalidDataException();var result=new Dictionary<string,(string,string)>(StringComparer.Ordinal);
            foreach(var e in root.Elements())
            {
                var id=Attr(e,"Id");var type=Attr(e,"Type");var mode=Attr(e,"TargetMode");
                if(e.Name!=XName.Get("Relationship",rel)||e.HasElements||mode is not("" or "Internal" or "External")||id.Length==0||id.Any(char.IsWhiteSpace)||!type.Contains(':')||type.Any(char.IsWhiteSpace))throw new InvalidDataException();
                // An External relationship (hyperlink, external link) is classified, never resolved or fetched;
                // the workbook and worksheet relationships must stay internal parts.
                if(mode=="External"){if(office.Any(ns=>type==ns+"/officeDocument"||type==ns+"/worksheet")||Attr(e,"Target").Length==0||!result.TryAdd(id,("",type)))throw new InvalidDataException();continue;}
                if(!result.TryAdd(id,(Target(baseName,Attr(e,"Target")),type)))throw new InvalidDataException();
            }
            return result;
        }
        if(Type("xl/workbook.xml")!=wbType)throw new InvalidDataException();
        var offices=Relations("_rels/.rels","").Values.Where(r=>office.Any(ns=>r.Type==ns+"/officeDocument")).ToArray();
        if(offices.Length!=1||offices[0].Target!="xl/workbook.xml")throw new InvalidDataException();
        var workbook=Part("xl/workbook.xml");var family=Array.FindIndex(main,ns=>workbook.Name==XName.Get("workbook",ns));
        if(family<0||offices[0].Type!=office[family]+"/officeDocument"||workbook.Elements(XName.Get("sheets",main[family])).Count()!=1)throw new InvalidDataException();
        var relationships=Relations("xl/_rels/workbook.xml.rels","xl/");var ids=new HashSet<uint>();var rids=new HashSet<string>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var sheets=0;
        foreach(var sheet in workbook.Descendants().Where(e=>e.Name.LocalName=="sheet"))
        {
            var rid=(string?)sheet.Attribute(XName.Get("id",office[family]))??"";var name=Attr(sheet,"name");
            if(sheet.Name!=XName.Get("sheet",main[family])||sheet.Parent?.Name!=XName.Get("sheets",main[family])||sheet.Parent.Parent!=workbook||!uint.TryParse(Attr(sheet,"sheetId"),out var id)||id==0||name.Length==0||!ids.Add(id)||!rids.Add(rid)||!names.Add(name)||!relationships.TryGetValue(rid,out var relation)||relation.Type!=office[family]+"/worksheet"||Type(relation.Target)!=wsType||Part(relation.Target).Name!=XName.Get("worksheet",main[family]))throw new InvalidDataException();sheets++;
        }
        if(sheets==0)throw new InvalidDataException();
        foreach(var name in parts.Keys)
        {
            if(Type(name)==wsType&&!main.Any(ns=>Part(name).Name==XName.Get("worksheet",ns)))throw new InvalidDataException();
            if(!name.EndsWith(".rels",StringComparison.Ordinal))continue;
            var baseName="";
            if(name!="_rels/.rels")
            {var pos=name.LastIndexOf("/_rels/",StringComparison.Ordinal);if(pos<0)throw new InvalidDataException();baseName=name[..(pos+1)];var source=baseName+name[(pos+7)..^5];if(!parts.ContainsKey(source))throw new InvalidDataException();}
            foreach(var relation in Relations(name,baseName).Values)
                for(var f=0;f<2;f++)
                {if(relation.Type==office[f]+"/worksheet"&&(Type(relation.Target)!=wsType||Part(relation.Target).Name!=XName.Get("worksheet",main[f])))throw new InvalidDataException();if(relation.Type==office[f]+"/officeDocument"&&Type(relation.Target)!=wbType)throw new InvalidDataException();}
        }
    }
}
