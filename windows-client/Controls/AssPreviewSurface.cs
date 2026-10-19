using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Media;
namespace Frameflow.Controls;
internal sealed class AssPreviewSurface:FrameworkElement
{
    WriteableBitmap? bitmap;internal Rect Frame;Vector offset;internal double PresentedTime {get;private set;}=double.NaN;internal int FramesPresented {get;private set;}
    internal void SetOffset(double x,double y){var next=new Vector(x,y);if(next==offset)return;offset=next;InvalidateVisual();}
    internal void Present(AssFrame frame)
    {
        if(bitmap==null||bitmap.PixelWidth!=frame.Width||bitmap.PixelHeight!=frame.Height)bitmap=new WriteableBitmap(frame.Width,frame.Height,96,96,PixelFormats.Pbgra32,null);
        bitmap.WritePixels(new Int32Rect(0,0,frame.Width,frame.Height),frame.Pixels,frame.Width*4,0);PresentedTime=frame.Time;FramesPresented++;InvalidateVisual();
    }
    internal void Clear(){bitmap=null;offset=default;PresentedTime=double.NaN;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc){if(bitmap!=null&&!Frame.IsEmpty){dc.PushClip(new RectangleGeometry(Frame));dc.PushTransform(new TranslateTransform(offset.X,offset.Y));dc.DrawImage(bitmap,Frame);dc.Pop();dc.Pop();}}
}
