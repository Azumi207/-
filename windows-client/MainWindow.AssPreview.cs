using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Frameflow.Controls;
using Frameflow.Core;
using Frameflow.Media;
namespace Frameflow;
public partial class MainWindow
{
    readonly AssPreviewSurface preciseSurface=new(){IsHitTestVisible=false};
    readonly DispatcherTimer previewDebounce=new(){Interval=TimeSpan.FromMilliseconds(220)};
    CancellationTokenSource? previewCancel;Task previewTask=Task.CompletedTask;AssFrameBuffer? previewFrames;
    AssPreviewWorkspace? previewWorkspace;bool precisePreview,previewClosed;long previewGeneration;string previewFontDirectory="",previewDiagnostics="";double previewStart;
    string PreviewPreferencesPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Frameflow","ass-preview.json");
    void CancelAssPreview(){try{previewCancel?.Cancel();}catch(ObjectDisposedException){}}
    void InitializeAssPreview()
    {
        VideoSurface.Children.Insert(VideoSurface.Children.IndexOf(Overlay),preciseSurface);
        previewDebounce.Tick+=(_,_)=>{previewDebounce.Stop();BeginAssPreview();};
        Loaded+=(_,_)=>{if(!SmokeMode){try{if(File.Exists(PreviewPreferencesPath))previewFontDirectory=System.Text.Json.JsonSerializer.Deserialize<string>(File.ReadAllText(PreviewPreferencesPath))??"";}catch{}SetPrecisePreview(true);}};
        Closed+=(_,_)=>{previewClosed=true;previewDebounce.Stop();CancelAssPreview();};
    }
    void SetPrecisePreview(bool enable)
    {
        precisePreview=enable;PreviewMode.SelectedIndex=enable?0:1;Overlay.ShowText=!enable;Overlay.InvalidateDocument();
        preciseSurface.Visibility=enable?Visibility.Visible:Visibility.Collapsed;
        if(enable)ScheduleAssPreview(true);else{previewGeneration++;previewDebounce.Stop();CancelAssPreview();previewFrames?.Clear();preciseSurface.Clear();PreviewState.Text="轻量排版 · 复杂特效仅原样保留";}
    }
    void PreviewModeChanged(object sender,SelectionChangedEventArgs e){if(ready&&precisePreview!=(PreviewMode.SelectedIndex==0))SetPrecisePreview(PreviewMode.SelectedIndex==0);}
    void ScheduleAssPreview(bool soon=false)
    {
        if(!ready||!precisePreview||previewClosed)return;
        previewGeneration++;CancelAssPreview();previewDebounce.Stop();previewFrames?.Clear();previewFrames=null;preciseSurface.Clear();
        previewDebounce.Interval=TimeSpan.FromMilliseconds(soon?60:350);previewDebounce.Start();
    }
    string PreviewSnapshot()
    {
        string original=document.Serialize();if(!draftDirty||selected==null)return original;
        // A preview of a draft is a private copy, not ApplyEditor()/undo/save.
        var copy=SubtitleDocument.Parse(original);var row=copy.Rows.ElementAt(selected.Number-1);
        double start=Timecode.Parse(StartBox.Text),end=Timecode.Parse(EndBox.Text);if(end<=start)throw new FormatException("请先修正字幕起止时间。");
        if(!int.TryParse(LayerBox.Text,out var layer)||layer<0)throw new FormatException("层应为非负整数。");
        row.Text=TextEditor.Text.Replace("\r\n",@"\N").Replace("\n",@"\N");row.Start=start;row.End=end;row.Layer=layer.ToString();row.Actor=ActorBox.Text;row.Style=StyleBox.SelectedItem?.ToString()??row.Style;row.Comment=CommentBox.IsChecked==true;return copy.Serialize();
    }
    void BeginAssPreview()
    {
        if(!precisePreview||previewClosed)return;
        if(!VideoAvailable){preciseSurface.Clear();PreviewState.Text="打开视频后显示 libass 预览";return;}
        string snapshot;try{snapshot=PreviewSnapshot();}catch(Exception e){PreviewState.Text="预览未更新："+e.Message;return;}
        long generation=previewGeneration;var previous=previewTask;CancelAssPreview();var cancellation=new CancellationTokenSource();previewCancel=cancellation;var token=cancellation.Token;
        int width=(int)sourceVideoWidth,height=(int)sourceVideoHeight;double start=VideoPositionSeconds;previewStart=start;bool continuous=playing&&!AudioMode;string fonts=previewFontDirectory;
        var buffer=new AssFrameBuffer();var priorBuffer=previewFrames;previewFrames=buffer;PreviewState.Text="正在准备 libass 预览…";preciseSurface.Frame=Overlay.ContentRect;
        var installed=new HashSet<string>(FontBox.Items.OfType<string>(),StringComparer.OrdinalIgnoreCase);
        previewTask=Task.Run(async()=>
        {
            try
            {
                try{await previous;}catch{}priorBuffer?.Clear();token.ThrowIfCancellationRequested();
                using var workspace=new AssPreviewWorkspace(snapshot,fonts,token);previewWorkspace=workspace;bool first=true;
                var missing=BurnInEncoder.ReferencedFonts(snapshot).Where(n=>!installed.Contains(n.TrimStart('@'))).ToArray();
                try
                {
                    await workspace.RenderAsync(width,height,start,continuous,async frame=>
                    {
                        await buffer.AddAsync(frame,token);
                        if(first){first=false;await Dispatcher.InvokeAsync(()=>{if(generation==previewGeneration&&!previewClosed){AdvanceAssPreview(VideoPositionSeconds);PreviewState.Text="ASS · libass";PreviewState.ToolTip="同一引擎与字体规则；最高 30 fps / 720p 显示，先按源尺寸渲染再缩放。定位与压制以成片对照为准。";bool warning=missing.Length>0||workspace.Diagnostics.Contains("Glyph");PreviewFontsButton.Content=warning?"字体 !":"字体";PreviewFontsButton.SetResourceReference(ForegroundProperty,warning?"WarningBrush":"MutedBrush");PreviewFontsButton.ToolTip=warning?"字体待确认："+string.Join("、",missing)+"。附加字体、别名、缺字及实际替代结果请点此检查；不自动修改字幕字体。":"查看实际字体选择和附加字体目录";}},DispatcherPriority.Background);}
                    },token);
                }
                finally{previewDiagnostics=workspace.Diagnostics;}
            }
            catch(OperationCanceledException){}
            catch(Exception e){if(!token.IsCancellationRequested)await Dispatcher.InvokeAsync(()=>{if(generation==previewGeneration&&!previewClosed){PreviewState.Text="预览未完成 · 点字体查看 / 可切轻量模式";PreviewState.ToolTip=e.Message;Status.Text=e.Message;}},DispatcherPriority.Background);}
            finally{if(token.IsCancellationRequested)buffer.Clear();cancellation.Dispose();}
        });
    }
    void AdvanceAssPreview(double time)
    {
        if(!precisePreview)return;preciseSurface.Frame=Overlay.ContentRect;
        using var frame=previewFrames?.Take(time);if(frame!=null)preciseSurface.Present(frame);
    }
    void PreviewFontsClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        string snapshot=PreviewSnapshot();var d=new PreviewFontsDialog(snapshot,previewFontDirectory,previewWorkspace?.Diagnostics??previewDiagnostics){Owner=this};
        if(d.ShowDialog()==true){previewFontDirectory=d.DirectoryBox.Text.Trim();if(!SmokeMode){Directory.CreateDirectory(Path.GetDirectoryName(PreviewPreferencesPath)!);File.WriteAllText(PreviewPreferencesPath,System.Text.Json.JsonSerializer.Serialize(previewFontDirectory));}ScheduleAssPreview(true);}
    });
    void ComparePreviewClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(!VideoAvailable||dummyVideo||mediaPath==null)throw new InvalidOperationException("请先打开真实视频；空白画布不能压制对照。");
        if(!ApplyEditor())return;Pause();var d=new PreviewCompareDialog(document.Serialize(),mediaPath,previewFontDirectory,Math.Max(0,VideoPositionSeconds)){Owner=this};d.ShowDialog();
    });
}
