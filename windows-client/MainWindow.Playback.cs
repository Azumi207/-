using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Frameflow.Core;
using Frameflow.Media;

namespace Frameflow;
public partial class MainWindow
{
    readonly MediaPlayer AudioPlayer=new();
    CancellationTokenSource? audioCancel;bool audioReady,audioPreparing;
    string? audioCachePath;readonly List<string> audioCaches=[];
    double pendingAudioPosition;
    bool AudioMode=>AudioOnly.IsChecked==true;
    double PlaybackPosition=>AudioMode?(audioReady?AudioPlayer.Position.TotalSeconds:pendingAudioPosition):VideoPositionSeconds;
    bool PlaybackReady=>AudioMode?audioReady:mediaReady;
    public int PlaybackUiUpdates {get;private set;}
    bool renderingPlayback;double lastRenderMs=double.NegativeInfinity,lastTextUpdate=double.NaN;
    public int PlaybackRenderCallbacks {get;private set;}
    void StartPlaybackRendering(){ScheduleAssPreview(true);if(renderingPlayback)return;renderingPlayback=true;lastRenderMs=double.NegativeInfinity;CompositionTarget.Rendering+=PlaybackRendering;}
    void StopPlaybackRendering(){if(!renderingPlayback)return;renderingPlayback=false;CompositionTarget.Rendering-=PlaybackRendering;}
    void PlaybackRendering(object? sender,EventArgs e)
    {
        if(!playing||!PlaybackReady||scrubbing||WindowState==WindowState.Minimized)return;
        double frameMs=((RenderingEventArgs)e).RenderingTime.TotalMilliseconds;
        if(frameMs==lastRenderMs)return;lastRenderMs=frameMs;PlaybackRenderCallbacks++;
        double t=PlaybackPosition;if(t>=stopAt){if(RestartFineAudition())return;Pause();}UpdatePlaybackUi(t,!AudioMode,Follow.IsChecked==true);
    }

