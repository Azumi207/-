using System.Windows;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    AssRow? liveTimingRow;
    double liveTimingStart;

    double LiveTimingPosition()
    {
        // ASS timestamps are centiseconds. Never round beyond the media end.
        double limit=Math.Floor(Math.Min(duration,86400)*100)/100;
        return Math.Clamp(Math.Round(PlaybackPosition*100,MidpointRounding.AwayFromZero)/100,0,Math.Max(0,limit));
    }

    void CancelLiveTiming(string? message=null)
    {
        if(liveTimingRow==null)return;
        liveTimingRow=null;Wave.PendingStart=null;Wave.InvalidateVisual();
        LiveTimingHint.Text="F8 标起点 → F9 标终点 · 播放不停";
        if(message!=null)Status.Text=message;
    }

    void LiveCancelClick(object sender,RoutedEventArgs e)=>CancelLiveTiming("已取消待完成的起点，字幕未改动。播放状态不变。");

    void LiveStartClick(object sender,RoutedEventArgs e)
    {
        if(selected==null){Status.Text="请先选择或新增一句字幕。";return;}
        if(!PlaybackReady||!playing){Status.Text="先按 Ctrl+P（或非输入区域的 B）连续播放，再按 F8 标起点。";return;}
        if(Wave.Dragging||FineWave.Dragging||assDragging){Status.Text="请先结束鼠标拖动，再标记起点。";return;}
        double now=LiveTimingPosition();
        if(now>=Math.Min(duration,86400)){Status.Text="已经到达媒体末尾，请从更早的位置开始播放。";return;}
        liveTimingRow=selected;liveTimingStart=now;
        // Promote a bounded audition to continuous playback. Do not touch the
        // active player, Position, Play(), Pause(), or the current editor draft.
        CancelFineAudition();stopAt=duration;
        Wave.PendingStart=now;Wave.InvalidateVisual();
        LiveTimingHint.Text=$"已标起点 {Timecode.Ass(now)} · F9 完成 / Esc 取消";
        Status.Text="起点已暂存，声音继续播放。到句尾按 F9；再次按 F8 可重标起点。";
    }

    void LiveEndClick(object sender,RoutedEventArgs e)
    {
        if(liveTimingRow==null){Status.Text="先在播放中按 F8 标起点，再按 F9 标终点。";return;}
        if(!ReferenceEquals(selected,liveTimingRow)||!document.Rows.Contains(liveTimingRow))
        {CancelLiveTiming("字幕选择已改变，请重新按 F8 标起点。");return;}
        double end=LiveTimingPosition(),start=liveTimingStart;
        if(end<=start){Status.Text="终点必须晚于起点。继续听后按 F9，或按 Esc 取消。";return;}
        // Validation errors must not open a modal dialog or discard typed text.
        if(!ApplyEditor(false)){Status.Text="当前字幕有无效字段，请修正后再按 F9；起点仍保留。";return;}
        PushUndo();var completed=selected!;completed.Start=start;completed.End=end;completed.Reviewed=false;
        CancelLiveTiming();MarkDirty();
        if(LiveNextCheck.IsChecked==true)
        {
            var rows=document.Rows.ToList();int index=rows.IndexOf(completed);
            if(index+1==rows.Count&&end<86400){var next=document.Add(end,Math.Min(86400,end+2),"",completed);next.Layer=completed.Layer;}
            // Refresh selection without SelectRow/Next, which seek to old times.
            RefreshAll(index+1);
        }
        else {LoadEditor();RefreshIssues();}
        Status.Text=$"已标定第 {completed.Number} 句：{Timecode.Ass(start)} → {Timecode.Ass(end)} · 播放继续，可撤销。";
    }
}
