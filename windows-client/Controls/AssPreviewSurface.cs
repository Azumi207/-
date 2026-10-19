using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Media;
namespace Frameflow.Controls;
internal sealed class AssPreviewSurface:FrameworkElement
{
    WriteableBitmap? bitmap;internal Rect Frame;internal double PresentedTime {get;private set;}=double.NaN;internal int FramesPresented {get;private set;}
    internal void Present(AssFrame frame)
    {
        if(bitmap==null||bitmap.PixelWidth!=frame.Width||bitmap.PixelHeight!=frame.Height)bitmap=new WriteableBitmap(frame.Width,frame.Height,96,96,PixelFormats.Pbgra32,null);
        bitmap.WritePixels(new Int32Rect(0,0,frame.Width,frame.Height),frame.Pixels,frame.Width*4,0);PresentedTime=frame.Time;FramesPresented++;InvalidateVisual();
    }
    internal void Clear(){bitmap=null;PresentedTime=double.NaN;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc){if(bitmap!=null&&!Frame.IsEmpty)dc.DrawImage(bitmap,Frame);}
}
