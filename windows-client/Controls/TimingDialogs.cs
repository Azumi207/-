using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Frameflow.Core;
using Frameflow.Media;
using Microsoft.Win32;

namespace Frameflow.Controls;

internal abstract class TimingPreviewDialog:SubtitleToolDialog
{
    protected readonly SubtitleDocument Original;
    protected readonly int[] Selection;
    internal SubtitleDocument Working {get;private set;}
    internal AssRow[] Targets {get;private set;}=[];
    internal readonly DataGrid Preview=new(){Height=160,IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,Margin=new Thickness(3,8,3,3)};
    protected TimingPreviewDialog(string title,SubtitleDocument source,int[] selection,double height):base(title,790,height)
    {
        Original=SubtitleTools.Copy(source);Working=SubtitleTools.Copy(source);Selection=selection.ToArray();
        foreach(var (header,path,width) in new[]{("行","Number",40d),("原起点","BeforeStart",100d),("原终点","BeforeEnd",100d),("新起点","AfterStart",100d),("新终点","AfterEnd",100d),("正文","Text",160d)})Preview.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(path),Width=width});
    }
    protected static double Number(TextBox box)=>double.Parse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture);
    protected CheckBox Flag(string label,bool value=false){var c=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(3,8,3,3)};Body.Children.Add(c);return c;}
    protected abstract AssRow[] Calculate(SubtitleDocument copy);
    internal void RefreshPreview(){var copy=SubtitleTools.Copy(Original);var targets=Calculate(copy);var changes=TimingTools.Changes(Original,copy);Working=copy;Targets=targets;Preview.ItemsSource=changes;Message.Foreground=Brushes.DarkSlateGray;Message.Text=$"范围 {targets.Length} 行；实际改变 {changes.Length} 行。完成前不修改原字幕。";}
    protected void AddPreview(){Body.Children.Add(Preview);Action("预览变化",RefreshPreview);Action("应用",()=>{RefreshPreview();DialogResult=true;});Finish();}
}

internal sealed class ShiftTimesDialog:TimingPreviewDialog
{
    internal readonly TextBox Amount;
    internal readonly ComboBox Unit,Direction,Range,Fields;
    internal readonly CheckBox Clamp;
    internal readonly ListBox History=new(){Height=70};
    readonly FrameTimeline? frames;
    internal ShiftSettings Settings=>new(Unit.SelectedIndex==1?Number(Amount):Timecode.Parse(Amount.Text),Unit.SelectedIndex==1,Direction.SelectedIndex==1,Range.SelectedIndex,Fields.SelectedIndex,Clamp.IsChecked==true);
    internal ShiftTimesDialog(SubtitleDocument doc,int[] selection,FrameTimeline? timeline,IEnumerable<ShiftHistoryEntry> history):base("时间平移",doc,selection,740)
    {
        frames=timeline;Hint("正数平移量，选择提前或延后。默认拒绝负时间、零时长及倒置区间，整批不部分应用。保留正文、说话人、样式及 ASS 标签。");
        Amount=Input(Body,"平移量（秒 / h:mm:ss.cc；帧模式为整数）","0.50");
        var line=new WrapPanel();Body.Children.Add(line);ComboBox Pick(string title,string[] items){var p=new StackPanel{Width=175,Margin=new Thickness(0,0,8,0)};line.Children.Add(p);return Choice(p,title,items);}
        Unit=Pick("单位",["时间","帧（需要帧时间表）"]);Direction=Pick("方向",["延后","提前"]);Range=Pick("范围",["所选行","全部行","所选首行及之后"]);Fields=Pick("字段",["起点和终点","只改起点","只改终点"]);
        Clamp=Flag("时间模式：负时间截断为 0（仍拒绝零时长）");Hint(timeline==null?"尚无帧时间表：按帧平移前，请从时间菜单加载。":"帧数据："+timeline.Source);
        Hint("最近平移（双击仅载入参数，不直接改字幕；最多 20 条）");History.ItemsSource=history.ToArray();Body.Children.Add(History);History.MouseDoubleClick+=(_,_)=>{if(History.SelectedItem is ShiftHistoryEntry item)Load(item.Settings);};AddPreview();
    }
    internal void Load(ShiftSettings s){Amount.Text=s.Amount.ToString("0.######",CultureInfo.InvariantCulture);Unit.SelectedIndex=s.Frames?1:0;Direction.SelectedIndex=s.Backward?1:0;Range.SelectedIndex=s.Scope;Fields.SelectedIndex=s.Fields;Clamp.IsChecked=s.ClampNegative;}
    protected override AssRow[] Calculate(SubtitleDocument copy)=>TimingTools.Shift(copy,Selection,Settings,frames);
}

