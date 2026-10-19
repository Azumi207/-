using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    async Task RunMediaTestsAsync(Action<bool,string> check,string dir)
    {
        bool KeyFromWave(Key key){var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent};Wave.RaiseEvent(e);return e.Handled;}
        void Draw(FrameworkElement control){var visual=new DrawingVisual();using var dc=visual.RenderOpen();control.GetType().GetMethod("OnRender",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(control,new object[]{dc});}
        Pause();Seek(1);await Task.Delay(150);double videoTime=Player.Position.TotalSeconds,subtitleTime=Overlay.Time;
        AudioOnly.IsChecked=true;
        var deadline=DateTime.UtcNow.AddSeconds(20);while(!audioReady&&DateTime.UtcNow<deadline)await Task.Delay(100);
        check(audioReady&&audioCachePath!=null&&File.Exists(audioCachePath),"audio-only cache extracted from real AVI and opens");
        check(AudioPlayer.HasAudio&&!AudioPlayer.HasVideo,"audio-only player has audio and no video track");
        check(KeyFromWave(Key.S)&&playing,"S starts independent audio-only playback");await ObservePlaybackFrames(3);
        check(AudioPlayer.Position.TotalSeconds>.65,"audio-only playback clock advances");
        check(Math.Abs(Player.Position.TotalSeconds-videoTime)<.02&&Math.Abs(Overlay.Time-subtitleTime)<.001,"video and subtitle preview stay frozen during audio-only playback");
        double waveTime=Wave.Position,audioTime=AudioPlayer.Position.TotalSeconds;
        if(Math.Abs(waveTime-audioTime)>=.12)CapturePng(Path.Combine(dir,"audio-clock-failure.png"));
        check(Math.Abs(waveTime-audioTime)<.12,$"waveform follows independent audio-only clock (wave={waveTime:F3}, audio={audioTime:F3}, callbacks={PlaybackRenderCallbacks}, active={IsActive}, visible={IsVisible}, state={WindowState})");
        check(KeyFromWave(Key.H)&&!playing&&!timer.IsEnabled,"H stops audio-only player and idle refresh timer");double paused=AudioPlayer.Position.TotalSeconds;await Task.Delay(180);
        check(Math.Abs(AudioPlayer.Position.TotalSeconds-paused)<.02,"audio-only stop holds clock");
        check(KeyFromWave(Key.D)&&Math.Abs(AudioPlayer.Position.TotalSeconds-(selected!.End-.5))<.03,"D seeks audio-only tail without seeking video");KeyFromWave(Key.H);
        check(KeyFromWave(Key.E)&&Math.Abs(stopAt-(selected!.Start+.5))<.01,"E plays audio-only first 500ms");await Task.Delay(700);
        check(!playing,"audio-only selection stops automatically at range end");
        AudioPlayer.Position=TimeSpan.FromSeconds(1.4);AudioOnly.IsChecked=false;await Task.Delay(100);
        check(!playing&&Math.Abs(Player.Position.TotalSeconds-1.4)<.06,"switching back synchronizes video position and remains paused");
        PlayRange(.5,2);await Task.Delay(250);check(Player.Position.TotalSeconds>.6,"video playback resumes normally after audio-only mode");Pause();Seek(.6);

        foreach(var state in new[]{WindowState.Normal,WindowState.Maximized})
        {
            WindowState=state;await Task.Delay(200);UpdateLayout();
            Rect rect=Overlay.ContentRect;Crosshair.SetPointer(new Point(rect.Left+rect.Width/2,rect.Top+rect.Height/2),rect,document.PlayResX,document.PlayResY);
            check(Crosshair.ScriptPoint==new Point(960,540),$"crosshair center maps to script 960,540 in {state}");
            Crosshair.SetPointer(new Point(rect.Left+rect.Width*.25,rect.Top+rect.Height*.75),rect,document.PlayResX,document.PlayResY);
            check(Crosshair.ScriptPoint==new Point(480,810),$"crosshair quarter maps independently of window scale in {state}");
            Crosshair.SetPointer(new Point(rect.Left-1,rect.Top),rect,document.PlayResX,document.PlayResY);
            check(Crosshair.ScriptPoint==null,$"crosshair hides outside actual video rectangle in {state}");
            check(!Crosshair.IsHitTestVisible,$"crosshair does not intercept subtitle dragging in {state}");
            Crosshair.SetPointer(new Point(rect.Left+rect.Width*.495,rect.Top+rect.Height*.6),rect,document.PlayResX,document.PlayResY);
            Draw(Overlay);Draw(Wave);int sb=Overlay.DrawingBuilds,wb=Wave.BackgroundBuilds;
            for(int i=0;i<40;i++){Overlay.Time=.6+i*.001;Wave.Position=.6+i*.001;Draw(Overlay);Draw(Wave);}
            check(Overlay.DrawingBuilds==sb,$"subtitle geometry reused within unchanged interval in {state}");
            check(Wave.BackgroundBuilds==wb,$"waveform geometry reused as playhead moves in {state}");
            int updates=PlaybackUiUpdates;await Task.Delay(200);
            check(!timer.IsEnabled&&PlaybackUiUpdates==updates,$"paused window has no playback redraw loop in {state}");
            EditorTabs.SelectedIndex=2;var source=IssueList.ItemsSource;IssueList.SelectedIndex=0;
            check(ReferenceEquals(source,IssueList.ItemsSource)&&selected==((Issue)IssueList.SelectedItem).Row,$"review selection updates row without rebuilding issue collection in {state}");
            EditorTabs.SelectedIndex=0;await Task.Delay(80);UpdateLayout();CapturePng(Path.Combine(dir,$"crosshair-{state}.png"));
        }
        WindowState=WindowState.Normal;await Task.Delay(100);UpdateLayout();
        Draw(Overlay);int builds=Overlay.DrawingBuilds;Overlay.Time=selected!.End+.1;Draw(Overlay);
        check(Overlay.DrawingBuilds>builds,"subtitle render cache expires at timing boundary");
        builds=Overlay.DrawingBuilds;Overlay.InvalidateDocument();Draw(Overlay);check(Overlay.DrawingBuilds>builds,"subtitle document edit invalidates retained drawing");
        Draw(Wave);int backgrounds=Wave.BackgroundBuilds;Wave.ViewStart+=.1;Draw(Wave);check(Wave.BackgroundBuilds>backgrounds,"waveform cache expires when viewport changes");Wave.ViewStart=0;
        VideoPointerLeft(VideoSurface,new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount));check(Crosshair.ScriptPoint==null,"leaving preview clears crosshair");
        string cached=audioCachePath!;
        AudioOnly.IsChecked=true;OpenMedia(Path.Combine(dir,"sample.avi"));
        deadline=DateTime.UtcNow.AddSeconds(20);while((!audioReady||!mediaReady)&&DateTime.UtcNow<deadline)await Task.Delay(100);
        check(audioReady&&mediaReady&&AudioMode,"reopening the same media reloads video and preserves chosen audio-only mode");
        check(!File.Exists(cached),"replacing media removes the previous owned PCM cache");
        double held=Player.Position.TotalSeconds;KeyFromWave(Key.S);await Task.Delay(200);KeyFromWave(Key.H);
        check(Math.Abs(Player.Position.TotalSeconds-held)<.02&&AudioPlayer.Position.TotalSeconds>selected!.Start+.08,"audio-only remains independent after opening replacement media");
        cached=audioCachePath!;AudioOnly.IsChecked=false;CloseAudio();check(!File.Exists(cached),"owned audio cache is removed on close");
        Seek(selected.Start);
    }
}
