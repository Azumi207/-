using System.Windows;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    void InitializeAppearance()
    {
        SetResourceReference(StyleProperty,typeof(Window));
        Appearance.Changed+=RefreshAppearance;
        Closed+=(_,_)=>Appearance.Changed-=RefreshAppearance;
        RefreshAppearance();
    }
    void RefreshAppearance()
    {
        ThemeLightButton.Tag=Appearance.Mode=="light"?"selected":null;
        ThemeDarkButton.Tag=Appearance.Mode=="dark"?"selected":null;
        ThemeSystemButton.Tag=Appearance.Mode=="system"?"selected":null;
        ThemeLightItem.IsChecked=Appearance.Mode=="light";
        ThemeDarkItem.IsChecked=Appearance.Mode=="dark";
        ThemeSystemItem.IsChecked=Appearance.Mode=="system";
        Wave.RefreshTheme();PositionLabel.InvalidateVisual();SeekBar.InvalidateVisual();
    }
    void AppearanceClick(object sender,RoutedEventArgs e)
    {
        string mode=sender==ThemeLightButton||sender==ThemeLightItem?"light":sender==ThemeDarkButton||sender==ThemeDarkItem?"dark":"system";
        Guard(()=>{Appearance.Apply(mode);Status.Text="外观已切换并保存 · "+(mode=="light"?"浅色":mode=="dark"?"深色":"跟随系统")+" · 字幕与快捷键不变";});
    }
}
