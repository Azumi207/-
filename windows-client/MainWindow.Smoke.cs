using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    public bool SmokeMode {get;set;}
    public async Task RunSmokeAsync(string dir)
    {
        var log=new List<string>();void Assert(bool value,string label){if(!value)throw new Exception("FAIL UI: "+label);log.Add("PASS: "+label);File.WriteAllText(Path.Combine(dir,"ui-progress.txt"),string.Join(Environment.NewLine,log));}
        OpenSubtitle(Path.Combine(dir,"sample.ass"));Assert(document.Rows.Count()==3,"real ASS opens in desktop table");
        SelectRow(document.Rows.First());TextEditor.Text="桌面初翻测试";Assert(ApplyEditor(),"editor commits");Assert(selected!.Text=="桌面初翻测试","editor updates model");
        UndoClick(this,new RoutedEventArgs());Assert(selected!.Text=="第一句，含中文和逗号","undo restores original text");
        EditorTabs.SelectedIndex=2;RefreshIssues();IssueList.SelectedItem=((IEnumerable<Issue>)IssueList.ItemsSource).First(i=>i.Kind=="疑似闪轴");FixGapClick(this,new RoutedEventArgs());Assert(!Quality.Scan(document).Any(i=>i.Kind=="疑似闪轴"),"review repair removes real gap");UndoClick(this,new RoutedEventArgs());Assert(Quality.Scan(document).Any(i=>i.Kind=="疑似闪轴"),"undo restores real gap");
        ActorBox.Text="独立说话人";ApplyEditor();Assert(selected!.Actor=="独立说话人"&&selected.Style=="Default","speaker independent of style");UndoClick(this,new RoutedEventArgs());
        var posBefore=selected!.Text;PushUndo();AssTags.Set(selected,"pos","(960,900)");LoadEditor();Seek(selected.Start);Assert(selected.Text.Contains(@"\pos(960,900)"),"position maps to ASS override");UndoClick(this,new RoutedEventArgs());Assert(selected!.Text==posBefore,"undo restores position tags");
        var defaultPosition=Overlay.Position(selected!);Assert(Math.Abs(defaultPosition.X-960)<.01&&Math.Abs(defaultPosition.Y-1032)<.01,"default bottom-center is X=960 Y=1032, not left edge");
        AnchorClick(new System.Windows.Controls.Button{Tag="8"},new RoutedEventArgs());Assert(Math.Abs(Overlay.Position(selected!).X-960)<.01&&Math.Abs(Overlay.Position(selected!).Y-60)<.01,"top-center preset is X=960 Y=60");UpdateLayout();Assert(SubtitleGrid.Columns[8].GetCellContent(selected) is System.Windows.Controls.TextBlock cell&&cell.Text.Contains(@"\pos(960,60)"),"actual position edits still refresh table after selection optimization");UndoClick(this,new RoutedEventArgs());
        EndBox.Text="0:00:02.13";ApplyEditor();Assert(Math.Abs(Wave.End-2.13)<.001,"time editor updates waveform boundary");UndoClick(this,new RoutedEventArgs());
        Player.IsMuted=true;OpenMedia(Path.Combine(dir,"sample.wav"));var deadline=DateTime.UtcNow.AddSeconds(20);while((!mediaReady||Wave.Peaks.Length==0)&&DateTime.UtcNow<deadline)await Task.Delay(100);
        Assert(mediaReady,"Windows media playback opened real WAV");Assert(Wave.Peaks.Length>0,"real waveform reaches desktop control");PlayRange(.5,2.12);await Task.Delay(500);Assert(Player.Position.TotalSeconds>.6,"playback clock advances");Pause();Seek(.5);Assert(Math.Abs(Overlay.Time-.5)<.01&&Math.Abs(Wave.Position-.5)<.01,"seek links preview and waveform");
        OpenMedia(Path.Combine(dir,"sample.avi"));deadline=DateTime.UtcNow.AddSeconds(20);while((!mediaReady||Wave.Peaks.Length==0)&&DateTime.UtcNow<deadline)await Task.Delay(100);Assert(mediaReady&&Player.NaturalVideoWidth==160&&Player.NaturalVideoHeight==90,"real AVI video opens with video dimensions");Assert(Wave.Peaks.Length>=590,"video audio track produces waveform");PlayRange(.5,2.12);await Task.Delay(500);Assert(Player.Position.TotalSeconds>.6,"video playback clock advances");Pause();Seek(1);await Task.Delay(200);
        await RunShortcutTestsAsync(Assert);
        await RunMediaTestsAsync(Assert,dir);
        await RunPlaybackTestsAsync(Assert,dir);
        await RunMenuTestsAsync(Assert,dir);
        await RunSubtitleTestsAsync(Assert,dir);
        await RunTimingTestsAsync(Assert,dir);
        await RunMultimediaTestsAsync(Assert,dir);
        await RunEncodingTestsAsync(Assert,dir);
        await RunTeamTestsAsync(Assert,dir);
        EditorTabs.SelectedIndex=0;await Task.Delay(150);UpdateLayout();CapturePng(Path.Combine(dir,"windows-create.png"));
        EditorTabs.SelectedIndex=2;await Task.Delay(150);UpdateLayout();CapturePng(Path.Combine(dir,"windows-review.png"));
        EditorTabs.SelectedIndex=1;await Task.Delay(150);UpdateLayout();CapturePng(Path.Combine(dir,"windows-style.png"));
        var output=Path.Combine(dir,"ui-edited.ass");SubtitleDocument.AtomicSave(output,document.Bytes(),false);Assert(SubtitleDocument.Load(output).Rows.Count()==3,"desktop document writes readable ASS");
        File.WriteAllText(Path.Combine(dir,"ui-tests.txt"),string.Join(Environment.NewLine,log));Console.WriteLine(string.Join(Environment.NewLine,log));dirty=draftDirty=false;
    }
    void CapturePng(string path){var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(path);encoder.Save(stream);}
}