internal sealed class TimingPostprocessDialog:TimingPreviewDialog
{
    internal readonly ListBox Styles=new(){Height=75,SelectionMode=SelectionMode.Multiple};
    internal readonly CheckBox SelectionOnly,LeadIn,LeadOut,Continuous,Snap;
    internal readonly TextBox LeadInMs,LeadOutMs,GapMs,OverlapMs,StartBefore,StartAfter,EndBefore,EndAfter;
    internal readonly Slider Bias=new(){Minimum=0,Maximum=1,Value=.5,TickFrequency=.1,IsSnapToTickEnabled=true,Margin=new Thickness(5),Width=300};
    readonly FrameTimeline? frames;
    internal PostprocessSettings Settings=>new(SelectionOnly.IsChecked==true,Styles.SelectedItems.Cast<string>().ToArray(),LeadIn.IsChecked==true,Number(LeadInMs),LeadOut.IsChecked==true,Number(LeadOutMs),Continuous.IsChecked==true,Number(GapMs),Number(OverlapMs),Bias.Value,Snap.IsChecked==true,Number(StartBefore),Number(StartAfter),Number(EndBefore),Number(EndAfter));
    internal TimingPostprocessDialog(SubtitleDocument doc,int[] selection,FrameTimeline? timeline):base("时间轴后处理",doc,selection,820)
    {
        frames=timeline;Hint("按顺序：入场/出场延伸 → 相邻行衔接 → 关键帧吸附。只处理所选样式的对白；不同样式、层分别处理，避免误接平行字幕。所有变化可先预览，统一撤销。");
        SelectionOnly=Flag("仅处理所选行",true);Hint("处理样式（可多选，默认全部）：");Styles.ItemsSource=doc.Rows.Select(r=>r.Style).Distinct().ToArray();Body.Children.Add(Styles);Styles.SelectAll();
        var panel=new WrapPanel();Body.Children.Add(panel);TextBox Num(string label,string value){var p=new StackPanel{Width=170,Margin=new Thickness(0,0,8,0)};panel.Children.Add(p);return Input(p,label,value);}
        LeadIn=Flag("增加入场提前量（不新增相邻重叠）");LeadOut=Flag("增加出场延后量（不新增相邻重叠）");LeadInMs=Num("入场 ms","100");LeadOutMs=Num("出场 ms","100");
        Continuous=Flag("修复短间隙 / 短重叠（两行都在处理范围）");GapMs=Num("间隙阈值 ms","120");OverlapMs=Num("重叠阈值 ms","100");Hint("衔接偏向：0 保留前句终点；1 保留后句起点；0.5 取中点。");Body.Children.Add(Bias);
        Snap=Flag("吸附关键帧（非自动切镜识别）");Snap.IsEnabled=timeline?.Keyframes.Length>0;
        var snapPanel=new WrapPanel();Body.Children.Add(snapPanel);TextBox S(string label){var p=new StackPanel{Width=170,Margin=new Thickness(0,0,8,0)};snapPanel.Children.Add(p);return Input(p,label,"100");}
        StartBefore=S("起点在关键帧前 ms");StartAfter=S("起点在关键帧后 ms");EndBefore=S("终点在关键帧前 ms");EndAfter=S("终点在关键帧后 ms");Hint(timeline==null?"尚无帧数据，关键帧吸附不可用。":timeline.Source+"；终点使用下一段的独占边界。");AddPreview();
    }
    protected override AssRow[] Calculate(SubtitleDocument copy)=>TimingTools.Postprocess(copy,Selection,Settings,frames);
}

