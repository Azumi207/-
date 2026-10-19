using System.Windows;
using System.Windows.Media;
namespace Frameflow.Controls;
internal sealed class VideoGuides:FrameworkElement
{
    public Rect Frame {get;set;}
    public bool Enabled {get;set;}
    public VideoGuides(){IsHitTestVisible=false;}
    protected override void OnRender(DrawingContext dc){if(!Enabled||Frame.IsEmpty)return;foreach(double inset in new[]{.05,.1}){var r=Frame;r.Inflate(-Frame.Width*inset,-Frame.Height*inset);dc.DrawRectangle(null,new Pen(inset==.05?Brushes.Gold:Brushes.DeepSkyBlue,1){DashStyle=DashStyles.Dash},r);}}
}
