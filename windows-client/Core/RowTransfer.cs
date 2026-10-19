using System.Text.Json;

namespace Frameflow.Core;

public sealed record RowData(string[] Format,string[] Values,bool Comment,string Note="");
public sealed record StyleData(string[] Format,string[] Values);
public sealed record RowPacket(int Version,RowData[] Rows,StyleData[] Styles);

public static class RowTransfer
{
    public const string ClipboardFormat="Frameflow.SubtitleRows.v1";
    public const int MaxChars=5_000_000,MaxRows=10000;
    public static RowPacket Capture(SubtitleDocument doc,IEnumerable<AssRow> rows)=>new(1,rows.Select(r=>new RowData(r.Format.ToArray(),r.Values.ToArray(),r.Comment,r.Note)).ToArray(),doc.Styles.Select(s=>new StyleData(s.Format.ToArray(),s.Values.ToArray())).ToArray());
    public static string Encode(RowPacket packet){Validate(packet);var json=JsonSerializer.Serialize(packet);if(json.Length>MaxChars)throw new FormatException("复制内容超过 5 MB 字符上限，请分批操作。");return json;}
    public static RowPacket Decode(string json){if(json.Length>MaxChars)throw new FormatException("剪贴板内容过大。");var packet=JsonSerializer.Deserialize<RowPacket>(json)??throw new FormatException("无效字幕剪贴板。");Validate(packet);return packet;}
    public static AssRow Row(RowData d)=>new(){Format=d.Format.ToArray(),Values=d.Values.ToArray(),Original=[],Comment=d.Comment,Note=d.Note??""};
    static void ValidateFields(string[] format,string[] values,bool row)
    {
        if(format==null||values==null||format.Length==0||format.Length>64||format.Length!=values.Length||format.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=format.Length)throw new FormatException("剪贴板字段不完整或重复。");
        for(int i=0;i<format.Length;i++)
        {
            if(string.IsNullOrWhiteSpace(format[i])||format[i].IndexOfAny([',','\r','\n'])>=0||values[i]==null||values[i].IndexOfAny(['\r','\n'])>=0||(!format[i].Equals("Text",StringComparison.OrdinalIgnoreCase)&&values[i].Contains(',')))throw new FormatException("剪贴板含无效字段或换行。");
        }
        if(row&&(!format[^1].Equals("Text",StringComparison.OrdinalIgnoreCase)||!format.Any(f=>f.Equals("Start",StringComparison.OrdinalIgnoreCase))||!format.Any(f=>f.Equals("End",StringComparison.OrdinalIgnoreCase))))throw new FormatException("剪贴板缺少时间或正文。");
    }
    public static void Validate(RowPacket p)
    {
        if(p.Version!=1||p.Rows==null||p.Styles==null||p.Rows.Length==0||p.Rows.Length>MaxRows||p.Styles.Length>10000)throw new FormatException("不支持的字幕剪贴板或行数超出上限。");
        foreach(var r in p.Rows){ValidateFields(r.Format,r.Values,true);var row=Row(r);if(row.End<=row.Start)throw new FormatException("粘贴字幕结束时间必须晚于开始时间。");if(!int.TryParse(row.Layer,out int layer)||layer<0)throw new FormatException("字幕层必须为非负整数。");}
        foreach(var s in p.Styles){ValidateFields(s.Format,s.Values,false);if(!s.Format.Any(f=>f.Equals("Name",StringComparison.OrdinalIgnoreCase)))throw new FormatException("剪贴板样式缺少名称。");}
    }
    static string Field(AssRow row,string name)=>name.Equals("Name",StringComparison.OrdinalIgnoreCase)||name.Equals("Actor",StringComparison.OrdinalIgnoreCase)?row.Actor:name.Equals("Layer",StringComparison.OrdinalIgnoreCase)?row.Layer:name.Equals("Marked",StringComparison.OrdinalIgnoreCase)?"Marked="+row.Layer:row.Get(name);
    public static string AssText(RowPacket packet)
    {
        Validate(packet);var format=SubtitleDocument.EventFormat.Split(',').Select(x=>x.Trim()).ToArray();
        return string.Join("\r\n",packet.Rows.Select(d=>(d.Comment?"Comment: ":"Dialogue: ")+string.Join(',',format.Select(f=>Field(Row(d),f)))));
    }
    public static RowPacket FromText(string text,double start,double duration)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>MaxChars)throw new FormatException("剪贴板为空或内容过大。");
        var lines=text.Replace("\r\n","\n").TrimEnd('\n').Split('\n');if(lines.Length>MaxRows)throw new FormatException("一次最多粘贴 10000 行。");
        bool IsAss(string s)=>s.StartsWith("Dialogue:",StringComparison.OrdinalIgnoreCase)||s.StartsWith("Comment:",StringComparison.OrdinalIgnoreCase);
        SubtitleDocument doc;
        if(lines.Any(IsAss))
        {
            if(!lines.All(IsAss))throw new FormatException("字幕事件行与普通文本混杂，未粘贴任何行。");
            doc=SubtitleDocument.Parse("[Events]\nFormat: "+SubtitleDocument.EventFormat+"\n"+string.Join('\n',lines));
            if(doc.Warnings.Count>0||doc.Rows.Count()!=lines.Length)throw new FormatException("无法解析完整 ASS 剪贴板；未粘贴任何行。");
        }
        else {doc=SubtitleDocument.New();foreach(var line in lines){doc.Add(start,start+Math.Max(.01,duration),line);start+=Math.Max(.01,duration);}}
        var packet=Capture(doc,doc.Rows);Validate(packet);return packet;
    }
    static bool Same(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase);
    static string Canonical(string f,bool legacy)=>Same(f,"Actor")?"Name":Same(f,"Marked")||Same(f,"Layer")?legacy?"Marked":"Layer":f;
    public static AssRow[] Insert(SubtitleDocument doc,RowPacket packet,int rowIndex)
    {
        Validate(packet);var rows=doc.Rows.ToArray();rowIndex=Math.Clamp(rowIndex,0,rows.Length);
        int index;string[] active;
        if(rows.Length>0){var anchor=rows[Math.Min(rowIndex,rows.Length-1)];index=doc.Records.FindIndex(r=>r.Row==anchor)+(rowIndex==rows.Length?1:0);active=anchor.Format;}
        else
        {
            int header=doc.Records.FindIndex(r=>r.Raw.Trim().Equals("[Events]",StringComparison.OrdinalIgnoreCase));
            index=header<0?doc.Records.Count:header+1;active=SubtitleDocument.EventFormat.Split(',').Select(f=>f.Trim()).ToArray();
            while(header>=0&&index<doc.Records.Count&&!doc.Records[index].Raw.TrimStart().StartsWith('[')){var raw=doc.Records[index].Raw.Trim();if(raw.StartsWith("Format:",StringComparison.OrdinalIgnoreCase))active=raw.Split(':',2)[1].Split(',').Select(f=>f.Trim()).ToArray();index++;}
        }
        var fields=active.Concat(packet.Rows.SelectMany(r=>r.Format)).Select(f=>Canonical(f,doc.LegacySsa)).Where(f=>!Same(f,"Text")).Distinct(StringComparer.OrdinalIgnoreCase).Append("Text").ToArray();
        var inserted=packet.Rows.Select(d=>{var source=Row(d);return new AssRow{Format=fields.ToArray(),Values=fields.Select(f=>Field(source,f)).ToArray(),Original=[],Comment=d.Comment,Note=source.Note};}).ToArray();
        // Validate everything before touching the document. Format records retain
        // unknown event fields while leaving following pre-existing rows untouched.
        var styles=MissingStyles(doc,packet,inserted.Select(r=>r.Style));
        var records=new List<Record>();if(!doc.Records.Any(r=>Same(r.Raw.Trim(),"[Events]")))records.Add(new(){Raw="[Events]"});
        records.Add(new(){Raw="Format: "+string.Join(", ",fields)});records.AddRange(inserted.Select(r=>new Record{Row=r}));records.Add(new(){Raw="Format: "+string.Join(", ",active)});
        doc.Records.InsertRange(index,records);ImportStyles(doc,styles);doc.Renumber();return inserted;
    }
    static StyleData[] MissingStyles(SubtitleDocument doc,RowPacket p,IEnumerable<string> names)
    {
        var wanted=names.ToHashSet(StringComparer.Ordinal);var present=doc.Styles.Select(s=>s.Name).ToHashSet(StringComparer.Ordinal);
        var styles=p.Styles.Where(s=>wanted.Contains(new AssStyle{Format=s.Format,Values=s.Values}.Name)&&!present.Contains(new AssStyle{Format=s.Format,Values=s.Values}.Name)).GroupBy(s=>new AssStyle{Format=s.Format,Values=s.Values}.Name).Select(g=>g.First()).ToArray();
        if(doc.LegacySsa&&styles.Length>0)throw new InvalidOperationException("旧 SSA 项目不自动导入其他样式；请先在目标项目建立对应样式。");return styles;
    }
    static void ImportStyles(SubtitleDocument doc,StyleData[] styles)
    {
        if(styles.Length==0)return;
        int section=doc.Records.FindIndex(r=>Same(r.Raw.Trim(),"[V4+ Styles]"));
        if(section<0){section=doc.Records.FindIndex(r=>Same(r.Raw.Trim(),"[Events]"));if(section<0)section=doc.Records.Count;doc.Records.Insert(section,new(){Raw="[V4+ Styles]"});}
        int end=section+1;while(end<doc.Records.Count&&!doc.Records[end].Raw.TrimStart().StartsWith('['))end++;
        foreach(var s in styles){doc.Records.Insert(end++,new(){Raw="Format: "+string.Join(", ",s.Format)});doc.Records.Insert(end++,new(){Style=new AssStyle{Format=s.Format.ToArray(),Values=s.Values.ToArray(),Original=[]}});}
    }
    public static void PasteFields(SubtitleDocument doc,RowPacket packet,AssRow[] targets,IReadOnlySet<string> fields)
    {
        Validate(packet);if(fields.Count==0)throw new FormatException("请至少选择一个字段。");
        if(targets.Length==0||packet.Rows.Length!=1&&packet.Rows.Length!=targets.Length)throw new FormatException("请选择与复制行数相同的目标行，或复制一行后应用到多个目标行。");
        var updated=new List<AssRow>();
        for(int i=0;i<targets.Length;i++)
        {
            var source=Row(packet.Rows[packet.Rows.Length==1?0:i]);var target=Row(new(targets[i].Format,targets[i].Values,targets[i].Comment));
            foreach(var f in fields)switch(f){case "Comment":target.Comment=source.Comment;break;case "Actor":target.Actor=source.Actor;break;case "Layer":target.Layer=source.Layer;break;default:target.Set(f,source.Get(f));break;}
            if(target.End<=target.Start)throw new FormatException("粘贴后结束时间不晚于开始时间，未修改任何行。");updated.Add(target);
        }
        var styles=fields.Contains("Style")?MissingStyles(doc,packet,updated.Select(r=>r.Style)):[];
        for(int i=0;i<targets.Length;i++){targets[i].Values=updated[i].Values;targets[i].Comment=updated[i].Comment;targets[i].Reviewed=false;}
        ImportStyles(doc,styles);
    }
}
