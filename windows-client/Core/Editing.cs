using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;

namespace Frameflow.Core;

public static class AssTags
{
    public static void Set(AssRow row,string name,string value)
    {
        if(name=="pos" && Regex.IsMatch(row.Text,@"\\move\("))throw new InvalidOperationException("此句含动态 \\move；本版保留原标签，请在文本中明确修改动态定位。");
        string pattern=name is "pos" or "move"?$@"\\{name}\([^)]*\)":name is "c" or "1c"?@"\\(?:1c|c)&H[0-9A-Fa-f]+&?":name=="fn"?@"\\fn[^\\}]*":$@"\\{name}(?![A-Za-z])[^\\}}]*";
        var tag="\\"+name+value;
        bool replaced=false;row.Text=Regex.Replace(row.Text,@"\{[^}]*\}",m=>Regex.Replace(m.Value,pattern,_=>{replaced=true;return tag;}));
        if(!replaced)row.Text="{"+tag+"}"+row.Text;
    }
    public static string Get(AssRow row,string name,string fallback)
    {
        var tags=string.Concat(Regex.Matches(row.Text,@"\{[^}]*\}").Select(m=>m.Value));
        string pattern=name=="pos"?@"\\pos\(([^)]*)\)":name is "c" or "1c"?@"\\(?:1c|c)(&H[0-9A-Fa-f]+&?)":name=="fn"?@"\\fn([^\\}]+)":$@"\\{name}(?![A-Za-z])([^\\}}]+)";
        var matches=Regex.Matches(tags,pattern);return matches.Count>0?matches[^1].Groups[1].Value:fallback;
    }
    public static double Number(AssRow row,string name,double fallback)=>double.TryParse(Get(row,name,""),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&double.IsFinite(d)?d:fallback;
    public static string HexToAss(string rgb){rgb=rgb.Trim().TrimStart('#');if(!Regex.IsMatch(rgb,"^[0-9A-Fa-f]{6}$"))throw new FormatException("颜色应为 #RRGGBB。");return "&H"+rgb[4..6]+rgb[2..4]+rgb[0..2]+"&";}
    public static string AssToHex(string ass){var h=ass.Replace("&H","",StringComparison.OrdinalIgnoreCase).TrimEnd('&').PadLeft(6,'0');if(h.Length>6)h=h[^6..];return "#"+h[4..6]+h[2..4]+h[0..2];}
}
public sealed record QualityOptions(double GapMs=120,double ShortMs=500,double MaxCps=20,string ExcludedStyles="Screen,Sign,Title,特效,屏幕字");
public sealed record Issue(string Kind,AssRow Row,AssRow? Next,string Detail)
{
    public override string ToString()=>$"#{Row.Number} · {Kind}　{Detail}";
}
public static class Quality
{
    public static List<Issue> Scan(SubtitleDocument d,QualityOptions? options=null)
    {
        var o=options??new();var issues=new List<Issue>();var excluded=o.ExcludedStyles.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        foreach(var r in d.Rows)r.Check=r.Reviewed?"已校对":"待校对";
        var rows=d.Rows.Where(r=>!r.Comment&&!excluded.Contains(r.Style,StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach(var r in rows){var duration=r.End-r.Start;if(duration<=0)issues.Add(new("时间无效",r,null,"结束必须晚于开始"));else{if(duration*1000<o.ShortMs)issues.Add(new("显示过短",r,null,$"{duration*1000:0} ms"));double cps=Regex.Replace(r.Plain,@"\s","").Length/duration;if(cps>o.MaxCps)issues.Add(new("阅读过快",r,null,$"{cps:0.0} 字/秒"));}}
        foreach(var g in rows.GroupBy(r=>(r.Style,r.Layer)))
        {var sorted=g.OrderBy(r=>r.Start).ToArray();for(int i=0;i<sorted.Length-1;i++){var a=sorted[i];var b=sorted[i+1];var gap=b.Start-a.End;if(gap>0&&gap*1000<o.GapMs)issues.Add(new("疑似闪轴",a,b,$"→ #{b.Number} 间隙 {gap*1000:0} ms"));else if(gap<0)issues.Add(new("疑似重叠",a,b,$"与 #{b.Number} 重叠 {-gap*1000:0} ms；请人工判断"));}}
        foreach(var group in issues.GroupBy(i=>i.Row))group.Key.Check=string.Join(" / ",group.Select(i=>i.Kind));return issues.OrderBy(i=>i.Row.Start).ToList();
    }
    public static void FixGap(Issue issue){if(issue.Kind!="疑似闪轴"||issue.Next==null)throw new InvalidOperationException("此问题不能自动修复。");issue.Row.End=issue.Next.Start;issue.Row.Reviewed=false;}
}
public sealed record ReviewEntry(int Index,string Fingerprint,string Note,bool Reviewed);
public sealed record ReviewFile(string AssSha256,ReviewEntry[] Entries,string? MediaPath);
public static class ReviewStore
{
    public static void Save(string subtitlePath,SubtitleDocument d,string? mediaPath){var data=new ReviewFile(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(subtitlePath))),d.Rows.Select((r,i)=>new ReviewEntry(i,r.Fingerprint,r.Note,r.Reviewed)).ToArray(),mediaPath);SubtitleDocument.AtomicSave(subtitlePath+".frameflow.json",System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true})),false);}
    public static string? Load(string path,SubtitleDocument d)
    {var file=path+".frameflow.json";if(!File.Exists(file))return null;var data=JsonSerializer.Deserialize<ReviewFile>(File.ReadAllText(file))??throw new FormatException("校对记录为空。");if(data.AssSha256!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))throw new InvalidOperationException("字幕已被外部修改，旧校对记录未自动套用，避免批注错位。");var rows=d.Rows.ToArray();foreach(var e in data.Entries){if(e.Index>=0&&e.Index<rows.Length&&rows[e.Index].Fingerprint==e.Fingerprint){rows[e.Index].Note=e.Note;rows[e.Index].Reviewed=e.Reviewed;}}return data.MediaPath;}
}
