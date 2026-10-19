using System.Globalization;
using System.Text.Json;

namespace Frameflow.Core;
public sealed record ShiftSettings(double Amount,bool Frames,bool Backward,int Scope,int Fields,bool ClampNegative=false);
public sealed record ShiftHistoryEntry(string File,string When,ShiftSettings Settings)
{
    public override string ToString()=>$"{File} · {(Settings.Backward?"提前":"延后")} {Settings.Amount.ToString("0.###",CultureInfo.InvariantCulture)} {(Settings.Frames?"帧":"秒")} · {new[]{"所选行","全部行","所选首行之后"}[Math.Clamp(Settings.Scope,0,2)]} · {new[]{"起止","起点","终点"}[Math.Clamp(Settings.Fields,0,2)]}";
}
public sealed record TimingChange(int Number,string BeforeStart,string BeforeEnd,string AfterStart,string AfterEnd,string Text);
public sealed record PostprocessSettings(bool SelectionOnly,string[] Styles,bool LeadIn,double LeadInMs,bool LeadOut,double LeadOutMs,bool Continuous,double GapMs,double OverlapMs,double Bias,bool Snap,double StartBeforeMs,double StartAfterMs,double EndBeforeMs,double EndAfterMs);
public static class TimingTools
{
    public static double Round(double value)=>Math.Round(value*100,MidpointRounding.AwayFromZero)/100;
    static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static AssRow[] Scope(SubtitleDocument doc,int[] selection,int scope)
    {
        var rows=doc.Rows.ToArray();Need(scope>=0&&scope<=2,"未知作用范围。");Need(selection.All(i=>i>=0&&i<rows.Length),"字幕选择已失效。");
        if(scope!=1)Need(selection.Length>0,"请先选择至少一行字幕。");
        return scope==1?rows:scope==2?rows.Skip(selection.Min()).ToArray():selection.Distinct().Order().Select(i=>rows[i]).ToArray();
    }
    static void Apply(IEnumerable<(AssRow Row,double Start,double End)> input)
    {
        var changes=input.Select(p=>(p.Row,Start:Round(p.Start),End:Round(p.End))).ToArray();
        foreach(var c in changes)Need(double.IsFinite(c.Start)&&double.IsFinite(c.End)&&c.Start>=0&&c.End>c.Start&&c.End<=86400,$"第 {c.Row.Number} 行修改后时间无效（需 0 ≤ 起点 < 终点 ≤ 24 小时）。未修改任何行。");
        foreach(var c in changes)if(c.Row.Start!=c.Start||c.Row.End!=c.End){c.Row.Start=c.Start;c.Row.End=c.End;c.Row.Reviewed=false;}
    }
    public static AssRow[] Shift(SubtitleDocument doc,int[] selection,ShiftSettings settings,FrameTimeline? frames)
    {
        Need(double.IsFinite(settings.Amount)&&settings.Amount>=0&&settings.Amount<=86400,"平移量必须是 0～86400 的有限数字。");Need(settings.Fields>=0&&settings.Fields<=2,"未知时间字段。");
        if(settings.Frames){Need(frames!=null,"按帧平移需要先加载帧时间表。");Need(settings.Amount==Math.Truncate(settings.Amount),"帧数必须为整数。");}
        var targets=Scope(doc,selection,settings.Scope);Need(targets.Length>0,"没有可处理的字幕行。");
        double delta=settings.Backward?-settings.Amount:settings.Amount;
        double Move(double t)=>settings.Frames?frames!.Shift(t,(int)delta):settings.ClampNegative?Math.Max(0,t+delta):t+delta;
        Apply(targets.Select(r=>(r,settings.Fields==2?r.Start:Move(r.Start),settings.Fields==1?r.End:Move(r.End))));return targets;
    }
    public static AssRow[] Align(SubtitleDocument doc,int[] selection,int active,double position,FrameTimeline? frames)
    {
        Need(selection.Contains(active),"活动行不在所选行中。");var target=frames==null?Round(position):frames.Boundary(frames.At(position));
        double delta=target-doc.Rows.ElementAt(active).Start;
        return Shift(doc,selection,new(Math.Abs(delta),false,delta<0,0,0),null);
    }
    public static AssRow[] SnapScene(SubtitleDocument doc,int[] selection,FrameTimeline frames,double position)
    {
        var rows=Scope(doc,selection,0);var range=frames.Scene(position);Apply(rows.Select(r=>(r,range.Start,range.End)));return rows;
    }
    public static AssRow[] Continuous(SubtitleDocument doc,int[] selection,bool changeStart)
    {
        var rows=doc.Rows.ToArray();var targets=Scope(doc,selection,0);int[] indices=targets.Select(r=>r.Number-1).ToArray();
        Need(indices.Zip(indices.Skip(1)).All(p=>p.Second==p.First+1),"使时间连续要求选择相邻行；不会跨过未选字幕。");
        var changes=new List<(AssRow,double,double)>();
        if(indices.Length==1)
        {
            int i=indices[0];Need(changeStart?i>0:i+1<rows.Length,changeStart?"首行没有前一句。":"末行没有后一句。");changes.Add((rows[i],changeStart?rows[i-1].End:rows[i].Start,changeStart?rows[i].End:rows[i+1].Start));
        }
        else if(changeStart){for(int n=1;n<indices.Length;n++){int i=indices[n];changes.Add((rows[i],rows[i-1].End,rows[i].End));}}
        else{for(int n=0;n<indices.Length-1;n++){int i=indices[n];changes.Add((rows[i],rows[i].Start,rows[i+1].Start));}}
        Apply(changes);return targets;
    }
    public static AssRow[] Postprocess(SubtitleDocument doc,int[] selection,PostprocessSettings options,FrameTimeline? frames)
    {
        var amounts=new[]{options.LeadInMs,options.LeadOutMs,options.GapMs,options.OverlapMs,options.StartBeforeMs,options.StartAfterMs,options.EndBeforeMs,options.EndAfterMs};
        Need(amounts.All(n=>double.IsFinite(n)&&n>=0&&n<=60000),"各时间阈值须在 0～60000 ms。");Need(double.IsFinite(options.Bias)&&options.Bias>=0&&options.Bias<=1,"衔接偏向须在 0～1。");Need(options.LeadIn||options.LeadOut||options.Continuous||options.Snap,"请启用至少一个处理步骤。");
        if(options.Snap)Need(frames?.Keyframes.Length>0,"关键帧吸附需要有效帧时间表与关键帧。");
        var targets=Scope(doc,selection,options.SelectionOnly?0:1).Where(r=>!r.Comment&&options.Styles.Contains(r.Style,StringComparer.Ordinal)).ToArray();Need(targets.Length>0,"没有符合所选样式/范围的对白行（注释不处理）。");
        var wanted=targets.ToHashSet();var values=doc.Rows.ToDictionary(r=>r,r=>(Start:r.Start,End:r.End));
        // Isolate layers/styles to avoid joining parallel speakers/sign tracks.
        foreach(var group in doc.Rows.Where(r=>!r.Comment).GroupBy(r=>(r.Style,r.Layer)))
        {
            var rows=group.OrderBy(r=>r.Start).ThenBy(r=>r.Number).ToArray();double previousEnd=0;
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];if(wanted.Contains(r)&&options.LeadIn)values[r]=(Math.Max(Math.Max(0,r.Start-options.LeadInMs/1000),Math.Min(r.Start,previousEnd)),r.End);
                previousEnd=Math.Max(previousEnd,r.End);
            }
            if(options.LeadOut)for(int i=0;i<rows.Length;i++){var r=rows[i];if(!wanted.Contains(r))continue;var pair=values[r];double next=i+1<rows.Length?values[rows[i+1]].Start:86400;values[r]=(pair.Start,Math.Min(r.End+options.LeadOutMs/1000,Math.Max(r.End,next)));}
            if(options.Continuous)for(int i=1;i<rows.Length;i++)
            {
                var a=rows[i-1];var b=rows[i];if(!wanted.Contains(a)||!wanted.Contains(b))continue;var av=values[a];var bv=values[b];double gap=bv.Start-av.End;
                if(gap>0&&gap<=options.GapMs/1000+.000001||gap<0&&-gap<=options.OverlapMs/1000+.000001)
                {double boundary=Round(av.End+gap*options.Bias);Need(boundary>av.Start&&boundary<bv.End,$"第 {a.Number}/{b.Number} 行无法安全衔接。请降低重叠阈值。");values[a]=(av.Start,boundary);values[b]=(boundary,bv.End);}
            }
        }
        if(options.Snap)
        {
            var keys=frames!.Keyframes.Select(frames.Boundary).ToArray();
            double Snap(double t,double before,double after)
            {
                int found=Array.BinarySearch(keys,t);if(found>=0)return keys[found];int next=~found;var candidates=new List<double>();
                if(next<keys.Length&&keys[next]-t<=before/1000+.000001)candidates.Add(keys[next]);if(next>0&&t-keys[next-1]<=after/1000+.000001)candidates.Add(keys[next-1]);
                return candidates.OrderBy(k=>Math.Abs(k-t)).ThenBy(k=>k).DefaultIfEmpty(t).First();
            }
            foreach(var row in targets){var t=values[row];values[row]=(Snap(t.Start,options.StartBeforeMs,options.StartAfterMs),Snap(t.End,options.EndBeforeMs,options.EndAfterMs));}
        }
        Apply(targets.Select(r=>(r,values[r].Start,values[r].End)));return targets;
    }
    public static TimingChange[] Changes(SubtitleDocument before,SubtitleDocument after)=>before.Rows.Zip(after.Rows).Where(p=>p.First.Start!=p.Second.Start||p.First.End!=p.Second.End).Select(p=>new TimingChange(p.First.Number,p.First.StartText,p.First.EndText,p.Second.StartText,p.Second.EndText,p.Second.Plain)).ToArray();
    public static List<ShiftHistoryEntry> ReadHistory(string? path)
    {
        try{if(path==null||!File.Exists(path)||new FileInfo(path).Length>200000)return [];return (JsonSerializer.Deserialize<List<ShiftHistoryEntry>>(File.ReadAllText(path))??[]).Where(e=>e!=null&&e.Settings!=null&&double.IsFinite(e.Settings.Amount)&&e.Settings.Amount>=0&&e.Settings.Amount<=86400&&e.Settings.Scope is >=0 and <=2&&e.Settings.Fields is >=0 and <=2).Take(20).ToList();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return [];}
    }
    public static void WriteHistory(string path,IEnumerable<ShiftHistoryEntry> history){Directory.CreateDirectory(Path.GetDirectoryName(path)!);SubtitleDocument.AtomicSave(path,System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(history.Take(20))),false);}
}
