using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Frameflow.Controls;
internal static class BasicDrawing
{
    internal static string Create(IReadOnlyList<Point> points)
    {
        if(points.Count<3)throw new InvalidOperationException("至少点击三个点组成多边形。");
        if(points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)))throw new FormatException("无效坐标。");
        double area=0;for(int i=0;i<points.Count;i++){var p=points[i];var q=points[(i+1)%points.Count];area+=p.X*q.Y-q.X*p.Y;}
        if(Math.Abs(area)<1)throw new InvalidOperationException("多边形不能退化为一条线。");
        double x=points.Min(p=>p.X),y=points.Min(p=>p.Y);string N(double n)=>n.ToString("0.##",CultureInfo.InvariantCulture);
        return "{\\an7\\pos("+N(x)+","+N(y)+")\\bord0\\shad0\\p1}m "+N(points[0].X-x)+" "+N(points[0].Y-y)+" l "+string.Join(" ",points.Skip(1).Append(points[0]).Select(p=>N(p.X-x)+" "+N(p.Y-y)))+"{\\p0}";
    }
    internal static Geometry? Geometry(string text)
    {
        // Only our basic p1 polygon subset; never mistake arbitrary ASSDraw for text.
        var match=Regex.Match(text,@"\\p1\}m\s+([-\d.\s]+)\s+l\s+([-\d.\s]+)\{\\p0\}");if(!match.Success)return null;
        var numbers=Regex.Matches(match.Groups[1].Value+" "+match.Groups[2].Value,@"-?\d+(?:\.\d+)?").Select(m=>double.Parse(m.Value,CultureInfo.InvariantCulture)).ToArray();if(numbers.Length<8||numbers.Length%2!=0)return null;
        var figure=new PathFigure{StartPoint=new Point(numbers[0],numbers[1]),IsClosed=true,IsFilled=true};for(int i=2;i<numbers.Length;i+=2)figure.Segments.Add(new LineSegment(new Point(numbers[i],numbers[i+1]),true));return new PathGeometry([figure]);
    }
}
internal sealed class DrawingDialog:SubtitleToolDialog
{
    internal readonly List<Point> Points=[];
    internal readonly Canvas Surface=new(){Background=new SolidColorBrush(Color.FromRgb(25,35,49)),Height=330,ClipToBounds=true};
    internal string DrawingText {get;private set;}="";
    readonly int width,height;
    internal DrawingDialog(int scriptWidth,int scriptHeight):base("ASS 绘图 · 基础多边形",720,550)
    {
        width=scriptWidth;height=scriptHeight;Hint($"画布按脚本 {width} × {height} 坐标缩放。左键加点，至少三个点；插入独立绘图行，不覆盖原句。当前仅多边形，不是完整 ASSDraw/贝塞尔编辑器。");Body.Children.Add(Surface);
        Surface.MouseLeftButtonDown+=(_,e)=>{var p=e.GetPosition(Surface);var rect=Rect; if(rect.Contains(p))AddPoint(new((p.X-rect.X)/rect.Width*width,(p.Y-rect.Y)/rect.Height*height));};Surface.SizeChanged+=(_,_)=>Redraw();
        Action("撤回一点",()=>{if(Points.Count>0)Points.RemoveAt(Points.Count-1);Redraw();});Action("清空",()=>{Points.Clear();Redraw();});Action("插入绘图行",()=>{DrawingText=BasicDrawing.Create(Points);DialogResult=true;});Finish();
    }
    Rect Rect {get{double w=Math.Max(1,Surface.ActualWidth),h=Surface.Height,ratio=width/(double)height;if(w/h>ratio)w=h*ratio;else h=w/ratio;return new((Surface.ActualWidth-w)/2,(Surface.Height-h)/2,w,h);}}
    internal void AddPoint(Point point){Points.Add(new(Math.Clamp(point.X,0,width),Math.Clamp(point.Y,0,height)));Redraw();}
    void Redraw()
    {
        Surface.Children.Clear();var rect=Rect;var border=new Rectangle{Width=rect.Width,Height=rect.Height,Stroke=Brushes.SlateGray};Canvas.SetLeft(border,rect.X);Canvas.SetTop(border,rect.Y);Surface.Children.Add(border);
        var polygon=new Polygon{Stroke=Brushes.DeepSkyBlue,Fill=new SolidColorBrush(Color.FromArgb(90,67,176,235)),StrokeThickness=2};foreach(var p in Points)polygon.Points.Add(new(rect.X+p.X/width*rect.Width,rect.Y+p.Y/height*rect.Height));Surface.Children.Add(polygon);
        foreach(var p in polygon.Points){var dot=new Ellipse{Width=6,Height=6,Fill=Brushes.White};Canvas.SetLeft(dot,p.X-3);Canvas.SetTop(dot,p.Y-3);Surface.Children.Add(dot);}
        Message.Text=$"{Points.Count} 个顶点";
    }
}
