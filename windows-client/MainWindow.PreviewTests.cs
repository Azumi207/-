using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Frameflow.Controls;
using Frameflow.Core;
namespace Frameflow;
public partial class MainWindow
{
    public async Task RunPreviewTestsAsync(string dir)
    {
        var log=new List<string>();void Check(bool result,string text){if(!result)throw new Exception("FAIL PREVIEW: "+text);log.Add("PASS: "+text);File.WriteAllLines(Path.Combine(dir,"preview-progress.txt"),log);}
        async Task Until(Func<bool> done,string why,int seconds=20){var end=DateTime.UtcNow.AddSeconds(seconds);while(!done()&&DateTime.UtcNow<end)await Task.Delay(30);Check(done(),why);}
        await PreviewTests.RunAsync(dir,Check);OpenSubtitle(Path.Combine(dir,"complex.ass"));Player.IsMuted=true;OpenMedia(Path.Combine(dir,"source.mp4"));await Until(()=>mediaReady&&Wave.Peaks.Length>0,"real video, audio and waveform ready");
        SetPrecisePreview(true);Pause();Seek(6.3);await Until(()=>preciseSurface.FramesPresented>0&&Math.Abs(preciseSurface.PresentedTime-6.3)<.08,"main editor displays real libass frame at sought time");
        string before=document.Serialize();int undoCount=undo.Count;TextEditor.Text+=@"{\t(0,1000,\fscx130)}";string draft=TextEditor.Text;int presented=preciseSurface.FramesPresented;
        await Until(()=>preciseSurface.FramesPresented>presented,"typing updates debounced precise preview");Check(document.Serialize()==before&&undo.Count==undoCount&&draftDirty&&TextEditor.Text==draft,"live draft rendering does not commit text, change undo or save subtitles");
        TextEditor.Text=selected!.Text.Replace(@"\N","\n");draftDirty=false;
        PlayRange(6,8);await Until(()=>preciseSurface.FramesPresented>presented+8,"real-time libass animation advances alongside video");
        Check(Math.Abs(preciseSurface.PresentedTime-VideoPositionSeconds)<.3,"preview follows video clock rather than independent wall clock");
        int staticRenders=Wave.StaticRenders;int builds=Wave.BackgroundBuilds;await Task.Delay(200);Check(Wave.StaticRenders==staticRenders&&Wave.BackgroundBuilds==builds,"precise preview does not rebuild waveform on each frame");
        Check(preciseSurface.IsMeasureValid,"bitmap updates do not invalidate WPF layout per frame");Pause();Seek(10.8);await Until(()=>Math.Abs(preciseSurface.PresentedTime-10.8)<.08,"seeking replaces canceled stream with current frame");
        previewFontDirectory=Path.Combine(dir,"fonts [字体]");ScheduleAssPreview(true);await Until(()=>!previewTask.IsCompleted||previewDebounce.IsEnabled,"font reload scheduled");await Task.Delay(600);
        using(var fontFile=File.OpenRead(Path.Combine(previewFontDirectory,"custom.ttf")))Check(fontFile.Length>0,"font source remains accessible after rendering");
        Check(PreviewFontsButton.Content.ToString()!.Contains("!"),"editor proactively flags fonts needing verification");
        var fonts=new PreviewFontsDialog(document.Serialize(),previewFontDirectory,previewWorkspace?.Diagnostics??previewDiagnostics){Owner=this};fonts.Show();await Task.Delay(100);CaptureElement(fonts,Path.Combine(dir,"font-dialog.png"));Check(MenuVisuals<TextBlock>(fonts).Any(t=>t.Text.Contains("Frameflow Missing Font 7123")),"font dialog shows missing requested font and replacement guidance");fonts.Close();
        var compare=new PreviewCompareDialog(document.Serialize(),mediaPath!,previewFontDirectory,6.3){Owner=this};compare.Show();await compare.GenerateAsync();
        File.WriteAllText(Path.Combine(dir,"compare-result.txt"),compare.LastError??"SUCCESS");
        Check(compare.LastError==null&&compare.Result!=null,"comparison executes actual 10-second encoder successfully: "+compare.LastError);Check(compare.ReferenceImage.Source!=null&&compare.EncodedImage.Source!=null,"comparison decodes both reference and actual compressed frame");
        Check(compare.Result!.FontLog.Contains("fontselect")&&compare.Result.LogPath.Length>0,"comparison exposes real selected fonts and encoder log");
        var a=new System.Windows.Media.Imaging.FormatConvertedBitmap((System.Windows.Media.Imaging.BitmapSource)compare.ReferenceImage.Source!,PixelFormats.Bgra32,null,0);var b=new System.Windows.Media.Imaging.FormatConvertedBitmap((System.Windows.Media.Imaging.BitmapSource)compare.EncodedImage.Source!,PixelFormats.Bgra32,null,0);
        Check(a.PixelWidth==b.PixelWidth&&a.PixelHeight==b.PixelHeight,"comparison keeps source pixel dimensions on both sides");var ap=new byte[a.PixelWidth*a.PixelHeight*4];var bp=new byte[ap.Length];a.CopyPixels(ap,a.PixelWidth*4,0);b.CopyPixels(bp,b.PixelWidth*4,0);double error=0;int colored=0;for(int i=0;i<ap.Length;i+=4)if(Math.Max(ap[i],ap[i+2])>120||Math.Max(bp[i],bp[i+2])>120){for(int c=0;c<3;c++)error+=Math.Abs(ap[i+c]-bp[i+c]);colored++;}double mean=error/Math.Max(1,colored*3);File.WriteAllText(Path.Combine(dir,"comparison-pixel-error.txt"),$"foreground_pixels={colored}; RGB_MAE={mean:0.###}");Check(colored>500&&mean<20,"actual compressed subtitle pixels match pre-encode reference within measured compression tolerance");
        File.Copy(compare.ReferencePath!,Path.Combine(dir,"compare-reference.png"),true);File.Copy(compare.EncodedPath!,Path.Combine(dir,"compare-encoded.png"),true);
        File.Copy(compare.Result.LogPath,Path.Combine(dir,"compare-encode.log.txt"),true);CaptureElement(compare,Path.Combine(dir,"comparison-dialog.png"));compare.Close();
        Check(document.Serialize()==before,"comparison leaves the editor model unchanged");
        Width=1440;Height=940;EditorTabs.SelectedIndex=0;Seek(6.3);await Until(()=>Math.Abs(preciseSurface.PresentedTime-6.3)<.08,"preview restored after comparison");
        foreach(string theme in new[]{"light","dark"}){Appearance.Apply(theme);await Task.Delay(150);UpdateLayout();CaptureElement((FrameworkElement)Content,Path.Combine(dir,"precise-"+theme+".png"));Check(precisePreview,"theme switch preserves libass mode");}
        SetWorkspaceFullscreen(true);PlayRange(6,8);await Task.Delay(600);Check(preciseSurface.FramesPresented>0&&Math.Abs(preciseSurface.PresentedTime-VideoPositionSeconds)<.35,"full-screen precise preview stays aligned with actual media");SetWorkspaceFullscreen(false);Pause();
        AudioOnly.IsChecked=true;await Until(()=>audioReady,"audio-only cache ready");await Task.Delay(300);double frozen=VideoPositionSeconds;PlayRange(1,3);await Task.Delay(600);Check(Math.Abs(VideoPositionSeconds-frozen)<.06,"audio-only playback keeps video frozen with precise preview enabled");Pause();AudioOnly.IsChecked=false;
        CreateDummyVideo(3840,2160,30,20,Colors.Black,false);AudioOnly.IsEnabled=true;AudioOnly.IsChecked=false;PlayRange(6,10);await Until(()=>!double.IsNaN(preciseSurface.PresentedTime),"4K-source ASS overlay starts without blocking editor");int frames=preciseSurface.FramesPresented;var watch=System.Diagnostics.Stopwatch.StartNew();await Task.Delay(1200);double elapsed=watch.Elapsed.TotalSeconds;int rendered=preciseSurface.FramesPresented-frames;File.WriteAllText(Path.Combine(dir,"4k-preview-performance.txt"),$"source=3840x2160; display<=1280x720; elapsed={elapsed:0.000}; frames={rendered}; presented_fps={rendered/elapsed:0.00}; lag={VideoPositionSeconds-preciseSurface.PresentedTime:0.000}");Check(rendered>=15&&VideoPositionSeconds-preciseSurface.PresentedTime<.4,"4K-source synthetic animation remains responsive with bounded scaled preview");Pause();
        SetPrecisePreview(false);await previewTask;Check(Overlay.ShowText&&preciseSurface.Visibility==Visibility.Collapsed,"lightweight mode restores original placement renderer and cancels precise renderer");
        Check(document.Serialize()==before,"preview UI suite preserves subtitle content");dirty=draftDirty=false;
        File.WriteAllLines(Path.Combine(dir,"preview-tests.txt"),log.Append("TOTAL: "+log.Count+" passed"));Console.WriteLine(string.Join('\n',log)+"\nTOTAL: "+log.Count+" passed");
    }
}
