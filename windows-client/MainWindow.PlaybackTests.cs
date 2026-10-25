using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Frameflow;
public partial class MainWindow
{
    async Task ObservePlaybackFrames(int count)
    {
        // Observe real compositor callbacks instead of assuming this desktop/remote session runs at a fixed FPS.
        int start=PlaybackRenderCallbacks;var deadline=DateTime.UtcNow.AddSeconds(1);
        while(PlaybackRenderCallbacks<start+count&&DateTime.UtcNow<deadline)await Task.Delay(16);
        if(PlaybackRenderCallbacks<start+count)throw new Exception($"No timely compositor progress: {PlaybackRenderCallbacks-start}/{count} real callbacks in one second.");
    }
    async Task RunPlaybackTestsAsync(Action<bool,string> check,string dir)
    {
        Pause();AudioOnly.IsChecked=false;Wave.ViewStart=0;Wave.InvalidateVisual();Seek(.6);await Task.Delay(150);UpdateLayout();
        int renders=Wave.StaticRenders,backgrounds=Wave.BackgroundBuilds;
        for(int i=0;i<100;i++)Wave.Position=.6+i*.001;
        await Task.Delay(100);
        // Layers may precede the retained playhead; identify its translation instead of its index.
        var head=Enumerable.Range(0,VisualTreeHelper.GetChildrenCount(Wave)).Select(i=>VisualTreeHelper.GetChild(Wave,i)).OfType<DrawingVisual>().Single(v=>v.Transform is TranslateTransform);
        check(Wave.StaticRenders==renders&&Wave.BackgroundBuilds==backgrounds,"100 playhead moves do not redraw waveform surface or rebuild geometry");
        check(head is DrawingVisual dv&&Math.Abs(((TranslateTransform)dv.Transform).X-(Wave.Position-Wave.ViewStart)/Wave.Span*Wave.ActualWidth)<.01,"retained playhead visual moves to the correct waveform coordinate");
        Wave.Position=Wave.ViewStart+Wave.Span+1;check(((DrawingVisual)head).Opacity==0,"playhead hides outside viewport");Wave.Position=.6;

        // Value and text changes must not dirty layout up through the video pane.
        double beforeProgress=Player.Position.TotalSeconds;
        timerSeeking=true;SeekBar.Value=.9;timerSeeking=false;PositionLabel.Text="0:00:00.90";
        check(SeekBar.IsMeasureValid&&SeekBar.IsArrangeValid&&PositionLabel.IsMeasureValid&&PositionLabel.IsArrangeValid&&VideoSurface.IsMeasureValid,"playback chrome updates preserve valid measure and arrange");
        check(Math.Abs(Player.Position.TotalSeconds-beforeProgress)<.03,"programmatic progress update never seeks media recursively");
        SeekBar.Value=1.2;check(Math.Abs(Player.Position.TotalSeconds-1.2)<.03&&Math.Abs(Wave.Position-1.2)<.001,$"user seek value synchronizes media and waveform (video={Player.Position.TotalSeconds:F3}, wave={Wave.Position:F3}, value={SeekBar.Value:F3}, audio={AudioMode}, mediaReady={mediaReady}, timerSeeking={timerSeeking}, ready={ready})");
        var peer=UIElementAutomationPeer.CreatePeerForElement(SeekBar)!;var range=(IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!;
        check(peer.GetAutomationControlType()==AutomationControlType.Slider&&!range.IsReadOnly,"retained seek control exposes slider automation");
        range.SetValue(1.4);check(Math.Abs(Player.Position.TotalSeconds-1.4)<.03,"accessible range seek changes media position");
        void SeekKey(Key key){var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,key){RoutedEvent=Keyboard.KeyDownEvent};SeekBar.GetType().GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(SeekBar,new object[]{e});check(e.Handled,"seek keyboard handles "+key);}
        SeekKey(Key.Right);check(Math.Abs(SeekBar.Value-1.5)<.001,"seek right arrow moves 100ms");
        SeekKey(Key.Home);check(SeekBar.Value==SeekBar.Minimum,"seek home reaches beginning");
        SeekKey(Key.End);check(SeekBar.Value==SeekBar.Maximum,"seek end reaches duration");
        Seek(.6);
        // Simulate losing a drag capture without mouse-up (e.g. Alt-Tab).
        SeekBar.GetType().GetField("dragging",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(SeekBar,true);scrubbing=true;
        SeekBar.GetType().GetMethod("OnLostMouseCapture",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(SeekBar,new object[]{new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.LostMouseCaptureEvent}});
        check(!scrubbing,"lost seek capture cannot leave playback display stuck in scrubbing mode");

        foreach(var state in new[]{WindowState.Normal,WindowState.Maximized})
        {
            WindowState=state;await Task.Delay(200);UpdateLayout();PlayRange(.6,2);await Task.Delay(120);
            int callbacks=PlaybackRenderCallbacks;renders=Wave.StaticRenders;backgrounds=Wave.BackgroundBuilds;await ObservePlaybackFrames(3);
            if(!(playing&&renderingPlayback&&PlaybackRenderCallbacks>callbacks+2))CapturePng(Path.Combine(dir,$"compositor-failure-{state}.png"));
            check(playing&&renderingPlayback&&PlaybackRenderCallbacks>callbacks+2,$"video playback uses compositor callbacks in {state} (before={callbacks}, after={PlaybackRenderCallbacks}, playing={playing}, subscribed={renderingPlayback}, active={IsActive}, position={Player.Position.TotalSeconds:F3})");
            check(Math.Abs(Wave.Position-Player.Position.TotalSeconds)<.1,$"retained waveform follows video clock in {state}");
            check(Wave.StaticRenders==renders&&Wave.BackgroundBuilds==backgrounds,$"steady playback does not redraw full waveform in {state}");
            CapturePng(Path.Combine(dir,$"playback-{state}.png"));Pause();callbacks=PlaybackRenderCallbacks;await Task.Delay(120);
            check(!renderingPlayback&&!timer.IsEnabled&&callbacks==PlaybackRenderCallbacks,$"pause removes rendering subscription and timer in {state}");
            PlayRange(.6,.9);await Task.Delay(700);check(!playing&&!renderingPlayback,$"compositor playback still stops at selection end in {state}");
        }
        WindowState=WindowState.Normal;await Task.Delay(120);Seek(selected!.Start);
    }
}
