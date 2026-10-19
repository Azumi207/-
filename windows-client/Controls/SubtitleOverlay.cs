using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Frameflow.Core;

namespace Frameflow.Controls;
public sealed class SubtitleOverlay:FrameworkElement
{
    public SubtitleDocument? Document {get;set;}public AssRow? Selected {get;set;}public double Time {get;set;}public double VideoWidth {get;set;}=1920;public double VideoHeight {get;set;}=1080;
    public bool Editing {get;set;}=true;public event Func<bool>? BeginMove;public event Action<double,double>? Moved;public event Action? EndMove;
    bool dragging;Point origin;double initialX,initialY;Rect hit;
    DrawingGroup? cachedDrawing;SubtitleDocument? cachedDocument;AssRow? cachedSelected;
    Size cachedSize;double cachedVideoWidth,cachedVideoHeight,cachedDpi,validFrom,validUntil;bool cachedEditing;
    public int DrawingBuilds {get;private set;}
    public void InvalidateDocument(){cachedDrawing=null;InvalidateVisual();}
    public void UpdateTime(double time){Time=time;if(cachedDrawing==null||time<validFrom||time>=validUntil)InvalidateVisual();}
    public Rect ContentRect {get{double ratio=VideoWidth/Math.Max(1,VideoHeight),w=ActualWidth,h=ActualHeight;if(w/h>ratio)w=h*ratio;else h=w/ratio;return new Rect((ActualWidth-w)/2,(ActualHeight-h)/2,Math.Max(1,w),Math.Max(1,h));}}
    public (double X,double Y,int Alignment) Position(AssRow row)
    {
        var d=Document!;var s=d.FindStyle(row);int align=(int)AssTags.Number(row,"an",s?.Number("Alignment",2)??2);align=Math.Clamp(align,1,9);
        var pos=AssTags.Get(row,"pos","").Split(',');if(pos.Length==2&&double.TryParse(pos[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var px)&&double.TryParse(pos[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var py))return(px,py,align);
        double ml=double.TryParse(row.Get("MarginL"),out var l)&&l!=0?l:s?.Number("MarginL",30)??30,mr=double.TryParse(row.Get("MarginR"),out var r)&&r!=0?r:s?.Number("MarginR",30)??30,mv=double.TryParse(row.Get("MarginV"),out var v)&&v!=0?v:s?.Number("MarginV",48)??48;
        return(((align-1)%3) switch{0=>ml,1=>d.PlayResX/2.0,_=>d.PlayResX-mr},align<=3?d.PlayResY-mv:align<=6?d.PlayResY/2.0:mv,align);
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        if(Document==null)return;
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(cachedDrawing==null||cachedDocument!=Document||cachedSelected!=Selected||cachedSize!=RenderSize||cachedVideoWidth!=VideoWidth||cachedVideoHeight!=VideoHeight||cachedDpi!=dpi||cachedEditing!=Editing||Time<validFrom||Time>=validUntil)
        {
            cachedDocument=Document;cachedSelected=Selected;cachedSize=RenderSize;cachedVideoWidth=VideoWidth;cachedVideoHeight=VideoHeight;cachedDpi=dpi;cachedEditing=Editing;
            var drawing=new DrawingGroup();using(var context=drawing.Open())DrawSubtitles(context);drawing.Freeze();cachedDrawing=drawing;DrawingBuilds++;
        }
        dc.DrawDrawing(cachedDrawing);
    }
    void DrawSubtitles(DrawingContext dc)
    {
        if(Document==null)return;var rect=ContentRect;double sx=rect.Width/Document.PlayResX,sy=rect.Height/Document.PlayResY;hit=Rect.Empty;
        validFrom=double.NegativeInfinity;validUntil=double.PositiveInfinity;var visible=new List<AssRow>();
        foreach(var row in Document.Rows)
        {
            if(row.Comment)continue;double start=row.Start,end=row.End;
            foreach(var boundary in new[]{start,end}){if(boundary<=Time)validFrom=Math.Max(validFrom,boundary);else validUntil=Math.Min(validUntil,boundary);}
            if(Time>=start&&Time<end)visible.Add(row);
        }
        foreach(var row in visible.OrderBy(r=>int.TryParse(r.Layer,out var n)?n:0))
        {
            var style=Document.FindStyle(row);var font=AssTags.Get(row,"fn",style?.Get("Fontname","Microsoft YaHei")??"Microsoft YaHei");double fs=Math.Clamp(AssTags.Number(row,"fs",style?.Number("Fontsize",48)??48)*sy,1,500);bool bold=AssTags.Number(row,"b",style?.Number("Bold",0)??0)!=0;bool italic=AssTags.Number(row,"i",style?.Number("Italic",0)??0)!=0;
            Brush fill=Brushes.White;try{fill=new SolidColorBrush((Color)ColorConverter.ConvertFromString(AssTags.AssToHex(AssTags.Get(row,"c",style?.Get("PrimaryColour","&H00FFFFFF")??"&H00FFFFFF"))));}catch{}
            if(System.Text.RegularExpressions.Regex.IsMatch(row.Text,@"\\p[1-9]"))
            {
                var polygon=BasicDrawing.Geometry(row.Text);
                if(polygon!=null)
                {
                    var location=Position(row);var transform=new Matrix(sx,0,0,sy,rect.X+location.X*sx,rect.Y+location.Y*sy);
                    polygon.Transform=new MatrixTransform(transform);dc.PushClip(new RectangleGeometry(rect));dc.DrawGeometry(fill,null,polygon);dc.Pop();
                    if(row==Selected&&Editing){hit=polygon.Bounds;hit.Inflate(5,5);dc.DrawRectangle(null,new Pen(Brushes.DeepSkyBlue,1),hit);}
                }
                continue;
            }
            var ft=new FormattedText(row.Plain,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(new FontFamily(font),italic?FontStyles.Italic:FontStyles.Normal,bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),fs,fill,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(20,rect.Width*.96),TextAlignment=TextAlignment.Center};
            // Geometry width reflects actual text rather than the wrapping constraint.
            var geo=ft.BuildGeometry(new Point(0,0));var bounds=geo.Bounds;if(bounds.IsEmpty)continue;var p=Position(row);double x=rect.X+p.X*sx,y=rect.Y+p.Y*sy;
            int col=(p.Alignment-1)%3;x-=col==0?bounds.Left:col==1?bounds.Left+bounds.Width/2:bounds.Right;y-=p.Alignment<=3?bounds.Bottom:p.Alignment<=6?bounds.Top+bounds.Height/2:bounds.Top;
            dc.PushClip(new RectangleGeometry(rect));dc.PushTransform(new TranslateTransform(x,y));double shadow=Math.Max(0,AssTags.Number(row,"shad",style?.Number("Shadow",1)??1)*sy);if(shadow>0){dc.PushTransform(new TranslateTransform(shadow,shadow));dc.DrawGeometry(Brushes.Black,null,geo);dc.Pop();}
            Brush outline=Brushes.Black;try{outline=new SolidColorBrush((Color)ColorConverter.ConvertFromString(AssTags.AssToHex(style?.Get("OutlineColour","&H0018202A")??"&H0018202A")));}catch{}
            double border=Math.Max(0,AssTags.Number(row,"bord",style?.Number("Outline",2)??2)*sy);if(border>0)dc.DrawGeometry(null,new Pen(outline,border*2){LineJoin=PenLineJoin.Round},geo);dc.DrawGeometry(fill,null,geo);dc.Pop();dc.Pop();
            if(row==Selected&&Editing){hit=new Rect(bounds.X+x-5,bounds.Y+y-5,bounds.Width+10,bounds.Height+10);dc.DrawRectangle(null,new Pen(Brushes.DeepSkyBlue,1),hit);}
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {if(!Editing||Selected==null||!hit.Contains(e.GetPosition(this)))return;if(BeginMove?.Invoke()==false){e.Handled=true;return;}var p=Position(Selected);initialX=p.X;initialY=p.Y;origin=e.GetPosition(this);dragging=true;CaptureMouse();Cursor=Cursors.SizeAll;e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){if(!dragging||Document==null)return;var p=e.GetPosition(this);Moved?.Invoke(Math.Clamp(initialX+(p.X-origin.X)/ContentRect.Width*Document.PlayResX,0,Document.PlayResX),Math.Clamp(initialY+(p.Y-origin.Y)/ContentRect.Height*Document.PlayResY,0,Document.PlayResY));}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(!dragging)return;dragging=false;ReleaseMouseCapture();Cursor=Cursors.Arrow;EndMove?.Invoke();}
}
