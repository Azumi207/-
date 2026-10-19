using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Frameflow.Core;

public static class Timecode
{
    public static double Parse(string value)
    {
        value=value.Trim().Replace(',', '.');
        if (double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds) && double.IsFinite(seconds) && seconds>=0) return seconds;
        var p=value.Split(':');
        if(p.Length!=3 || !int.TryParse(p[0],out int h) || !int.TryParse(p[1],out int m) || !double.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var s) || !double.IsFinite(s) || h<0 || m<0 || m>=60 || s<0 || s>=60) throw new FormatException("请输入 h:mm:ss.cc 或非负秒数。");
        return h*3600+m*60+s;
    }
    public static string Ass(double t) { long cs=(long)Math.Round(Math.Max(0,t)*100,MidpointRounding.AwayFromZero); return $"{cs/360000}:{cs/6000%60:00}:{cs/100%60:00}.{cs%100:00}"; }
    public static string Srt(double t) {long ms=(long)Math.Round(Math.Max(0,t)*1000);return $"{ms/3600000:00}:{ms/60000%60:00}:{ms/1000%60:00},{ms%1000:000}";}
}

public sealed class AssRow
{
    public string[] Format {get;set;}=[];
    public string[] Values {get;set;}=[];
    public string Raw {get;set;}="";
    public string[] Original {get;set;}=[];
    public bool Comment {get;set;}
    public bool OriginalComment {get;set;}
    public int Number {get;set;}
    public string Note {get;set;}="";
    public bool Reviewed {get;set;}
    public string Check {get;set;}="";
    public string Get(string name,string fallback="") {int i=Array.FindIndex(Format,k=>k.Equals(name,StringComparison.OrdinalIgnoreCase));return i>=0 && i<Values.Length?Values[i]:fallback;}
    public void Set(string name,string value) {int i=Array.FindIndex(Format,k=>k.Equals(name,StringComparison.OrdinalIgnoreCase));if(i<0)throw new InvalidOperationException($"当前事件格式没有 {name} 字段；为避免破坏文件，未写入。");Values[i]=value;}
    public double Start {get=>Timecode.Parse(Get("Start","0"));set=>Set("Start",Timecode.Ass(value));}
    public double End {get=>Timecode.Parse(Get("End","0"));set=>Set("End",Timecode.Ass(value));}
    public string StartText=>Timecode.Ass(Start);
    public string EndText=>Timecode.Ass(End);
    public string DurationText=>(End-Start).ToString("0.00",CultureInfo.InvariantCulture)+" s";
    public string Text {get=>Get("Text");set=>Set("Text",value.Replace("\r\n","\\N").Replace("\n","\\N"));}
    public string Plain=>Regex.Replace(Text,@"\{[^}]*\}","").Replace(@"\N","\n").Replace(@"\n","\n").Replace(@"\h"," ");
    public string Actor {get=>Get("Name",Get("Actor"));set=>Set(Format.Any(f=>f.Equals("Name",StringComparison.OrdinalIgnoreCase))?"Name":"Actor",value);}
    public string Style {get=>Get("Style","Default");set=>Set("Style",value);}
    public string Layer {get=>Get("Layer",Get("Marked","0")).Replace("Marked=","",StringComparison.OrdinalIgnoreCase);set{if(Format.Any(f=>f.Equals("Layer",StringComparison.OrdinalIgnoreCase)))Set("Layer",value);else Set("Marked",Get("Marked").StartsWith("Marked=",StringComparison.OrdinalIgnoreCase)?"Marked="+value:value);}}
    public string Effect {get=>Get("Effect");set=>Set("Effect",value);}
    public string Fingerprint=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize())));
    public string Serialize()=>Comment==OriginalComment&&Original.SequenceEqual(Values)?Raw:(Comment?"Comment: ":"Dialogue: ")+string.Join(',',Values);
}
public sealed class AssStyle
{
    public string[] Format {get;set;}=[]; public string[] Values {get;set;}=[]; public string[] Original {get;set;}=[];public string Raw {get;set;}="";
    public string Get(string name,string fallback="") {int i=Array.FindIndex(Format,k=>k.Equals(name,StringComparison.OrdinalIgnoreCase));return i>=0&&i<Values.Length?Values[i]:fallback;}
    public void Set(string name,string value) {int i=Array.FindIndex(Format,k=>k.Equals(name,StringComparison.OrdinalIgnoreCase));if(i<0)throw new InvalidOperationException($"样式中缺少 {name} 字段。");Values[i]=value;}
    public string Name=>Get("Name","Default");
    public double Number(string name,double fallback) => double.TryParse(Get(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&double.IsFinite(d)?d:fallback;
    public string Serialize()=>Original.SequenceEqual(Values)?Raw:"Style: "+string.Join(',',Values);
}
public sealed class Record {public string Raw {get;set;}=""; public AssRow? Row {get;set;} public AssStyle? Style {get;set;} public string Serialize()=>Row?.Serialize()??Style?.Serialize()??Raw;}

public sealed class SubtitleDocument
{
    public const string EventFormat="Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text";
    public const string StyleFormat="Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding";
    public List<Record> Records {get;}=[];public List<string> Warnings {get;}=[];
    public IEnumerable<AssRow> Rows=>Records.Where(r=>r.Row!=null).Select(r=>r.Row!);
    public IEnumerable<AssStyle> Styles=>Records.Where(r=>r.Style!=null).Select(r=>r.Style!);
    public string Newline {get;private set;}="\r\n";public bool TrailingNewline {get;private set;}=true;
    private byte[]? originalBytes;private string originalText="";
    public int PlayResX=>InfoInt("PlayResX",1920);public int PlayResY=>InfoInt("PlayResY",1080);
    public bool LegacySsa=>Records.Any(r=>r.Raw.Trim().Equals("[V4 Styles]",StringComparison.OrdinalIgnoreCase));
    int InfoInt(string key,int fallback){var line=Records.Select(r=>r.Raw).FirstOrDefault(s=>s.TrimStart().StartsWith(key+":",StringComparison.OrdinalIgnoreCase));return line!=null&&int.TryParse(line.Split(':',2)[1],out var n)&&n>0?n:fallback;}
    public static SubtitleDocument Load(string path)=>Path.GetExtension(path).Equals(".srt",StringComparison.OrdinalIgnoreCase)?FromSrt(Decode(File.ReadAllBytes(path))):ParseBytes(File.ReadAllBytes(path));
    public static string Decode(byte[] data)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if(data.Length>=3&&data[0]==239&&data[1]==187&&data[2]==191)return Encoding.UTF8.GetString(data,3,data.Length-3);
        if(data.Length>=2&&data[0]==255&&data[1]==254)return Encoding.Unicode.GetString(data,2,data.Length-2);
        if(data.Length>=2&&data[0]==254&&data[1]==255)return Encoding.BigEndianUnicode.GetString(data,2,data.Length-2);
        try{return new UTF8Encoding(false,true).GetString(data);}catch(DecoderFallbackException){return Encoding.GetEncoding(936).GetString(data);}
    }
    public static SubtitleDocument ParseBytes(byte[] bytes){var d=Parse(Decode(bytes));d.originalBytes=bytes.ToArray();d.originalText=d.Serialize();return d;}
    public static SubtitleDocument Parse(string text)
    {
        var d=new SubtitleDocument{Newline=text.Contains("\r\n")?"\r\n":"\n",TrailingNewline=text.EndsWith('\n')};
        var lines=text.Replace("\r\n","\n").Split('\n');int count=lines.Length-(d.TrailingNewline?1:0);string section="";string[] eventFormat=EventFormat.Split(',').Select(s=>s.Trim()).ToArray(),styleFormat=StyleFormat.Split(',').Select(s=>s.Trim()).ToArray();
        for(int i=0;i<count;i++)
        {
            var raw=lines[i];var trim=raw.Trim();var rec=new Record{Raw=raw};d.Records.Add(rec);
            if(trim.StartsWith('[')){section=trim;continue;}
            if(trim.StartsWith("Format:",StringComparison.OrdinalIgnoreCase)){var f=trim.Split(':',2)[1].Split(',').Select(s=>s.Trim()).ToArray();if(section.Equals("[Events]",StringComparison.OrdinalIgnoreCase))eventFormat=f;else if(section.Contains("Styles",StringComparison.OrdinalIgnoreCase))styleFormat=f;continue;}
            if(section.Equals("[Events]",StringComparison.OrdinalIgnoreCase)&&(trim.StartsWith("Dialogue:",StringComparison.OrdinalIgnoreCase)||trim.StartsWith("Comment:",StringComparison.OrdinalIgnoreCase)))
            {
                try {if(!eventFormat[^1].Equals("Text",StringComparison.OrdinalIgnoreCase))throw new FormatException("Text 不是最后一个字段");var v=raw[(raw.IndexOf(':')+1)..].TrimStart().Split(',',eventFormat.Length);if(v.Length!=eventFormat.Length)throw new FormatException("字段数不完整");var row=new AssRow{Format=eventFormat.ToArray(),Values=v,Original=v.ToArray(),Raw=raw,Comment=trim.StartsWith("Comment:",StringComparison.OrdinalIgnoreCase)};_ =row.Start;_=row.End;rec.Row=row;}
                catch(Exception ex) when(ex is FormatException or IndexOutOfRangeException){d.Warnings.Add($"第 {i+1} 行仅原样保留，无法编辑：{ex.Message}");}
            }
            else if(section.Contains("Styles",StringComparison.OrdinalIgnoreCase)&&trim.StartsWith("Style:",StringComparison.OrdinalIgnoreCase))
            {var v=raw[(raw.IndexOf(':')+1)..].TrimStart().Split(',',styleFormat.Length);if(v.Length==styleFormat.Length)rec.Style=new AssStyle{Format=styleFormat.ToArray(),Values=v,Original=v.ToArray(),Raw=raw};else d.Warnings.Add($"第 {i+1} 行样式格式异常，已原样保留。");}
        }
        foreach(var row in d.Rows)row.OriginalComment=row.Comment;d.Renumber();return d;
    }
    public static SubtitleDocument New()=>Parse("[Script Info]\r\nTitle: Frameflow\r\nScriptType: v4.00+\r\nPlayResX: 1920\r\nPlayResY: 1080\r\nWrapStyle: 0\r\nScaledBorderAndShadow: yes\r\n\r\n[V4+ Styles]\r\nFormat: "+StyleFormat+"\r\nStyle: Default,Microsoft YaHei,48,&H00FFFFFF,&H000000FF,&H0018202A,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,30,30,48,1\r\n\r\n[Events]\r\nFormat: "+EventFormat+"\r\n");
    public void Renumber(){int i=0;foreach(var row in Rows)row.Number=++i;}
    public AssRow Add(double start,double end,string text="",AssRow? after=null)
    {
        var f=(after?.Format??Rows.LastOrDefault()?.Format??EventFormat.Split(',').Select(s=>s.Trim()).ToArray()).ToArray();
        var row=new AssRow{Format=f,Values=f.Select(_=>"").ToArray(),Original=[],Raw=""};row.Start=start;row.End=end;row.Style=after?.Style??Styles.FirstOrDefault()?.Name??"Default";row.Actor=after?.Actor??"";row.Layer="0";row.Text=text;
        foreach(var name in new[]{"MarginL","MarginR","MarginV"})if(f.Contains(name))row.Set(name,"0000");
        int index=after!=null?Records.FindIndex(r=>r.Row==after):Records.FindLastIndex(r=>r.Row!=null);
        if(index<0){int header=Records.FindIndex(r=>r.Raw.Trim().Equals("[Events]",StringComparison.OrdinalIgnoreCase));if(header<0){Records.Add(new Record{Raw="[Events]"});Records.Add(new Record{Raw="Format: "+EventFormat});index=Records.Count-1;}else{index=header;while(index+1<Records.Count&&!Records[index+1].Raw.TrimStart().StartsWith('['))index++;}}
        Records.Insert(index+1,new Record{Row=row});Renumber();return row;
    }
    public void Remove(AssRow row){Records.RemoveAll(r=>r.Row==row);Renumber();}
    public void AddStyle(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny([',','\r','\n'])>=0||Styles.Any(s=>s.Name==name))throw new FormatException("样式名称不能为空、重复或含逗号/换行。");
        if(LegacySsa)throw new InvalidOperationException("旧 SSA 样式暂只保留；请先转换为 ASS 后新增样式。");
        var template=Styles.FirstOrDefault()??New().Styles.First();var s=new AssStyle{Format=template.Format.ToArray(),Values=template.Values.ToArray(),Original=[],Raw=""};s.Set("Name",name);
        int index=Records.FindLastIndex(r=>r.Style!=null);if(index<0){int before=Records.FindIndex(r=>r.Raw.Trim().Equals("[Events]",StringComparison.OrdinalIgnoreCase));if(before<0)before=Records.Count;Records.Insert(before,new Record{Raw="[V4+ Styles]"});Records.Insert(before+1,new Record{Raw="Format: "+StyleFormat});index=before+1;}
        var previousFormat=Records[index].Style?.Format??s.Format;
        Records.InsertRange(index+1,[new Record{Raw="Format: "+string.Join(", ",s.Format)},new Record{Style=s},new Record{Raw="Format: "+string.Join(", ",previousFormat)}]);
    }
    public string Serialize()=>string.Join(Newline,Records.Select(r=>r.Serialize()))+(TrailingNewline?Newline:"");
    public byte[] Bytes(){var text=Serialize();return originalBytes!=null&&text==originalText?originalBytes.ToArray():new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();}
    public string ExportSrt()=>string.Join("\r\n",Rows.Where(r=>!r.Comment).Select((r,i)=>$"{i+1}\r\n{Timecode.Srt(r.Start)} --> {Timecode.Srt(r.End)}\r\n{r.Plain.Replace("\n","\r\n")}\r\n"));
    public static SubtitleDocument FromSrt(string text){var d=New();foreach(var block in Regex.Split(text.Trim(),@"\r?\n\s*\r?\n")){var ls=block.Replace("\r","").Split('\n');int i=Array.FindIndex(ls,l=>l.Contains("-->"));if(i<0)continue;var times=ls[i].Split("-->");try{d.Add(Timecode.Parse(times[0]),Timecode.Parse(times[1].Trim().Split(' ')[0]),string.Join(@"\N",ls.Skip(i+1)));}catch(FormatException){d.Warnings.Add("无法解析的 SRT 段："+ls[i]);}}return d;}
    public AssStyle? FindStyle(AssRow row)=>Styles.FirstOrDefault(s=>s.Name==row.Style)??Styles.FirstOrDefault();
    public static void AtomicSave(string path,byte[] bytes,bool backup=true){var temp=path+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temp,bytes);if(File.Exists(path)&&backup)File.Copy(path,path+".bak-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
