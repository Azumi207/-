using System.Globalization;

namespace Frameflow.Core;
public sealed class FrameTimeline
{
    public double[] Starts {get;}
    public int[] Keyframes {get;}
    public double End {get;}
    public string Source {get;}
    public int Count=>Starts.Length;
    public FrameTimeline(IEnumerable<double> starts,double end,IEnumerable<int> keys,string source)
    {
        Starts=starts.ToArray();End=end;Keyframes=keys.Distinct().Order().ToArray();Source=source;
        if(Starts.Length==0||Starts.Length>2_000_000||!double.IsFinite(end)||end<=0||end>86400||Starts.Any(t=>!double.IsFinite(t)||t<0)||Starts.Zip(Starts.Skip(1)).Any(p=>p.First>=p.Second)||Starts[^1]>=end)throw new FormatException("帧时间必须严格递增、非负且早于视频终点，最多 200 万帧 / 24 小时。");
        if(Keyframes.Any(k=>k<0||k>=Count))throw new FormatException("关键帧编号超出帧表范围（编号从 0 开始）。");
    }
    public int At(double seconds)
    {
        if(!double.IsFinite(seconds)||seconds<Starts[0]-.000001||seconds>=End+.000001)throw new InvalidOperationException("当前播放位置超出帧时间表。");
        int i=Array.BinarySearch(Starts,seconds);return i>=0?i:Math.Clamp(~i-1,0,Count-1);
    }
    int BoundaryIndex(double seconds)
    {
        if(seconds<0||seconds>End+.000001)throw new InvalidOperationException("待平移时间超出帧表，不能猜测视频以外的帧率。");
        int i=Array.BinarySearch(Starts,seconds);return i>=0?i:~i;
    }
    // ASS centiseconds must fall after the previous frame PTS and no later than
    // the chosen frame PTS. Otherwise that boundary cannot be represented safely.
    public double Boundary(int index)
    {
        if(index<0||index>Count)throw new InvalidOperationException("平移后的帧超出视频范围。");
        double t=index==Count?End:Starts[index];double quantized=Math.Floor(t*100+.000001)/100;
        if(index>0&&quantized<=Starts[index-1]+.0000001)throw new InvalidOperationException("该帧边界无法用 ASS 的 10 ms 精度表示，未修改字幕。");
        return quantized;
    }
    public double Shift(double time,int frames)=>Boundary(checked(BoundaryIndex(time)+frames));
    public (double Start,double End) Scene(double time)
    {
        if(Keyframes.Length==0)throw new InvalidOperationException("没有关键帧数据，请先分析视频或导入关键帧表。");
        int frame=At(time);int i=Array.BinarySearch(Keyframes,frame);if(i<0)i=~i-1;
        return (Boundary(i<0?0:Keyframes[i]),Boundary(i+1<Keyframes.Length?Keyframes[i+1]:Count));
    }
    public FrameTimeline WithKeys(IEnumerable<int> keys,string source)=>new(Starts,End,keys,Source+"；"+source);
    public static FrameTimeline Cfr(double fps,double duration)
    {
        if(!double.IsFinite(fps)||fps<1||fps>240||!double.IsFinite(duration)||duration<=0||duration>86400||Math.Ceiling(duration*fps)>2_000_000)throw new FormatException("帧率范围 1～240；帧数不得超过 200 万，时长不得超过 24 小时。");
        int count=(int)Math.Ceiling(duration*fps-.0000001);return new(Enumerable.Range(0,count).Select(i=>i/fps),duration,[],"手动固定帧率 "+fps.ToString("0.######",CultureInfo.InvariantCulture)+" fps（不适用于 VFR）");
    }
    public static FrameTimeline ParseV2(string text,double lastEnd)
    {
        var lines=text.TrimStart('\uFEFF').Replace("\r","").Split('\n');if(!lines.FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s))?.Trim().Equals("# timecode format v2",StringComparison.OrdinalIgnoreCase)??true)throw new FormatException("仅支持 timecode format v2（每行一个毫秒时间戳）。");
        var times=lines.Where(l=>!string.IsNullOrWhiteSpace(l)&&!l.TrimStart().StartsWith('#')).Select(l=>double.Parse(l.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture)/1000).ToArray();return new(times,lastEnd,[],"导入 timecodes v2（末帧结束由用户确认）");
    }
    public static int[] ParseKeyframes(string text)
    {
        var lines=text.TrimStart('\uFEFF').Replace("\r","").Split('\n');if(!lines.FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s))?.Trim().Equals("# keyframe format v1",StringComparison.OrdinalIgnoreCase)??true)throw new FormatException("仅支持 Aegisub keyframe format v1（0 起始帧编号）。");
        var result=new List<int>();foreach(var line in lines.Select(l=>l.Trim())){if(line.Length==0||line.StartsWith('#')||line.StartsWith("fps ",StringComparison.OrdinalIgnoreCase))continue;if(!int.TryParse(line,NumberStyles.None,CultureInfo.InvariantCulture,out int frame))throw new FormatException("无效关键帧编号："+line);result.Add(frame);}
        if(result.Count==0)throw new FormatException("关键帧文件为空。");return result.ToArray();
    }
    public string Timecodes()=>"# timecode format v2\n"+string.Join('\n',Starts.Select(t=>(t*1000).ToString("0.######",CultureInfo.InvariantCulture)))+"\n";
    public string KeyframeText()=>"# keyframe format v1\nfps 0\n"+string.Join('\n',Keyframes)+"\n";
}