internal sealed class FrameTimingDialog:SubtitleToolDialog
{
    internal FrameTimeline? Timeline {get;private set;}
    internal readonly TextBox EndTime,Fps;
    internal readonly CheckBox ConfirmCfr=new(){Content="我确认素材为固定帧率（VFR 请分析视频或导入 v2）",Margin=new Thickness(3,8,3,3)};
    internal readonly TextBlock Summary=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,8,3,8)};
    internal bool Busy {get;private set;}
    readonly string? media;readonly WrapPanel tools=new();readonly Button analyze,apply;
    CancellationTokenSource? cancellation;bool closed;
    internal FrameTimingDialog(FrameTimeline? timeline,string? mediaPath,double duration):base("帧时间表与关键帧",740,680)
    {
        Timeline=timeline;media=mediaPath;Hint("仅在这里手动分析，不增加播放时负担。读取 Windows 支持的媒体样本时间戳和 CleanPoint；关键帧不等于所有画面切镜。播放位置仍来自系统播放器，不能保证逐帧解码定位。");Body.Children.Add(Summary);
        analyze=Action("分析当前视频",()=>_ = AnalyzeAsync(),tools);analyze.IsEnabled=media!=null;Action("取消分析",()=>cancellation?.Cancel(),tools);Body.Children.Add(tools);
        EndTime=Input(Body,"末帧结束时间（秒；v2 文件没有此值，请确认）",(timeline?.End??Math.Max(duration,1)).ToString("0.######",CultureInfo.InvariantCulture));
        var files=new WrapPanel();Body.Children.Add(files);
        Action("导入时间码 v2…",()=>{Idle();var p=new OpenFileDialog{Filter="时间码|*.txt;*.timecodes|所有文件|*.*"};if(p.ShowDialog(this)==true)LoadTimecodes(p.FileName);},files);
        Action("导入关键帧 v1…",()=>{Idle();var p=new OpenFileDialog{Filter="关键帧|*.txt;*.keyframes|所有文件|*.*"};if(p.ShowDialog(this)==true)LoadKeys(p.FileName);},files);
        Action("导出时间码…",()=>Export(false),files);Action("导出关键帧…",()=>Export(true),files);
        Fps=Input(Body,"手动固定帧率（仅明确知道帧率时使用，不推测）","24");Body.Children.Add(ConfirmCfr);var manual=new WrapPanel();Body.Children.Add(manual);Action("生成固定帧率表",Generate,manual);
        Hint("按帧修改会转换为 ASS 的 10 ms 精度；不能安全表达的帧边界将拒绝。导入时间码后关键帧清空，需重新导入。更换视频会清除当前帧表。");
        Action("清除帧数据",()=>{Idle();Timeline=null;Refresh();},tools);apply=Action("使用此帧表",()=>{Idle();DialogResult=true;});Finish();Closed+=(_,_)=>{closed=true;cancellation?.Cancel();};Refresh();
    }
    void Idle(){if(Busy)throw new InvalidOperationException("请先等待或取消当前分析。");}
    void Refresh()=>Summary.Text=Timeline==null?"当前没有帧时间表。":$"{Timeline.Count:N0} 帧 · {Timeline.Keyframes.Length:N0} 个关键帧 · 视频终点 {Timeline.End:0.######} 秒\n{Timeline.Source}";
    internal async Task AnalyzeAsync()
    {
        if(Busy||media==null||closed)return;Busy=true;analyze.IsEnabled=apply.IsEnabled=false;cancellation=new CancellationTokenSource();var token=cancellation.Token;Message.Foreground=Brushes.DarkSlateGray;Message.Text="正在扫描媒体样本…（不解码画面）";
        try{var progress=new Progress<int>(n=>{if(!closed&&!token.IsCancellationRequested)Message.Text=$"已读取 {n:N0} 个视频样本…";});var result=await Task.Run(()=>VideoFrameIndex.Read(media,token,progress),token);if(!closed&&!token.IsCancellationRequested){Timeline=result;EndTime.Text=result.End.ToString("0.######",CultureInfo.InvariantCulture);Refresh();Message.Text="分析完成；点击使用此帧表生效。";}}
        catch(OperationCanceledException){if(!closed)Message.Text="已取消；保留之前的帧表。";}
        catch(Exception ex){if(!closed){Message.Foreground=Frameflow.Core.Appearance.Brush("ErrorBrush");Message.Text="无法生成可靠帧表："+ex.Message+"。可导入外部时间码与关键帧。";}}
        finally{Busy=false;cancellation.Dispose();cancellation=null;if(!closed){analyze.IsEnabled=media!=null;apply.IsEnabled=true;}}
    }
    static string Read(string path){if(new FileInfo(path).Length>64*1024*1024)throw new IOException("帧数据文件超过 64 MB。");return File.ReadAllText(path);}
    internal void LoadTimecodes(string path){Idle();var value=FrameTimeline.ParseV2(Read(path),Timecode.Parse(EndTime.Text));Timeline=value;Refresh();}
    internal void LoadKeys(string path){Idle();if(Timeline==null)throw new InvalidOperationException("请先加载帧时间表。");Timeline=Timeline.WithKeys(FrameTimeline.ParseKeyframes(Read(path)),"导入关键帧 v1");Refresh();}
    internal void Generate(){Idle();if(ConfirmCfr.IsChecked!=true)throw new InvalidOperationException("请确认素材为固定帧率，不可将固定帧率表用于 VFR。");Timeline=FrameTimeline.Cfr(double.Parse(Fps.Text,CultureInfo.InvariantCulture),Timecode.Parse(EndTime.Text));Refresh();}
    internal void SaveTable(string path,bool keys){Idle();if(Timeline==null)throw new InvalidOperationException("没有可导出的帧表。");if(keys&&Timeline.Keyframes.Length==0)throw new InvalidOperationException("没有可导出的关键帧。");SubtitleDocument.AtomicSave(path,System.Text.Encoding.UTF8.GetBytes(keys?Timeline.KeyframeText():Timeline.Timecodes()));}
    void Export(bool keys){Idle();var p=new SaveFileDialog{Filter="文本文件|*.txt",FileName=keys?"keyframes.txt":"timecodes.txt"};if(p.ShowDialog(this)==true)SaveTable(p.FileName,keys);}
}

