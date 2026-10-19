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
    bool assDragging,assDragRetain,assDragTranslateReady,shownDragSimple,pendingDragSimple;AssRow? assDragRow;SubtitleDocument? assDragDocument;
    double shownDragX,shownDragY,pendingDragX,pendingDragY;string shownSnapshot="",pendingSnapshot="";
    NativeAssRenderer? dragRenderer;bool assDragPending;string dragRendererError="";int previewFontsRevision,dragFontsRevision=-1;
    string PreviewPreferencesPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Frameflow","ass-preview.json");
    void CancelAssPreview(){try{previewCancel?.Cancel();}catch(ObjectDisposedException){}}
    void InitializeAssPreview()
    {
        VideoSurface.Children.Insert(VideoSurface.Children.IndexOf(Overlay),preciseSurface);
        previewDebounce.Tick+=(_,_)=>{if(assDragging&&!previewTask.IsCompleted)return;previewDebounce.Stop();BeginAssPreview();};
        Loaded+=(_,_)=>{if(!SmokeMode){try{if(File.Exists(PreviewPreferencesPath))previewFontDirectory=System.Text.Json.JsonSerializer.Deserialize<string>(File.ReadAllText(PreviewPreferencesPath))??"";}catch{}SetPrecisePreview(true);}};
        Closed+=(_,_)=>{previewClosed=true;previewDebounce.Stop();CancelAssPreview();_ = previewTask.ContinueWith(_=>{dragRenderer?.Dispose();dragRenderer=null;});};
    }
    void SetPrecisePreview(bool enable)
    {
        if(!enable){assDragging=assDragRetain=assDragPending=false;assDragRow=null;preciseSurface.SetOffset(0,0);}
        precisePreview=enable;PreviewMode.SelectedIndex=enable?0:1;Overlay.ShowText=!enable;Overlay.InvalidateDocument();
        preciseSurface.Visibility=enable?Visibility.Visible:Visibility.Collapsed;
        if(enable)ScheduleAssPreview(true);else{previewGeneration++;previewDebounce.Stop();CancelAssPreview();previewFrames?.Clear();preciseSurface.Clear();PreviewState.Text="轻量排版 · 复杂特效仅原样保留";}
    }
    void PreviewModeChanged(object sender,SelectionChangedEventArgs e){if(ready&&precisePreview!=(PreviewMode.SelectedIndex==0))SetPrecisePreview(PreviewMode.SelectedIndex==0);}
    void ScheduleAssPreview(bool soon=false)
    {
        if(!ready||!precisePreview||previewClosed)return;
        if(assDragRetain&&!assDragging&&(document!=assDragDocument||!VideoAvailable||Math.Abs(VideoPositionSeconds-preciseSurface.PresentedTime)>.08))assDragRetain=false;
        // During a drag, coalesce pointer updates rather than cancel/debounce on
        // every mouse move. The in-flight frame completes; the next uses latest pos.
        if(assDragging){assDragPending=true;if(previewTask.IsCompleted){assDragPending=false;BeginAssPreview();}return;}
        previewGeneration++;CancelAssPreview();previewDebounce.Stop();previewFrames?.Clear();previewFrames=null;if(!assDragRetain)preciseSurface.Clear();
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
        int width=(int)sourceVideoWidth,height=(int)sourceVideoHeight,fontRevision=previewFontsRevision;double start=VideoPositionSeconds;previewStart=start;bool continuous=playing&&!AudioMode,drag=assDragging;string fonts=previewFontDirectory;
        pendingSnapshot=snapshot;pendingDragSimple=false;
        if(!draftDirty&&selected!=null)
        {
            var visible=document.Rows.Where(r=>!r.Comment&&r.Start<=start&&start<r.End).Take(2).ToArray();
            // Never translate a whole composite containing other subtitles, or
            // fixed masks/origins/transforms. Those use fresh full-scene libass frames.
            pendingDragSimple=visible.Length==1&&visible[0]==selected&&selected.Effect.Length==0&&!System.Text.RegularExpressions.Regex.IsMatch(selected.Text,@"\\(?:i?clip|move|org|t)\s*\(",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var pos=Overlay.Position(selected);pendingDragX=pos.X;pendingDragY=pos.Y;
        }
        var buffer=new AssFrameBuffer();var priorBuffer=previewFrames;previewFrames=buffer;PreviewState.Text=assDragging?"拖动字幕 · ASS":"正在准备 libass 预览…";preciseSurface.Frame=Overlay.ContentRect;
        var installed=new HashSet<string>(FontBox.Items.OfType<string>(),StringComparer.OrdinalIgnoreCase);
        previewTask=Task.Run(async()=>
        {
            try
            {
                try{await previous;}catch{}priorBuffer?.Clear();token.ThrowIfCancellationRequested();
                if(drag)
                {
                    PrepareDragRenderer(fonts,fontRevision);
                    if(dragRenderer!=null)
                    {
                        var frame=dragRenderer.Render(snapshot,width,height,start,token);await buffer.AddAsync(frame,token);
                        await Dispatcher.InvokeAsync(()=>{if(generation==previewGeneration&&!previewClosed){AdvanceAssPreview(start);PreviewState.Text="ASS 实时定位 · 松手精确校准";PreviewState.ToolTip="常驻 libass / 字体缓存；拖动按显示尺寸渲染完整 ASS，松手后以原尺寸压制引擎校准。";}},DispatcherPriority.Render);
                        return;
                    }
                }
                using var workspace=new AssPreviewWorkspace(snapshot,fonts,token);previewWorkspace=workspace;bool first=true;
                var missing=BurnInEncoder.ReferencedFonts(snapshot).Where(n=>!installed.Contains(n.TrimStart('@'))).ToArray();
                try
                {
                    await workspace.RenderAsync(width,height,start,continuous,async frame=>
                    {
                        await buffer.AddAsync(frame,token);
                        if(first){first=false;await Dispatcher.InvokeAsync(()=>{if(generation==previewGeneration&&!previewClosed){AdvanceAssPreview(VideoPositionSeconds);PreviewState.Text=assDragging?"兼容定位 · 实时组件不可用":"ASS · libass";PreviewState.ToolTip="同一引擎与字体规则；最高 30 fps / 720p 显示，先按源尺寸渲染再缩放。定位与压制以成片对照为准。";bool warning=missing.Length>0||workspace.Diagnostics.Contains("Glyph");PreviewFontsButton.Content=warning?"字体 !":"字体";PreviewFontsButton.SetResourceReference(ForegroundProperty,warning?"WarningBrush":"MutedBrush");PreviewFontsButton.ToolTip=warning?"字体待确认："+string.Join("、",missing)+"。附加字体、别名、缺字及实际替代结果请点此检查；不自动修改字幕字体。":"查看实际字体选择和附加字体目录";}},DispatcherPriority.Background);}
                    },token);
                }
                finally{previewDiagnostics=workspace.Diagnostics;}
                // Warm font/shaping caches on the worker while the paused exact
                // frame is already visible; no synchronous font scan on pointer down.
                if(!continuous&&!drag&&!token.IsCancellationRequested){PrepareDragRenderer(fonts,fontRevision);if(dragRenderer!=null){using var warm=dragRenderer.Render(snapshot,width,height,start,token);}}
            }
            catch(OperationCanceledException){}
            catch(Exception e){if(!token.IsCancellationRequested)await Dispatcher.InvokeAsync(()=>{if(generation==previewGeneration&&!previewClosed){PreviewState.Text="预览未完成 · 点字体查看 / 可切轻量模式";PreviewState.ToolTip=e.Message;Status.Text=e.Message;}},DispatcherPriority.Background);}
            finally{if(token.IsCancellationRequested)buffer.Clear();cancellation.Dispose();}
        });
        _ = previewTask.ContinueWith(_=>Dispatcher.BeginInvoke(()=>
        {
            if(!previewClosed&&precisePreview&&assDragging&&assDragPending&&previewTask.IsCompleted){assDragPending=false;BeginAssPreview();}
        },DispatcherPriority.Render));
    }
    void PrepareDragRenderer(string fonts,int revision)
    {
        if(dragRenderer!=null&&dragRenderer.Fonts==fonts&&dragFontsRevision==revision)return;
        dragRenderer?.Dispose();dragRenderer=null;
        try{dragRenderer=new NativeAssRenderer(fonts);dragFontsRevision=revision;dragRendererError="";}
        catch(Exception e){dragRendererError=e.ToString();previewDiagnostics+="\n实时定位组件不可用，使用兼容预览："+e.Message;}
    }
    void AdvanceAssPreview(double time)
    {
        if(!precisePreview)return;preciseSurface.Frame=Overlay.ContentRect;
        using var frame=previewFrames?.Take(time);if(frame!=null)
        {
            preciseSurface.Present(frame);shownSnapshot=pendingSnapshot;shownDragSimple=pendingDragSimple;shownDragX=pendingDragX;shownDragY=pendingDragY;
            if(assDragging){assDragTranslateReady=true;UpdateAssDragOffset();}
            else{assDragRetain=false;preciseSurface.SetOffset(0,0);}
        }
    }
    void BeginAssSubtitleDrag()
    {
        if(!precisePreview)return;
        assDragRow=selected;assDragDocument=document;assDragTranslateReady=shownSnapshot==PreviewSnapshot();assDragging=assDragRetain=true;
        if(playing)Pause(); // Freeze the editing frame, including karaoke/animation time.
        previewGeneration++;CancelAssPreview();previewFrames?.Clear();previewFrames=null;
        assDragPending=false;previewDebounce.Stop();UpdateAssDragOffset();
    }
    void UpdateAssDragOffset()
    {
        if(!precisePreview)return;
        if(assDragging&&assDragTranslateReady&&shownDragSimple&&selected!=null&&selected==assDragRow)
        {
            var pos=Overlay.Position(selected);var rect=Overlay.ContentRect;
            preciseSurface.SetOffset((pos.X-shownDragX)*rect.Width/document.PlayResX,(pos.Y-shownDragY)*rect.Height/document.PlayResY);
        }
        else if(assDragging)preciseSurface.SetOffset(0,0);
    }
    void MoveAssSubtitlePreview(){UpdateAssDragOffset();if(assDragging)ScheduleAssPreview(true);}
    void EndAssSubtitleDrag(){assDragging=assDragPending=false;assDragRow=null;MarkDirty();LoadEditor();RefreshRows();if(assDragRetain)ScheduleAssPreview(true);}
    void PreviewFontsClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        string snapshot=PreviewSnapshot();var d=new PreviewFontsDialog(snapshot,previewFontDirectory,previewWorkspace?.Diagnostics??previewDiagnostics){Owner=this};
        if(d.ShowDialog()==true){previewFontsRevision++;previewFontDirectory=d.DirectoryBox.Text.Trim();if(!SmokeMode){Directory.CreateDirectory(Path.GetDirectoryName(PreviewPreferencesPath)!);File.WriteAllText(PreviewPreferencesPath,System.Text.Json.JsonSerializer.Serialize(previewFontDirectory));}ScheduleAssPreview(true);}
    });
    void ComparePreviewClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(!VideoAvailable||dummyVideo||mediaPath==null)throw new InvalidOperationException("请先打开真实视频；空白画布不能压制对照。");
        if(!ApplyEditor())return;Pause();var d=new PreviewCompareDialog(document.Serialize(),mediaPath,previewFontDirectory,Math.Max(0,VideoPositionSeconds)){Owner=this};d.ShowDialog();
    });
}
