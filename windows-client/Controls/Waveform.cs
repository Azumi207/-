using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Frameflow.Controls;
public sealed class Waveform:FrameworkElement
{
    public ImageSource? Spectrum {get;set;}public bool SpectrumMode {get;set;}public double Gain {get;set;}=1;
    public float[] Peaks {get;set;}=[];public double ViewStart {get;set;}=0;public double Span {get;set;}=12;public double Start {get;set;}=0;public double End {get;set;}=2;
    double position;readonly DrawingVisual playhead=new();readonly TranslateTransform playheadTransform=new();
    public double Position {get=>position;set{position=value;MovePlayhead();}}
    public int StaticRenders {get;private set;}
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?playhead:throw new ArgumentOutOfRangeException(nameof(index));
    public double Duration {get;set;}=60;public IReadOnlyList<(double Start,double End)> Gaps {get;set;}=[];
    public bool Dragging {get;private set;}
    public event Func<bool>? BeginEdit;public event Action? EndEdit;public event Action<double,double>? SelectionEdited;public event Action<double>? SeekRequested;public event Action? ZoomChanged;
    public bool EditingStart=>dragStart;
    private bool dragStart;
    public Waveform(){Focusable=true;MinHeight=100;ClipToBounds=true;Cursor=Cursors.Cross;playhead.Transform=playheadTransform;AddVisualChild(playhead);}
    void MovePlayhead(){double x=X(position);playhead.Opacity=x>=0&&x<=ActualWidth?1:0;playheadTransform.X=x;}
    double X(double time)=>(time-ViewStart)/Span*ActualWidth;
    double TimeAt(double x)=>Math.Clamp(ViewStart+x/Math.Max(1,ActualWidth)*Span,0,Math.Max(Duration,End+2));
    // Clone brushes before freezing cached geometry; never freeze the app's live theme palette.
    static Brush ColorBrush(string key)=>Frameflow.Core.Appearance.Brush(key).CloneCurrentValue();
    public void RefreshTheme(){background=null;InvalidateVisual();}
    DrawingGroup? background;float[]? cachedPeaks;double cachedView,cachedSpan,cachedDpi,cachedGain;Size cachedSize;ImageSource? cachedSpectrum;bool cachedSpectrumMode;
    public int BackgroundBuilds {get;private set;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);StaticRenders++;double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;dc.DrawRoundedRectangle(ColorBrush("FieldBackground"),null,new Rect(0,0,w,h),12,12);double center=24+(h-34)/2;
        var selLeft=Math.Clamp(X(Start),0,w);var selRight=Math.Clamp(X(End),0,w);if(selRight>selLeft)dc.DrawRectangle(ColorBrush("SelectionBrush"),null,new Rect(selLeft,24,selRight-selLeft,h-24));
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(background==null||cachedPeaks!=Peaks||cachedView!=ViewStart||cachedSpan!=Span||cachedSize!=RenderSize||cachedDpi!=dpi||cachedSpectrum!=Spectrum||cachedSpectrumMode!=SpectrumMode||cachedGain!=Gain)
        {
            var drawing=new DrawingGroup();using(var context=drawing.Open())DrawBackground(context,w,h,center);drawing.Freeze();background=drawing;
            cachedPeaks=Peaks;cachedView=ViewStart;cachedSpan=Span;cachedSize=RenderSize;cachedDpi=dpi;cachedSpectrum=Spectrum;cachedSpectrumMode=SpectrumMode;cachedGain=Gain;BackgroundBuilds++;
        }
        dc.DrawDrawing(background);
        foreach(var gap in Gaps){double a=Math.Clamp(X(gap.Start),0,w),b=Math.Clamp(X(gap.End),0,w);if(b>a)dc.DrawRectangle(ColorBrush("WaveGapBrush"),null,new Rect(a,24,Math.Max(3,b-a),h-24));}
        Boundary(dc,Start,ColorBrush("WaveStartBrush"),"起");Boundary(dc,End,ColorBrush("WaveEndBrush"),"止");
        using(var line=playhead.RenderOpen())line.DrawLine(new Pen(ColorBrush("ForegroundBrush"),1),new Point(0,24),new Point(0,h));MovePlayhead();
    }
    void DrawBackground(DrawingContext dc,double w,double h,double center)
    {
        if(SpectrumMode){if(Spectrum!=null)dc.DrawImage(Spectrum,new Rect(0,24,w,h-24));else DrawText(dc,"正在准备当前视窗频谱…",new Point(12,center),ColorBrush("MutedBrush"));}
        double interval=Span<=5?1:Span<=15?2:Span<=40?5:10;
        double lastLabelRight=-10;
        for(double t=Math.Ceiling(ViewStart/interval)*interval;t<=ViewStart+Span;t+=interval){double x=X(t);dc.DrawLine(new Pen(ColorBrush("BorderBrush"),1),new Point(x,24),new Point(x,h));double labelX=Math.Clamp(x+2,0,Math.Max(0,w-56));if(labelX>=lastLabelRight+8){DrawText(dc,$"{(int)t/60:00}:{t%60:00.0}",new Point(labelX,2),ColorBrush("MutedBrush"));lastLabelRight=labelX+52;}}
        if(SpectrumMode)return;
        if(Peaks.Length==0)DrawText(dc,"打开视频或音频后生成真实波形",new Point(12,center-10),ColorBrush("MutedBrush"));
        else
        {
            var geom=new StreamGeometry();using(var ctx=geom.Open()){for(int px=0;px<w;px+=2){int a=Math.Max(0,(int)((ViewStart+px/w*Span)*100)),b=Math.Min(Peaks.Length,(int)((ViewStart+(px+2)/w*Span)*100)+1);float peak=0;for(int i=a;i<b;i++)peak=Math.Max(peak,Peaks[i]);double amp=Math.Min(1,peak*Gain)*(h-40)*.48;ctx.BeginFigure(new Point(px,center-amp),false,false);ctx.LineTo(new Point(px,center+amp),true,false);}}geom.Freeze();dc.DrawGeometry(null,new Pen(ColorBrush("WaveBrush"),1),geom);
        }
    }
    void Boundary(DrawingContext dc,double time,Brush brush,string text){double x=X(time);if(x<0||x>ActualWidth)return;dc.DrawLine(new Pen(brush,2),new Point(x,24),new Point(x,ActualHeight));DrawText(dc,text,new Point(Math.Min(x+3,ActualWidth-20),26),brush);}
    void DrawText(DrawingContext dc,string text,Point point,Brush brush)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),11,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),point);
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();var p=e.GetPosition(this);if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)||e.ChangedButton==MouseButton.Middle){SeekRequested?.Invoke(TimeAt(p.X));e.Handled=true;return;}
        if(e.ChangedButton!=MouseButton.Left&&e.ChangedButton!=MouseButton.Right)return;
        if(BeginEdit?.Invoke()==false){e.Handled=true;return;}Dragging=true;dragStart=e.ChangedButton==MouseButton.Left;if(Math.Abs(p.X-X(End))<8)dragStart=false;else if(Math.Abs(p.X-X(Start))<8)dragStart=true;CaptureMouse();UpdateDrag(p.X);e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e){if(Dragging)UpdateDrag(e.GetPosition(this).X);}
    void UpdateDrag(double x){double time=TimeAt(x);if(dragStart)Start=Math.Min(time,End-.01);else End=Math.Max(time,Start+.01);Start=Math.Max(0,Start);SelectionEdited?.Invoke(Start,End);InvalidateVisual();}
    protected override void OnMouseUp(MouseButtonEventArgs e){if(!Dragging)return;Dragging=false;ReleaseMouseCapture();EndEdit?.Invoke();e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){if(Dragging){Dragging=false;EndEdit?.Invoke();}base.OnLostMouseCapture(e);}
    protected override void OnMouseWheel(MouseWheelEventArgs e){var anchor=TimeAt(e.GetPosition(this).X);Span=Math.Clamp(Span*(e.Delta>0?.8:1.25),1,120);ViewStart=Math.Max(0,anchor-Span*.5);ZoomChanged?.Invoke();InvalidateVisual();e.Handled=true;}
}
