using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Frameflow.Controls;

// Playback text must not trigger a layout pass through the video pane each tick.
public sealed class PlaybackTimeLabel:FrameworkElement
{
    string text="0:00:00.00";readonly DrawingVisual label=new();
    public string Text{get=>text;set{if(text==value)return;text=value;Paint();}}
    public PlaybackTimeLabel(){Width=95;Height=24;IsHitTestVisible=false;AddVisualChild(label);}
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?label:throw new ArgumentOutOfRangeException(nameof(index));
    void Paint(){using var dc=label.RenderOpen();dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),12,Frameflow.Core.Appearance.Brush("ForegroundBrush"),VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(0,4));}
    protected override void OnRender(DrawingContext dc)=>Paint();
}

// A retained seek thumb: Value changes move a visual, not WPF Slider's Track layout.
public sealed class PlaybackSeekBar:FrameworkElement
{
    readonly DrawingVisual thumb=new();readonly TranslateTransform translation=new();
    double value,minimum,maximum=60;bool dragging;
    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;
    public event EventHandler? ScrubCompleted;
    public double Minimum{get=>minimum;set{minimum=value;MoveThumb();}}
    public double Maximum{get=>maximum;set{maximum=Math.Max(minimum,value);Value=this.value;MoveThumb();}}
    public double Value{get=>value;set{double next=Math.Clamp(value,Minimum,Maximum);if(Math.Abs(next-this.value)<.000001)return;double old=this.value;this.value=next;MoveThumb();ValueChanged?.Invoke(this,new RoutedPropertyChangedEventArgs<double>(old,next));}}
    public PlaybackSeekBar(){Height=20;MinWidth=50;Focusable=true;Cursor=Cursors.Hand;thumb.Transform=translation;AddVisualChild(thumb);}
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?thumb:throw new ArgumentOutOfRangeException(nameof(index));
    void MoveThumb()=>translation.X=6+(maximum<=minimum?0:(value-minimum)/(maximum-minimum)*Math.Max(0,ActualWidth-12));
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        dc.DrawLine(new Pen(Frameflow.Core.Appearance.Brush("BorderBrush"),3),new Point(6,10),new Point(Math.Max(6,ActualWidth-6),10));
        using(var drawing=thumb.RenderOpen())drawing.DrawRoundedRectangle(Frameflow.Core.Appearance.Brush("Accent"),null,new Rect(-4,3,8,14),4,4);
        if(IsKeyboardFocused)dc.DrawRectangle(null,new Pen(Frameflow.Core.Appearance.Brush("Accent"),1),new Rect(0,0,ActualWidth,ActualHeight));MoveThumb();
    }
    void FromPointer(MouseEventArgs e)=>Value=Minimum+Math.Clamp((e.GetPosition(this).X-6)/Math.Max(1,ActualWidth-12),0,1)*(Maximum-Minimum);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){Focus();dragging=true;CaptureMouse();FromPointer(e);e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){if(dragging)FromPointer(e);}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(dragging){FromPointer(e);dragging=false;ReleaseMouseCapture();ScrubCompleted?.Invoke(this,EventArgs.Empty);e.Handled=true;}}
    protected override void OnLostMouseCapture(MouseEventArgs e){if(dragging){dragging=false;ScrubCompleted?.Invoke(this,EventArgs.Empty);}base.OnLostMouseCapture(e);}
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e){base.OnGotKeyboardFocus(e);InvalidateVisual();}
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e){base.OnLostKeyboardFocus(e);InvalidateVisual();}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch(e.Key){case Key.Left:Value-=.1;break;case Key.Right:Value+=.1;break;case Key.Home:Value=Minimum;break;case Key.End:Value=Maximum;break;default:base.OnKeyDown(e);return;}e.Handled=true;
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new SeekPeer(this);
    sealed class SeekPeer(PlaybackSeekBar owner):FrameworkElementAutomationPeer(owner),IRangeValueProvider
    {
        protected override string GetClassNameCore()=>nameof(PlaybackSeekBar);
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface pattern)=>pattern==PatternInterface.RangeValue?this:base.GetPattern(pattern);
        public bool IsReadOnly=>!owner.IsEnabled;public double LargeChange=>1;public double SmallChange=>.1;
        public double Maximum=>owner.Maximum;public double Minimum=>owner.Minimum;public double Value=>owner.Value;
        public void SetValue(double value){if(!owner.IsEnabled)throw new System.Windows.Automation.ElementNotEnabledException();owner.Value=value;}
    }
}
