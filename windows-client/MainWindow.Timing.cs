using System.Windows;
using System.Windows.Controls;
using Frameflow.Controls;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    FrameTimeline? timingFrames;
    string? timingHistoryOverride;
    string TimingHistoryPath=>timingHistoryOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Frameflow","timing-shifts.json");
    int[] TimingSelection()=>SelectedRows().Select(r=>r.Number-1).ToArray();
    void ShiftTimesClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(!ApplyEditor())return;Pause();var history=TimingTools.ReadHistory(TimingHistoryPath);var dialog=new ShiftTimesDialog(document,TimingSelection(),timingFrames,history){Owner=this};
        if(dialog.ShowDialog()!=true)return;CommitSubtitleCopy(dialog.Working,dialog.Targets,"时间平移已应用");
        try{history.Insert(0,new(Path.GetFileName(subtitlePath)??"未命名",DateTimeOffset.Now.ToString("O"),dialog.Settings));TimingTools.WriteHistory(TimingHistoryPath,history);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Status.Text="时间平移已应用，可撤销；但历史记录保存失败："+ex.Message;}
    });
    void TimingPostprocessClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(!ApplyEditor())return;Pause();var dialog=new TimingPostprocessDialog(document,TimingSelection(),timingFrames){Owner=this};if(dialog.ShowDialog()==true)CommitSubtitleCopy(dialog.Working,dialog.Targets,"时间轴后处理已应用");
    });
    void KanjiTimingClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(!ApplyEditor())return;Pause();var dialog=new KanjiTimingDialog(document){Owner=this};if(dialog.ShowDialog()==true)CommitSubtitleCopy(dialog.Working,dialog.Working.Rows.Where(r=>dialog.Applied.Contains(r.Number-1)).ToArray(),"卡拉 OK 计时转移已应用");
    });
    void FrameTimingClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        Pause();string? source=mediaPath;var dialog=new FrameTimingDialog(timingFrames,VideoAvailable?source:null,duration){Owner=this};
        if(dialog.ShowDialog()==true){if(source!=mediaPath)throw new InvalidOperationException("视频已改变，请重新加载帧表。");timingFrames=dialog.Timeline;Status.Text=timingFrames==null?"帧数据已清除。":"帧时间表已加载（仅当前媒体会话）；可用于按帧平移与关键帧吸附。";}
    });
    void RunTimingOperation(string operation)
    {
        if(!ApplyEditor())return;Pause();var copy=SubtitleTools.Copy(document);var selection=TimingSelection();AssRow[] result;
        if(operation is "scene" or "align")
        {
            if(!VideoAvailable)throw new InvalidOperationException("请先打开视频。");
            // Audio-only mode deliberately keeps the displayed video frame stationary.
            double position=VideoPositionSeconds;
            result=operation=="scene"?TimingTools.SnapScene(copy,selection,timingFrames??throw new InvalidOperationException("请先加载帧时间表与关键帧。"),position):TimingTools.Align(copy,selection,selected?.Number-1??-1,position,timingFrames);
        }
        else result=TimingTools.Continuous(copy,selection,operation=="continuous-start");
        CommitSubtitleCopy(copy,result,operation=="scene"?"所选行已贴齐关键帧场景":operation=="align"?"所选行整体平移至视频位置":"所选行时间已衔接");
    }
    void SnapSceneClick(object sender,RoutedEventArgs e)=>Guard(()=>RunTimingOperation("scene"));
    void AlignToCurrentClick(object sender,RoutedEventArgs e)=>Guard(()=>RunTimingOperation("align"));
    void MakeContinuousClick(object sender,RoutedEventArgs e)=>Guard(()=>RunTimingOperation((string)((MenuItem)sender).Tag));
    void TimingMenuOpened(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(e.Source,sender))return;var indices=TimingSelection();bool any=indices.Length>0,video=VideoAvailable;
        SceneTimingItem.IsEnabled=any&&video&&timingFrames?.Keyframes.Length>0;AlignTimingItem.IsEnabled=any&&video;
        AlignTimingItem.Header=timingFrames==null?"所选行整体对齐视频播放位置":"所选行整体对齐当前帧";
        bool adjacent=any&&indices.Zip(indices.Skip(1)).All(p=>p.Second==p.First+1);ContinuousStartItem.IsEnabled=adjacent&&(indices.Length>1||indices[0]>0);ContinuousEndItem.IsEnabled=adjacent&&(indices.Length>1||indices[0]+1<document.Rows.Count());
    }
}