    void InitializePlayback()
    {
        SeekBar.ScrubCompleted+=(_,_)=>scrubbing=false;
        AudioPlayer.MediaOpened+=(_,_)=>{audioReady=true;audioPreparing=false;AudioPlayer.Position=TimeSpan.FromSeconds(pendingAudioPosition);AudioPlayer.SpeedRatio=Player.SpeedRatio;Status.Text=AudioMode?"纯音频已就绪 · 视频保持暂停，按 S 试听。":"音频缓存已就绪。";};
        AudioPlayer.MediaEnded+=(_,_)=>{if(AudioMode&&!RestartFineAudition())Pause();};
        AudioPlayer.MediaFailed+=(_,e)=>{audioReady=audioPreparing=false;Pause();Status.Text="纯音频打开失败："+e.ErrorException.Message;};
    }
    async void PrepareAudio()
    {
        if(audioReady||audioPreparing||audioClosed||externalAudioPath==null&&mediaPath==null)return;
        audioPreparing=true;var cancel=new CancellationTokenSource();audioCancel=cancel;var token=cancel.Token;
        var path=Path.Combine(Path.GetTempPath(),"Frameflow-audio-"+Guid.NewGuid().ToString("N")+".wav");audioCaches.Add(path);string source=externalAudioPath??mediaPath!;
        Status.Text="正在准备纯音频缓存…视频已暂停；准备好后按 S 试听。";
        try
        {
            await Task.Run(()=>AudioPeaks.Decode(source,token,null,path),token);
            if(token.IsCancellationRequested){TryDeleteAudio(path);return;}
            audioCachePath=path;AudioPlayer.IsMuted=SmokeMode||playbackMuted;AudioPlayer.Open(new Uri(path));
        }
        catch(OperationCanceledException){TryDeleteAudio(path);}
        catch(Exception ex){TryDeleteAudio(path);if(!token.IsCancellationRequested){audioPreparing=false;Status.Text="无法准备纯音频："+ex.Message+" 可取消勾选，继续使用视频模式。";}}
    }
    void AudioOnlyChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        // The CheckBox already has its new value; read the previous player's clock.
        double position=AudioMode?VideoPositionSeconds:audioReady?AudioPlayer.Position.TotalSeconds:pendingAudioPosition;
        Pause();pendingAudioPosition=position;if(AudioMode)audioClosed=false;ApplyAudioVolume();
        if(AudioMode){if(audioReady)AudioPlayer.Position=TimeSpan.FromSeconds(position);else PrepareAudio();Status.Text=audioReady?"仅播放音频 · 视频停在当前帧。":"正在准备纯音频；视频保持暂停。";}
        else {Seek(position);Status.Text="已恢复音视频播放，位置已同步。";}
    }
    static void TryDeleteAudio(string path){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    void CloseAudio()
    {
        spectrumCancel?.Cancel();audioCancel?.Cancel();AudioPlayer.Close();audioReady=audioPreparing=false;audioCachePath=null;
        foreach(var path in audioCaches)TryDeleteAudio(path);audioCaches.Clear();
    }
    void VideoPointerMoved(object sender,MouseEventArgs e)=>Crosshair.SetPointer(e.GetPosition(Overlay),Overlay.ContentRect,document.PlayResX,document.PlayResY);
    void VideoPointerLeft(object sender,MouseEventArgs e)=>Crosshair.SetPointer(null,Overlay.ContentRect,document.PlayResX,document.PlayResY);
    void VideoSizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(!ready)return;
        UpdateVideoRectangle();Crosshair.SetPointer(VideoSurface.IsMouseOver?Mouse.GetPosition(Overlay):null,Overlay.ContentRect,document.PlayResX,document.PlayResY);
    }
    void UpdatePlaybackUi(double t,bool video,bool follow)
    {
        PlaybackUiUpdates++;timerSeeking=true;SeekBar.Value=t;timerSeeking=false;
        if(!playing||double.IsNaN(lastTextUpdate)||Math.Abs(t-lastTextUpdate)>=.1){PositionLabel.Text=Timecode.Ass(t);lastTextUpdate=t;}
        if(video){Overlay.UpdateTime(t);AdvanceAssPreview(t);}
        Wave.Position=t;if(FineTimingPanel.IsExpanded)FineWave.Position=t;if(follow&&!Wave.Dragging&&!FineWave.Dragging&&fineAudition==null)EnsureWave(t);
    }
    void Tick()
    {
        if(!playing||!PlaybackReady||scrubbing)return;
        // Timer is only the range-end safety check (also works while minimized).
        // Display updates follow the compositor, not a 40ms dispatcher timer.
        double t=PlaybackPosition;if(t>=stopAt){if(RestartFineAudition())return;Pause();UpdatePlaybackUi(t,!AudioMode,false);}
    }
    void Seek(double t,bool follow=true)
    {
        CancelFineAudition();CancelLiveTiming("已跳转播放位置，待完成的起点已取消；请重新按 F8。");
        t=Math.Clamp(t,0,Math.Max(duration,selected?.End??0));
        if(AudioMode){pendingAudioPosition=t;if(audioReady)AudioPlayer.Position=TimeSpan.FromSeconds(t);}
        else if(dummyVideo){dummyPosition=t;if(dummyClock.IsRunning)dummyClock.Restart();}else if(mediaReady)Player.Position=TimeSpan.FromSeconds(t);
        UpdatePlaybackUi(t,!AudioMode,follow&&Linked.IsChecked==true);if(!AudioMode)ScheduleAssPreview(true);
    }
    void Pause(){CancelFineAudition();CancelLiveTiming("播放已停止，待完成的起点已取消；字幕未改动。");if(dummyVideo){dummyPosition=VideoPositionSeconds;dummyClock.Reset();}playing=false;timer.Stop();StopPlaybackRendering();Player.Pause();AudioPlayer.Pause();PlayButton.Content="播放";stopAt=double.PositiveInfinity;ScheduleAssPreview(true);}
    void PlayRange(double a,double b)
    {
        if(!PlaybackReady){if(AudioMode){PrepareAudio();Status.Text="纯音频尚未就绪，请等待音轨准备完成。";}else Status.Text="请先打开可播放的媒体。";return;}
        if(!ApplyEditor())return;Seek(a);stopAt=Math.Min(b,duration);
        if(AudioMode){Player.Pause();AudioPlayer.Play();}else{AudioPlayer.Pause();if(dummyVideo)dummyClock.Restart();else Player.Play();}
        playing=true;timer.Start();StartPlaybackRendering();PlayButton.Content="暂停";
    }
}
