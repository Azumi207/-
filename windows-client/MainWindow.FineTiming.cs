using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Frameflow.Controls;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    // The document is committed once on release, not 120+ times per second.
    bool waveEditing,waveSeekPending,waveAssPending,wavePreviewRetain;
    AssRow? waveEditRow;SubtitleDocument? wavePreviewDocument;
    double waveStart,waveEnd,waveSeekTime;
    readonly DispatcherTimer wavePreviewTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(100)};
    public int WavePreviewSeeks {get;private set;}
    bool? fineAudition; // false = boundary context; true = exact selection
    AssRow? fineAuditionRow;
    double fineAuditionStart,fineAuditionEnd;
    public int FineLoopRestarts {get;private set;}

    void InitializeFineTiming()
    {
        foreach(var wave in new[]{Wave,FineWave})
        {
            wave.BeginEdit+=()=>BeginWaveEdit(wave);
            wave.SelectionEdited+=(a,b)=>UpdateWaveEdit(wave,a,b);
            wave.EndEdit+=EndWaveEdit;
        }
        FineWave.FixedBoundary=true;FineWave.PrecisionView=true;
        FineWave.SeekRequested+=t=>Seek(t,false);
        FineWave.ZoomChanged+=()=>FineZoomBox.SelectedIndex=FineWave.Span<=.5?0:FineWave.Span<=1?1:2;
        wavePreviewTimer.Tick+=(_,_)=>FlushWavePreview();
        Closed+=(_,_)=>wavePreviewTimer.Stop();
    }

    bool BeginWaveEdit(Waveform source)
    {
        CancelLiveTiming();StopFineAudition();
        if(selected==null||!ApplyEditor())return false;
        UpdateTimingDisplays(selected.Start,selected.End);
        waveEditRow=selected;waveStart=selected.Start;waveEnd=selected.End;waveEditing=true;
        waveSeekTime=selected.Start;wavePreviewDocument=document;wavePreviewRetain=true;waveSeekPending=waveAssPending=false;
        // Cancel once at the start, retaining the last complete displayed frame.
        previewGeneration++;CancelAssPreview();previewDebounce.Stop();previewFrames?.Clear();previewFrames=null;
        wavePreviewTimer.Start();return true;
    }
    void UpdateWaveEdit(Waveform source,double a,double b)
    {
        if(!waveEditing||waveEditRow!=selected)return;
        waveStart=a;waveEnd=b;waveSeekTime=source.EditingStart?a:b;waveSeekPending=true;
        UpdateTimingDisplays(a,b,source);
    }
    void UpdateTimingDisplays(double a,double b,Waveform? source=null)
    {
        bool oldLoading=loading;loading=true;
        StartBox.Text=Timecode.Ass(a);EndBox.Text=Timecode.Ass(b);DurationLabel.Text=$"{b-a:0.00} s";loading=oldLoading;
        Wave.Start=FineWave.Start=a;Wave.End=FineWave.End=b;
        if(source!=Wave)Wave.RefreshSelection();if(FineTimingPanel.IsExpanded&&source!=FineWave)FineWave.RefreshSelection();
        FineBoundaryLabel.Text=$"{(FineEndChoice.IsChecked==true?"终点":"起点")} {Timecode.Ass(FineEndChoice.IsChecked==true?b:a)} · 10 ms 精度";
    }
    void FlushWavePreview()
    {
        if(!waveEditing)return;
        if(waveSeekPending)
        {
            waveSeekPending=false;
            if(Linked.IsChecked==true){WavePreviewSeeks++;Seek(waveSeekTime,false);}
            else waveAssPending=true;
        }
        // Never queue another decoder/render worker behind an in-flight worker.
        if(waveAssPending&&precisePreview&&previewTask.IsCompleted)
        {waveAssPending=false;BeginAssPreview();}
    }
    void EndWaveEdit()
    {
        if(!waveEditing)return;
        wavePreviewTimer.Stop();waveEditing=false;waveSeekPending=waveAssPending=false;
        var row=waveEditRow;waveEditRow=null;
        if(row==null||row!=selected||!document.Rows.Contains(row)){wavePreviewRetain=false;return;}
        if(Math.Abs(row.Start-waveStart)>.004||Math.Abs(row.End-waveEnd)>.004)
        {PushUndo();row.Start=waveStart;row.End=waveEnd;row.Reviewed=false;MarkDirty();RefreshIssues();}
        UpdateTimingDisplays(row.Start,row.End);RefreshFineTiming(true);
        if(Linked.IsChecked==true)Seek(waveSeekTime,false);else ScheduleAssPreview(true);
    }
    void CancelWaveEdit()
    {
        if(!waveEditing)return;
        waveEditing=false;waveEditRow=null;waveSeekPending=waveAssPending=false;wavePreviewTimer.Stop();
    }

    void FinePanelChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        AudioEditorPanel.MinHeight=FineTimingPanel.IsExpanded?760:520;
        if(!FineTimingPanel.IsExpanded)StopFineAudition();else {RefreshFineTiming(true);Dispatcher.BeginInvoke(()=>FineTimingPanel.BringIntoView(),DispatcherPriority.Loaded);}
    }
    void FineChoiceChanged(object sender,RoutedEventArgs e)
    {
        if(!ready||loading)return;
        StopFineAudition();RefreshFineTiming(true);
    }
    void FineOptionsChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready||loading)return;
        bool? audition=fineAudition;RefreshFineTiming(true);
        if(audition is bool selection)StartFineAudition(selection);
    }
    void RefreshFineTiming(bool center)
    {
        if(!ready||!FineTimingPanel.IsExpanded)return;
        FineWave.Peaks=Wave.Peaks;FineWave.Duration=Wave.Duration;FineWave.Gain=Wave.Gain;
        FineWave.Start=waveEditing?waveStart:selected?.Start??0;FineWave.End=waveEditing?waveEnd:selected?.End??.01;
        FineWave.FixedBoundary=FineEndChoice.IsChecked!=true;
        FineWave.Span=FineZoomBox.SelectedIndex switch{0=>.5,1=>1,_=>2};
        double boundary=FineEndChoice.IsChecked==true?FineWave.End:FineWave.Start;
        if(center)FineWave.ViewStart=Math.Max(0,boundary-FineWave.Span/2);
        FineWave.Position=PlaybackPosition;FineWave.InvalidateVisual();
        FineBoundaryLabel.Text=$"{(FineEndChoice.IsChecked==true?"终点":"起点")} {Timecode.Ass(boundary)} · 10 ms 精度";
    }
    void FineNudgeClick(object sender,RoutedEventArgs e)=>FineNudge(int.Parse(((Button)sender).Tag.ToString()!,CultureInfo.InvariantCulture));
    void FineNudge(int milliseconds)
    {
        if(selected==null||!ApplyEditor())return;
        CancelLiveTiming();bool? audition=fineAudition;
        bool end=FineEndChoice.IsChecked==true;
        // Integer centiseconds keep the displayed time equal to the saved ASS time.
        int a=(int)Math.Round(selected.Start*100),b=(int)Math.Round(selected.End*100);
        int value=(end?b:a)+milliseconds/10;
        if(end)b=Math.Clamp(value,a+1,8640000);else a=Math.Clamp(value,0,b-1);
        if(a/100d==selected.Start&&b/100d==selected.End)return;
        PushUndo();selected.Start=a/100d;selected.End=b/100d;selected.Reviewed=false;
        UpdateTimingDisplays(selected.Start,selected.End);MarkDirty();RefreshIssues();RefreshFineTiming(true);
        Status.Text=$"已微调{(end?"终点":"起点")}；可撤销。未启用自动吸附。";
        if(audition is bool selection)StartFineAudition(selection);
        else if(Linked.IsChecked==true)Seek(end?selected.End:selected.Start,false);
    }
    void FineAuditionClick(object sender,RoutedEventArgs e)=>StartFineAudition(((Button)sender).Tag?.ToString()=="selection");
    void StartFineAudition(bool selection)=>TryStartFineAudition(selection);
    bool TryStartFineAudition(bool selection)
    {
        // Timer/compositor restarts must never open a modal validation dialog.
        if(selected==null||!ApplyEditor(false)){StopFineAudition();Status.Text="试听已停止：请修正字幕时间或其他无效字段。";return false;}
        if(!PlaybackReady){StopFineAudition();Status.Text="请先打开媒体，等待音频就绪后再试听。";return false;}
        double context=FineContextBox.SelectedIndex switch{0=>.2,1=>.3,_=>.5};
        double boundary=FineEndChoice.IsChecked==true?selected.End:selected.Start;
        double a=Math.Clamp(selection?selected.Start:boundary-context,0,duration);
        double b=Math.Clamp(selection?selected.End:boundary+context,0,duration);
        if(b-a<.01){StopFineAudition();Status.Text="该范围不在可播放的媒体内，请调整字幕时间。";return false;}
        PlayRange(a,b);if(!playing)return false;
        fineAudition=selection;fineAuditionRow=selected;fineAuditionStart=a;fineAuditionEnd=b;
        Status.Text=$"试听{(selection?"实际选区":"边界前后")} {Timecode.Ass(a)} → {Timecode.Ass(b)} · H 停止";return true;
    }
    bool RestartFineAudition()
    {
        if(fineAudition is not bool selection||fineAuditionRow!=selected||!FineTimingPanel.IsExpanded||FineLoopCheck.IsChecked!=true)return false;
        bool restarted=TryStartFineAudition(selection);if(restarted)FineLoopRestarts++;return restarted;
    }
    void StopFineAudition(){if(fineAudition!=null)Pause();}
    void CancelFineAudition(){fineAudition=null;fineAuditionRow=null;}
}
