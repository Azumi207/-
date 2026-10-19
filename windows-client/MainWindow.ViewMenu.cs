using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Frameflow.Controls;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    string workspaceLayout="full",menuLanguage="zh";int tagDisplay;bool crosshairVisible=true,workspaceFullscreen;WindowStyle priorWindowStyle;ResizeMode priorResizeMode;WindowState priorWindowState;string? displayOptionsOverride;
    string DisplayOptionsPath=>displayOptionsOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Frameflow","display-options.json");
    DisplayOptions CurrentDisplayOptions()=>new(workspaceLayout,tagDisplay,MainToolbar.Visibility==Visibility.Visible,crosshairVisible,Follow.IsChecked==true,Linked.IsChecked==true,playbackVolume,Wave.Gain,(int)autosave.Interval.TotalSeconds,SubtitleGrid.RowHeight,menuLanguage);
    void ViewOperationClick(object sender,RoutedEventArgs e)=>Guard(()=>RunViewOperation((string)((MenuItem)sender).Tag));
    void RunViewOperation(string op)
    {
        if(op.StartsWith("layout-")){workspaceLayout=op[7..];ApplyWorkspaceLayout();}
        else if(op.StartsWith("tags-")){tagDisplay=int.Parse(op[5..]);SetTagDisplay(tagDisplay);}
        else if(op=="toolbar")MainToolbar.Visibility=MainToolbar.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
        else if(op=="crosshair"){crosshairVisible=!crosshairVisible;Crosshair.Visibility=crosshairVisible?Visibility.Visible:Visibility.Hidden;}
        else if(op=="fullscreen")SetWorkspaceFullscreen(!workspaceFullscreen);
        else if(op=="reset"){SetVideoDetached(false);workspaceLayout="full";videoZoom=aspectOverride=0;MainToolbar.Visibility=Visibility.Visible;WorkspaceTopRow.Height=new GridLength(4.3,GridUnitType.Star);WorkspaceGrid.RowDefinitions[2].Height=new GridLength(1.5,GridUnitType.Star);ApplyWorkspaceLayout();UpdateVideoPresentation();}
        else if(op.StartsWith("language-")){SetMenuLanguage(op[9..]);Status.Text="仅一级菜单语言已切换；工具对话框保持中文。";}
        else if(op=="options"){var d=new PreferencesDialog(CurrentDisplayOptions()){Owner=this};if(d.ShowDialog()==true){var options=d.Options;options.Save(DisplayOptionsPath);ApplyDisplayOptions(options);Status.Text="显示与播放默认设置已保存。";}}
    }
    void ApplyWorkspaceLayout()
    {
        bool video=workspaceLayout is "full" or "video",audio=workspaceLayout is "full" or "audio";VideoPanel.Visibility=video||detachedVideo!=null?Visibility.Visible:Visibility.Collapsed;bool inMain=video&&detachedVideo==null;
        VideoColumn.MinWidth=inMain?350:0;VideoColumn.Width=inMain?new GridLength(49,GridUnitType.Star):new GridLength(0);VideoSplitColumn.Width=new GridLength(inMain?12:0);VideoSplitter.Visibility=inMain?Visibility.Visible:Visibility.Collapsed;
        foreach(var part in new FrameworkElement[]{AudioHeader,Wave,AudioTransport,AudioScrollPanel})part.Visibility=audio?Visibility.Visible:Visibility.Collapsed;AudioWaveRow.MinHeight=audio?100:0;AudioWaveRow.Height=audio?new GridLength(1.1,GridUnitType.Star):new GridLength(0);WorkspaceTopRow.MinHeight=audio?360:215;UpdateVideoPresentation();
    }
    void SetTagDisplay(int mode){if(mode is <0 or >2)throw new ArgumentOutOfRangeException(nameof(mode));tagDisplay=mode;((DataGridTextColumn)SubtitleGrid.Columns[8]).Binding=new Binding("Text"){Converter=new TagDisplayConverter(mode)};SubtitleGrid.Items.Refresh();}
    void SetMenuLanguage(string language)
    {
        menuLanguage=language;var menu=(Menu)VideoMenu.Parent;string[] zh=["文件(_F)","编辑(_E)","字幕(_S)","时间(_T)","视频(_V)","音频(_A)","自动化(_U)","视图(_W)","团队","帮助(_H)"];string[] en=["_File","_Edit","_Subtitles","_Timing","_Video","_Audio","A_utomation","Vie_w","Team","_Help"];var items=menu.Items.OfType<MenuItem>().ToArray();for(int i=0;i<Math.Min(items.Length,zh.Length);i++)items[i].Header=language=="en"?en[i]:zh[i];
    }
    void ApplyDisplayOptions(DisplayOptions v){v.Validate();workspaceLayout=v.Layout;MainToolbar.Visibility=v.Toolbar?Visibility.Visible:Visibility.Collapsed;crosshairVisible=v.Crosshair;Crosshair.Visibility=v.Crosshair?Visibility.Visible:Visibility.Hidden;Follow.IsChecked=v.Follow;Linked.IsChecked=v.Linked;playbackVolume=v.Volume;Wave.Gain=v.WaveGain;autosave.Interval=TimeSpan.FromSeconds(v.AutosaveSeconds);SubtitleGrid.RowHeight=v.RowHeight;SetTagDisplay(v.Tags);SetMenuLanguage(v.MenuLanguage);ApplyAudioVolume();ApplyWorkspaceLayout();Wave.InvalidateVisual();}
    void SetWorkspaceFullscreen(bool full)
    {
        if(full==workspaceFullscreen)return;if(full){priorWindowStyle=WindowStyle;priorResizeMode=ResizeMode;priorWindowState=WindowState;WindowState=WindowState.Normal;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;}else{WindowState=WindowState.Normal;WindowStyle=priorWindowStyle;ResizeMode=priorResizeMode;WindowState=priorWindowState;}workspaceFullscreen=full;
    }
    void ViewMenuOpened(object sender,RoutedEventArgs e){if(!ReferenceEquals(sender,e.Source))return;foreach(var item in ViewMenu.Items.OfType<MenuItem>())if(item.Tag is string op)item.IsChecked=op=="layout-"+workspaceLayout||op=="tags-"+tagDisplay||op=="toolbar"&&MainToolbar.Visibility==Visibility.Visible||op=="crosshair"&&crosshairVisible||op=="fullscreen"&&workspaceFullscreen;}
}