internal sealed class KanjiTimingDialog:SubtitleToolDialog
{
    readonly SubtitleDocument original;
    internal SubtitleDocument Working {get;private set;}
    internal readonly ComboBox SourceStyle,TargetStyle,KaraokeTag;
    internal readonly TextBox SourceCount,TargetCount;
    internal readonly CheckBox Overwrite=new(){Content="允许覆盖目标已有卡拉 OK 标签"},CopyTimes=new(){Content="同时复制源行起止时间",IsChecked=true};
    internal readonly TextBlock Pair=new(){TextWrapping=TextWrapping.Wrap},Remaining=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,8,3,8)};
    internal readonly ListBox GroupsList=new(){Height=120};
    internal readonly List<KaraokeGroup> Groups=[];
    internal readonly HashSet<int> Applied=[];
    AssRow[] sources=[],targets=[];int si,ti;
    internal KanjiTimingDialog(SubtitleDocument doc):base("汉字计时器 · 卡拉 OK 计时转移",780,760)
    {
        original=SubtitleTools.Copy(doc);Working=SubtitleTools.Copy(doc);Hint("按样式配对源行与目标行，将已有 k / K / kf / ko 音节时间分配给目标正文。手动多对多分组；不是自动语音识别。保留目标静态样式标签；动画和绘图目标暂拒绝。");
        var styles=doc.Rows.Where(r=>!r.Comment).Select(r=>r.Style).Distinct().ToArray();SourceStyle=Choice(Body,"源行样式（已有卡拉 OK）",styles);TargetStyle=Choice(Body,"目标行样式",styles);if(styles.Length>1)TargetStyle.SelectedIndex=1;
        foreach(var c in new[]{Overwrite,CopyTimes}){c.Margin=new Thickness(3,7,3,3);Body.Children.Add(c);}KaraokeTag=Choice(Body,"输出标签",["k","kf","ko"]);
        var toolbar=new WrapPanel();Body.Children.Add(toolbar);Action("开始 / 重置所有暂存配对",Start,toolbar);Action("跳过源行",()=>Skip(true),toolbar);Action("跳过目标行",()=>Skip(false),toolbar);
        Body.Children.Add(Pair);Body.Children.Add(Remaining);
        var counts=new WrapPanel();Body.Children.Add(counts);var a=new StackPanel{Width=340};var b=new StackPanel{Width=340};counts.Children.Add(a);counts.Children.Add(b);SourceCount=Input(a,"本组源音节数","1");TargetCount=Input(b,"本组目标字素数（0 表示静音段）","1");
        Body.Children.Add(GroupsList);var links=new WrapPanel();Body.Children.Add(links);Action("连接这一组",Link,links);Action("撤销最后分组",Unlink,links);Action("应用本对并下一对",ApplyPair,links);
        Hint("跳过行会清除当前未应用分组。完成只提交已经应用的配对；取消放弃整个会话。源行不改动，提交后可统一撤销。");
        Action("完成（仅已应用配对）",()=>{if(Groups.Count>0)throw new InvalidOperationException("当前仍有未应用分组，请先应用本对或跳过后再完成。");DialogResult=true;});Finish();
    }
    internal void Start()
    {
        if(SourceStyle.SelectedItem is not string from||TargetStyle.SelectedItem is not string to||from==to)throw new InvalidOperationException("请选择两个不同的源/目标样式。");
        var copy=SubtitleTools.Copy(original);var s=copy.Rows.Where(r=>!r.Comment&&r.Style==from).ToArray();var t=copy.Rows.Where(r=>!r.Comment&&r.Style==to).ToArray();if(s.Length==0||t.Length==0)throw new InvalidOperationException("所选样式没有对白行。");Working=copy;sources=s;targets=t;si=ti=0;Groups.Clear();Applied.Clear();Refresh();
    }
    void Current(){if(si>=sources.Length||ti>=targets.Length)throw new InvalidOperationException("没有可配对的行，请开始或调整样式。");}
    internal void Link()
    {
        Current();int s=int.Parse(SourceCount.Text,CultureInfo.InvariantCulture),t=int.Parse(TargetCount.Text,CultureInfo.InvariantCulture);var src=KanjiTiming.Parse(sources[si]);var dst=KanjiTiming.Target(targets[ti].Text,Overwrite.IsChecked==true).Elements;
        if(s<=0||t<0||Groups.Sum(g=>g.SourceCount)+(long)s>src.Length||Groups.Sum(g=>g.TargetCount)+(long)t>dst.Length)throw new InvalidOperationException("分组数量超出剩余范围。");Groups.Add(new(s,t));Refresh();
    }
    internal void Unlink(){if(Groups.Count>0)Groups.RemoveAt(Groups.Count-1);Refresh();}
    internal void Skip(bool source){Current();if(source)si++;else ti++;Groups.Clear();Refresh();}
    internal void ApplyPair(){Current();KanjiTiming.Apply(sources[si],targets[ti],Groups,Overwrite.IsChecked==true,CopyTimes.IsChecked==true,(string)KaraokeTag.SelectedItem);Applied.Add(targets[ti].Number-1);si++;ti++;Groups.Clear();Refresh();}
    void Refresh()
    {
        GroupsList.ItemsSource=Groups.Select((g,i)=>$"{i+1}. {g.SourceCount} 个源音节 → {g.TargetCount} 个目标字素").ToArray();
        if(si>=sources.Length||ti>=targets.Length){Pair.Text=$"配对结束 / 未开始 · 已应用 {Applied.Count} 行";Remaining.Text="";return;}
        Pair.Text=$"源 #{sources[si].Number}：{sources[si].Text}\n目标 #{targets[ti].Number}：{targets[ti].Text}\n已应用 {Applied.Count} 行";
        try{var src=KanjiTiming.Parse(sources[si]);var dst=KanjiTiming.Target(targets[ti].Text,Overwrite.IsChecked==true).Elements;Remaining.Text="剩余源音节："+string.Join(" | ",src.Skip(Groups.Sum(g=>g.SourceCount)).Select(u=>$"{u.Text} [{u.Duration}cs]"))+"\n剩余目标："+string.Join(" | ",dst.Skip(Groups.Sum(g=>g.TargetCount)).Select(t=>t.Display));}
        catch(InvalidOperationException ex){Remaining.Text=ex.Message+" 可调整覆盖选项或跳过此行。";}
    }
}
