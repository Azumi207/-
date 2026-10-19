using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using Frameflow.Core;
using Frameflow.Controls;

namespace Frameflow;
public partial class MainWindow
{
    public async Task RunAppearanceTestsAsync(string dir)
    {
        var log=new List<string>();
        void Check(bool value,string label){if(!value)throw new Exception("FAIL APPEARANCE: "+label);log.Add("PASS: "+label);File.WriteAllText(Path.Combine(dir,"appearance-progress.txt"),string.Join(Environment.NewLine,log));}
        string ColorOf(Brush brush)=>((SolidColorBrush)brush).Color.ToString();
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Settle(){await Task.Delay(120);UpdateLayout();}
        Check(SmokeMode,"isolated appearance test mode");
        OpenSubtitle(Path.Combine(dir,"sample.ass"));
        Player.IsMuted=true;OpenMedia(Path.Combine(dir,"sample.avi"));
        var until=DateTime.UtcNow.AddSeconds(15);
        while((!mediaReady||Wave.Peaks.Length==0)&&DateTime.UtcNow<until)await Task.Delay(50);
        Check(mediaReady&&Wave.Peaks.Length>0,"actual video and decoded waveform load in new shell");
        Pause();Seek(1);await Settle();
        await RunShortcutTestsAsync(Check);
        Check(((Menu)VideoMenu.Parent).Items.Count==10,"all ten original top-level menus remain");
        Check(VideoPanel.CornerRadius.TopLeft==16,"native video panel uses rounded composition");
        PlayButton.ApplyTemplate();Check(PlayButton.Template.FindName("Chrome",PlayButton) is Border {CornerRadius.TopLeft:8},"native button has rounded template");
        string documentBefore=document.Serialize();var selectedBefore=selected;int undoBefore=undo.Count;var sourceBefore=Player.Source;
        TextEditor.Text+="（未提交草稿）";string draft=TextEditor.Text;
        foreach(string mode in new[]{"light","dark"})
        {
            EditorTabs.SelectedIndex=0;
            Click(mode=="light"?ThemeLightButton:ThemeDarkButton);await Settle();
            Check(Appearance.Mode==mode&&Appearance.IsDark==(mode=="dark"),mode+" button changes real theme");
            Check(ColorOf(Background)==(mode=="dark"?"#FF12151B":"#FFEEF1F6"),mode+" window background updates");
            Check(ColorOf(TextEditor.Background)==(mode=="dark"?"#FF151A23":"#FFF4F6FA"),mode+" editor uses themed input background");
            Check(ColorOf(SubtitleGrid.Background)==(mode=="dark"?"#FF1C2029":"#FFFFFFFF"),mode+" virtualized table background updates");
            Check(TextEditor.Text==draft&&draftDirty&&document.Serialize()==documentBefore&&selected==selectedBefore&&undo.Count==undoBefore&&Player.Source==sourceBefore,mode+" switching preserves uncommitted draft, selection, undo and media");
            string saved=File.ReadAllText(Appearance.SettingsPath);Appearance.Initialize(Appearance.SettingsPath);Check(Appearance.Mode==mode&&saved==File.ReadAllText(Appearance.SettingsPath),mode+" preference reloads without rewriting file");
            ActorBox.ApplyTemplate();Check(ActorBox.Template.FindName("PART_EditableTextBox",ActorBox) is TextBox,"editable speaker combo keeps native text input");
            StyleBox.IsDropDownOpen=true;await Settle();var comboPopup=(Popup)StyleBox.Template.FindName("PART_Popup",StyleBox);
            Check(comboPopup.IsOpen&&comboPopup.Child is Border cb&&ColorOf(cb.Background)==ColorOf(Appearance.Brush("PanelBackground")),mode+" native combo popup follows theme");
            CaptureElement((FrameworkElement)comboPopup.Child,Path.Combine(dir,mode+"-combo.png"));StyleBox.IsDropDownOpen=false;
            ViewMenu.IsSubmenuOpen=true;await Settle();var menuPopup=(Popup)ViewMenu.Template.FindName("PART_Popup",ViewMenu);
            Check(menuPopup.IsOpen&&menuPopup.Child is Border mb&&ColorOf(mb.Background)==ColorOf(Appearance.Brush("PanelBackground")),mode+" native menu popup follows theme");
            CaptureElement((FrameworkElement)menuPopup.Child,Path.Combine(dir,mode+"-menu.png"));ViewMenu.IsSubmenuOpen=false;
            for(int pane=0;pane<3;pane++){EditorTabs.SelectedIndex=pane;await Settle();CapturePng(Path.Combine(dir,mode+"-pane-"+pane+".png"));}
            var dialog=new BurnInDialog(document.Serialize(),null,0){Owner=this};dialog.Show();await Settle();
            Check(ColorOf(dialog.Background)==ColorOf(Background)&&ColorOf(dialog.InputVideo.Background)==ColorOf(TextEditor.Background),mode+" existing encoding dialog and fields follow theme");
            CaptureElement(dialog,Path.Combine(dir,mode+"-encoding.png"));dialog.Close();
        }
        TextEditor.Text=selected!.Text.Replace(@"\N","\n");draftDirty=false;EditorTabs.SelectedIndex=0;
        // A system-theme setting stores the mode, not a snapshot of the current palette.
        Click(ThemeSystemButton);Check(Appearance.Mode=="system"&&Appearance.IsDark==Appearance.SystemIsDark(),"follow-system resolves Windows app appearance without changing Windows settings");
        Check(File.ReadAllText(Appearance.SettingsPath).Contains("system"),"follow-system mode persisted");
        bool rejected=false;try{Appearance.Apply("invalid");}catch(ArgumentException){rejected=true;}Check(rejected&&Appearance.Mode=="system","invalid theme rejected without state change");
        File.WriteAllText(Appearance.SettingsPath,"{broken");Appearance.Initialize(Appearance.SettingsPath);Check(Appearance.Mode=="system","corrupt preference falls back safely");
        Click(ThemeDarkButton);await Settle();
        int staticRenders=Wave.StaticRenders,builds=Wave.BackgroundBuilds;
        for(int i=0;i<100;i++)Wave.Position=1+i*.001;
        await Settle();Check(Wave.StaticRenders==staticRenders&&Wave.BackgroundBuilds==builds,"theme does not regress retained playhead: 100 moves cause no waveform rebuild");
        PositionLabel.Text="0:00:01.20";timerSeeking=true;SeekBar.Value=1.2;timerSeeking=false;
        Check(VideoSurface.IsMeasureValid&&SeekBar.IsMeasureValid&&PositionLabel.IsMeasureValid,"themed playback chrome avoids per-tick layout invalidation");
        foreach(var size in new[]{(1080d,760d),(1440d,940d)})
        {
            WindowState=WindowState.Normal;Width=size.Item1;Height=size.Item2;await Settle();
            for(int pane=0;pane<3;pane++)
            {
                EditorTabs.SelectedIndex=pane;await Settle();
                Check(SubtitleGrid.ActualHeight>=100&&EditorTabs.ActualHeight>=200,"usable subtitle grid/editor at "+size+" pane "+pane);
                var point=SubtitleGrid.TranslatePoint(new Point(SubtitleGrid.ActualWidth,SubtitleGrid.ActualHeight),this);
                Check(point.X<=ActualWidth+1&&point.Y<=ActualHeight+1,"table fits window bounds at "+size+" pane "+pane);
                CapturePng(Path.Combine(dir,$"size-{size.Item1}-{pane}.png"));
            }
        }
        Check(HandleShortcut(Key.F11,ModifierKeys.None,false,false,false)&&workspaceFullscreen,"existing F11 enters full-screen with new shell");await Settle();
        EditorTabs.SelectedIndex=2;RefreshIssues();
        if(IssueList.Items.Count>0){IssueList.SelectedIndex=0;Check(IssueList.SelectedItem!=null,"review selection works in full-screen");}
        CapturePng(Path.Combine(dir,"dark-fullscreen.png"));Check(HandleShortcut(Key.Escape,ModifierKeys.None,false,false,false)&&!workspaceFullscreen,"Escape restores original window state");
        var peer=new MainWindow{SmokeMode=true,Width=1080,Height=760};peer.Show();await Settle();
        Click(ThemeLightButton);Check(ColorOf(peer.Background)=="#FFEEF1F6"&&Equals(peer.ThemeLightButton.Tag,"selected"),"theme updates another open window without replacing its document");peer.Close();
        EditorTabs.SelectedIndex=0;Width=1440;Height=940;await Settle();CaptureElement((FrameworkElement)Content,Path.Combine(dir,"final-light.png"));
        Click(ThemeDarkButton);await Settle();CaptureElement((FrameworkElement)Content,Path.Combine(dir,"final-dark.png"));
        Check(document.Serialize()==documentBefore,"appearance suite leaves subtitle model byte-identical");
        dirty=draftDirty=false;
        File.WriteAllText(Path.Combine(dir,"appearance-tests.txt"),string.Join(Environment.NewLine,log)+$"\nTOTAL: {log.Count} passed\n");
        Console.WriteLine(string.Join(Environment.NewLine,log)+$"\nTOTAL: {log.Count} passed");
    }
}
