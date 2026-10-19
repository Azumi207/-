using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Controls;
using Frameflow.Core;
using Microsoft.Win32;

namespace Frameflow;
public partial class MainWindow
{
    bool dummyVideo;double dummyDuration,dummyPosition,sourceVideoWidth=1920,sourceVideoHeight=1080,videoZoom,aspectOverride;readonly Stopwatch dummyClock=new();Window? detachedVideo;
    bool VideoAvailable=>mediaReady&&(dummyVideo||Player.NaturalVideoWidth>0);
    double VideoPositionSeconds=>dummyVideo?dummyPosition+dummyClock.Elapsed.TotalSeconds*Player.SpeedRatio:Player.Position.TotalSeconds;
    double VideoDuration=>dummyVideo?dummyDuration:Player.NaturalDuration.HasTimeSpan?Player.NaturalDuration.TimeSpan.TotalSeconds:duration;
    string? multimediaRecentOverride;
    string MultimediaRecentPath=>multimediaRecentOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Frameflow","media-menus-recent.json");
    void RememberMedia(string path,string kind){if(SmokeMode&&multimediaRecentOverride==null)return;try{RecentFiles.Add(MultimediaRecentPath,path,kind);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Status.Text="操作完成，但最近记录保存失败："+ex.Message;}}
    void VideoOperationClick(object sender,RoutedEventArgs e)=>Guard(()=>RunVideoOperation((string)((MenuItem)sender).Tag));
    void RunVideoOperation(string op)
    {
        if(op=="open"){var p=new OpenFileDialog{Filter="视频|*.mp4;*.mkv;*.avi;*.mov;*.m4v;*.wmv|所有文件|*.*"};if(p.ShowDialog(this)==true)OpenVideoFile(p.FileName);return;}
        if(op=="close"){CloseVideoOnly();return;}
        if(op=="dummy"){var d=new DummyVideoDialog{Owner=this};if(d.ShowDialog()==true){var v=d.Values();CreateDummyVideo(v.Width,v.Height,v.Fps,v.Duration,v.Color,v.Checker);}return;}
        if(op=="details"){ShowMediaDetails();return;}
        if(op=="detach"){SetVideoDetached(detachedVideo==null);return;}
        if(op.StartsWith("zoom-")){videoZoom=op=="zoom-fit"?0:double.Parse(op[5..],CultureInfo.InvariantCulture)/100;UpdateVideoPresentation();return;}
        if(op.StartsWith("aspect-")){string? value=op=="aspect-custom"?Prompt("视频显示比例","宽:高（只影响预览，不改脚本或媒体）"):op[7..];if(value!=null)SetVideoAspect(value);return;}
        if(op=="overscan"){SafeGuides.Enabled=!SafeGuides.Enabled;OverscanItem.IsChecked=SafeGuides.Enabled;UpdateVideoPresentation();return;}
        if(op.StartsWith("time-")||op.StartsWith("key-")){FrameFileOperation(op);return;}
        if(!VideoAvailable)throw new InvalidOperationException("请先打开视频或创建空白视频。");
        if(op=="jump"){var d=new JumpVideoDialog(VideoPositionSeconds,timingFrames){Owner=this};if(d.ShowDialog()==true)SeekVideoOnly(d.Position);}
        else if(op=="start"&&selected!=null)SeekVideoOnly(selected.Start);
        else if(op=="end"&&selected!=null)SeekVideoOnly(selected.End);
        else if(op is "frame-prev" or "frame-next"){if(timingFrames==null)throw new InvalidOperationException("请先加载帧时间表。");int i=Math.Clamp(timingFrames.At(VideoPositionSeconds)+(op=="frame-prev"?-1:1),0,timingFrames.Count-1);SeekVideoOnly(timingFrames.Starts[i]);}
        else if(op=="snapshot"){var p=new SaveFileDialog{Filter="PNG 预览截图|*.png",FileName="字幕预览.png"};if(p.ShowDialog(this)==true)SaveVideoPreview(p.FileName);}
    }
    void OpenVideoFile(string path)
    {
        if(externalAudioPath==null){OpenMedia(path);return;}
        Pause();timingFrames=null;dummyVideo=false;VideoSurface.Background=new SolidColorBrush(Color.FromRgb(20,29,40));mediaReady=false;Player.Close();Player.Source=null;mediaPath=Path.GetFullPath(path);Player.Source=new Uri(mediaPath);MediaLabel.Text=Path.GetFileName(path);Player.Play();Player.Pause();
    }
    void CloseVideoOnly()
    {
        Pause();if(mediaPath!=null&&!audioClosed&&externalAudioPath==null){externalAudioPath=mediaPath;AudioOnly.IsChecked=true;AudioOnly.IsEnabled=false;if(!audioReady)PrepareAudio();}
        dummyVideo=false;dummyClock.Reset();timingFrames=null;mediaReady=false;Player.Close();Player.Source=null;mediaPath=null;MediaLabel.Text="未打开视频";MediaPlaceholder.Text="视频已关闭（字幕与独立音频保留）";MediaPlaceholder.Visibility=Visibility.Visible;VideoSurface.Background=new SolidColorBrush(Color.FromRgb(20,29,40));Overlay.VideoWidth=document.PlayResX;Overlay.VideoHeight=document.PlayResY;UpdateVideoPresentation();
    }
    void CreateDummyVideo(int width,int height,double fps,double seconds,Color color,bool checker)
    {
        if(width<16||width>7680||height<16||height>4320)throw new FormatException("无效画布尺寸。");var frames=FrameTimeline.Cfr(fps,seconds).WithKeys([0],"虚拟空白视频，仅首帧关键帧");Pause();CloseVideoOnly();dummyVideo=true;dummyDuration=seconds;dummyPosition=0;dummyClock.Reset();mediaReady=true;sourceVideoWidth=width;sourceVideoHeight=height;duration=seconds;timingFrames=frames;aspectOverride=videoZoom=0;MediaPlaceholder.Visibility=Visibility.Collapsed;MediaLabel.Text=$"空白视频 · {width}×{height} · {fps:0.###} fps";
        Brush brush=new SolidColorBrush(color);if(checker){var group=new DrawingGroup();using(var dc=group.Open()){dc.DrawRectangle(brush,null,new Rect(0,0,32,32));var alt=new SolidColorBrush(Color.FromRgb((byte)(color.R/2),(byte)(color.G/2),(byte)(color.B/2)));dc.DrawRectangle(alt,null,new Rect(0,0,16,16));dc.DrawRectangle(alt,null,new Rect(16,16,16,16));}group.Freeze();brush=new DrawingBrush(group){TileMode=TileMode.Tile,Viewport=new Rect(0,0,32,32),ViewportUnits=BrushMappingMode.Absolute};}VideoSurface.Background=brush;SeekBar.Maximum=seconds;UpdateVideoPresentation();SeekVideoOnly(0);Status.Text="虚拟视频已创建；可播放时间轴、排版、截图，不含音轨。";
    }
    void SeekVideoOnly(double position)
    {
        if(!VideoAvailable)throw new InvalidOperationException("未打开视频。");if(!double.IsFinite(position)||position<0||position>VideoDuration)throw new FormatException("定位超出视频时长。");Pause();if(dummyVideo)dummyPosition=position;else Player.Position=TimeSpan.FromSeconds(position);Overlay.UpdateTime(position);if(!AudioMode)UpdatePlaybackUi(position,true,Linked.IsChecked==true);Status.Text="视频位置："+Timecode.Ass(position)+(dummyVideo?"（虚拟）":"（系统定位，非逐帧解码保证）");
    }
    void SetVideoAspect(string value)
    {
        if(value=="default")aspectOverride=0;else{var a=value.Split(':');double r=a.Length==2?double.Parse(a[0],CultureInfo.InvariantCulture)/double.Parse(a[1],CultureInfo.InvariantCulture):double.Parse(value,CultureInfo.InvariantCulture);if(!double.IsFinite(r)||r<.2||r>5)throw new FormatException("显示宽高比应在 0.2～5。");aspectOverride=r;}UpdateVideoPresentation();
    }
    void VideoViewportChanged(object sender,SizeChangedEventArgs e)=>UpdateVideoPresentation();
    void UpdateVideoPresentation()
    {
        if(!ready||VideoViewport==null)return;double ratio=aspectOverride>0?aspectOverride:sourceVideoWidth/Math.Max(1,sourceVideoHeight);Overlay.VideoWidth=ratio;Overlay.VideoHeight=1;
        // Fit mode must not feed the presence of a scrollbar back into its own size.
        VideoViewport.HorizontalScrollBarVisibility=VideoViewport.VerticalScrollBarVisibility=videoZoom>0?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
        VideoSurface.Width=videoZoom>0?sourceVideoWidth*videoZoom:Math.Max(1,VideoViewport.ActualWidth-2);VideoSurface.Height=videoZoom>0?sourceVideoWidth/ratio*videoZoom:Math.Max(1,VideoViewport.ActualHeight-2);UpdateVideoRectangle();
    }
    void UpdateVideoRectangle(){if(!ready)return;var rect=Overlay.ContentRect;Player.Width=rect.Width;Player.Height=rect.Height;SafeGuides.Frame=rect;SafeGuides.InvalidateVisual();Overlay.InvalidateDocument();}
    void SetVideoDetached(bool detach)
    {
        if(detach&&detachedVideo==null){MediaSplitGrid.Children.Remove(VideoPanel);var window=new Window{Title="帧语 · 视频预览",Width=900,Height=620,MinWidth=400,MinHeight=260,Owner=this,ShowActivated=!SmokeMode,Content=VideoPanel};detachedVideo=window;window.PreviewKeyDown+=WindowKeyDown;window.Closed+=(_,_)=>{window.Content=null;detachedVideo=null;if(!MediaSplitGrid.Children.Contains(VideoPanel))MediaSplitGrid.Children.Add(VideoPanel);DetachVideoItem.IsChecked=false;ApplyWorkspaceLayout();UpdateVideoPresentation();};window.Show();}
        else if(!detach&&detachedVideo!=null)detachedVideo.Close();DetachVideoItem.IsChecked=detachedVideo!=null;ApplyWorkspaceLayout();UpdateVideoPresentation();
    }
    void SaveVideoPreview(string path)
    {
        if(!VideoAvailable)throw new InvalidOperationException("没有视频预览。");Pause();VideoSurface.UpdateLayout();int width=(int)Math.Ceiling(VideoSurface.ActualWidth),height=(int)Math.Ceiling(VideoSurface.ActualHeight);if(width<1||height<1||(long)width*height>32_000_000)throw new InvalidOperationException("截图画布过大，请降低预览缩放。");bool edit=Overlay.Editing;var cross=Crosshair.Visibility;bool guides=SafeGuides.Enabled;
        try{Overlay.Editing=false;Crosshair.Visibility=Visibility.Hidden;SafeGuides.Enabled=false;Overlay.InvalidateDocument();SafeGuides.InvalidateVisual();VideoSurface.UpdateLayout();var bmp=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bmp.Render(VideoSurface);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var memory=new MemoryStream();encoder.Save(memory);SubtitleDocument.AtomicSave(path,memory.ToArray());}finally{Overlay.Editing=edit;Crosshair.Visibility=cross;SafeGuides.Enabled=guides;Overlay.InvalidateDocument();SafeGuides.InvalidateVisual();}Status.Text="预览截图已保存（不是原始解码帧，不等于压制）。";
    }
    string MediaDetailsText()=>"来源："+(dummyVideo?"虚拟空白视频":mediaPath??"无")+"\n画面："+sourceVideoWidth+" × "+sourceVideoHeight+"\n视频时长："+VideoDuration.ToString("0.###",CultureInfo.InvariantCulture)+" 秒\n系统解码器：Windows MediaElement / Media Foundation\n"+(timingFrames==null?"帧时间表未加载；不猜测帧率。":$"帧表：{timingFrames.Count} 帧 / {timingFrames.Keyframes.Length} 关键帧\n{timingFrames.Source}")+"\n独立音频："+(externalAudioPath??"无");
    void ShowMediaDetails(){var d=new MediaInfoDialog(MediaDetailsText()){Owner=this};d.ShowDialog();}
    void FrameFileOperation(string op)
    {
        if(op=="time-close"){timingFrames=null;Status.Text="时间码与依赖的关键帧已清除。";return;}
        if(op=="key-close"){if(timingFrames!=null)timingFrames=timingFrames.WithKeys([],"清除关键帧");Status.Text="关键帧已清除，帧时间保留。";return;}
        bool keys=op.StartsWith("key"),save=op.EndsWith("save");if(save){if(timingFrames==null||keys&&timingFrames.Keyframes.Length==0)throw new InvalidOperationException("没有可保存的数据。");var p=new SaveFileDialog{Filter="文本文件|*.txt",FileName=keys?"keyframes.txt":"timecodes.txt"};if(p.ShowDialog(this)==true)SaveFrameData(p.FileName,keys);}
        else{var p=new OpenFileDialog{Filter="帧数据|*.txt;*.timecodes;*.keyframes|所有文件|*.*"};if(p.ShowDialog(this)==true)OpenFrameData(p.FileName,keys);}
    }
    void SaveFrameData(string path,bool keys){if(timingFrames==null)throw new InvalidOperationException("没有帧表。");SubtitleDocument.AtomicSave(path,Encoding.UTF8.GetBytes(keys?timingFrames.KeyframeText():timingFrames.Timecodes()));RememberMedia(path,keys?"keyframe":"timecode");}
    void OpenFrameData(string path,bool keys,double? confirmedEnd=null)
    {
        if(new FileInfo(path).Length>64*1024*1024)throw new IOException("帧数据文件超过 64 MB。");string body=File.ReadAllText(path);
        if(keys){if(timingFrames==null)throw new InvalidOperationException("请先加载时间码。");timingFrames=timingFrames.WithKeys(FrameTimeline.ParseKeyframes(body),"导入关键帧 v1");}
        else{double end=confirmedEnd??0;if(confirmedEnd==null){string? value=Prompt("确认末帧终点","v2 不含最后一帧的结束时刻。请输入秒数，视频参考值："+VideoDuration.ToString("0.###",CultureInfo.InvariantCulture));if(value==null)return;end=Timecode.Parse(value);}timingFrames=FrameTimeline.ParseV2(body,end);}RememberMedia(path,keys?"keyframe":"timecode");Status.Text="帧数据已加载。";
    }
    void MediaRecentOpened(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.Source))return;var menu=(MenuItem)sender;string kind=(string)menu.Tag;menu.Items.Clear();foreach(var entry in RecentFiles.Load(MultimediaRecentPath).Where(r=>r.Kind==kind)){var item=new MenuItem{Header=Path.GetFileName(entry.Path),ToolTip=entry.Path,IsEnabled=File.Exists(entry.Path)};item.Click+=async(_,_)=>{try{if(kind=="video")OpenVideoFile(entry.Path);else if(kind=="audio")await LoadIndependentAudioAsync(entry.Path,true);else OpenFrameData(entry.Path,kind=="keyframe");}catch(Exception ex){Status.Text=ex.Message;}};menu.Items.Add(item);}if(menu.Items.Count==0)menu.Items.Add(new MenuItem{Header="暂无记录",IsEnabled=false});
    }
    void MediaMenusOpened(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.Source))return;void Update(MenuItem parent){foreach(var item in parent.Items.OfType<MenuItem>()){if(item.Tag is string op){if(parent==VideoMenu||sender==VideoMenu)item.IsEnabled=op is "open" or "dummy" or "detach" or "overscan"||op.StartsWith("zoom-")||op.StartsWith("aspect-")||op.StartsWith("time-")||op.StartsWith("key-")||op is "video" or "timecode" or "keyframe"||VideoAvailable;else if(op=="from-video")item.IsEnabled=mediaPath!=null&&VideoAvailable&&!dummyVideo;else if(op=="clip")item.IsEnabled=selected!=null&&!audioClosed&&(externalAudioPath!=null||mediaPath!=null);else if(op is "play" or "stop")item.IsEnabled=PlaybackReady;}Update(item);}}Update((MenuItem)sender);WaveformItem.IsChecked=!Wave.SpectrumMode;SpectrumItem.IsChecked=Wave.SpectrumMode;AudioMuteItem.IsChecked=playbackMuted;
    }
}
