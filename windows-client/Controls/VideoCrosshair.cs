using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Frameflow.Controls;
public sealed class VideoCrosshair:FrameworkElement
{
    Point? pointer;Rect viewport;int scriptWidth,scriptHeight;
    public Point? ScriptPoint {get;private set;}
    public VideoCrosshair(){IsHitTestVisible=false;ClipToBounds=true;}
    public void SetPointer(Point? point,Rect content,int width,int height)
    {
        viewport=content;scriptWidth=width;scriptHeight=height;
        pointer=point.HasValue&&content.Width>0&&content.Height>0&&content.Contains(point.Value)?point:null;
        ScriptPoint=pointer is Point p?new Point(Math.Clamp(Math.Round((p.X-content.Left)/content.Width*width),0,width-1),Math.Clamp(Math.Round((p.Y-content.Top)/content.Height*height),0,height-1)):null;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(pointer is not Point p||ScriptPoint is not Point coord)return;
        dc.PushClip(new RectangleGeometry(viewport));
        // White backing keeps the thin black guide visible on both dark and light footage.
        foreach(var pen in new[]{new Pen(Brushes.White,2),new Pen(Brushes.Black,1)})
        {
            dc.DrawLine(pen,new Point(viewport.Left,p.Y),new Point(viewport.Right,p.Y));
            dc.DrawLine(pen,new Point(p.X,viewport.Top),new Point(p.X,viewport.Bottom));
        }
        var text=new FormattedText($"{coord.X:0}, {coord.Y:0}",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),13,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double x=Math.Clamp(p.X+9,viewport.Left+3,Math.Max(viewport.Left+3,viewport.Right-text.Width-7)),y=p.Y+8;
        if(y+text.Height+5>viewport.Bottom)y=p.Y-text.Height-9;
        y=Math.Max(viewport.Top+3,y);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(200,15,20,28)),null,new Rect(x-3,y-2,text.Width+6,text.Height+4),2,2);dc.DrawText(text,new Point(x,y));dc.Pop();
    }
}
