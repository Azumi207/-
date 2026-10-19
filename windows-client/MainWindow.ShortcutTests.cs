using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Frameflow;
public partial class MainWindow
{
    async Task RunShortcutTestsAsync(Action<bool,string> check)
    {
        // Route keys through WPF, not the click callbacks. No SendInput is used:
        // tests cannot type into another application while running in background.
        bool KeyFrom(UIElement source,Key key)
        {
            var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent};
            source.RaiseEvent(e);return e.Handled;
        }
        EditorTabs.SelectedIndex=0;SelectRow(document.Rows.First());Pause();Seek(selected!.Start);
        double start=selected.Start,end=selected.End;
        foreach(var (source,name) in new (UIElement,string)[]{(Wave,"waveform"),(SubtitleGrid,"subtitle grid"),(PlayButton,"video toolbar"),(AudioPlayButton,"audio toolbar")})
        {
            check(KeyFrom(source,Key.S)&&playing&&Math.Abs(stopAt-end)<.001,$"S routes from {name} and plays selection");
            check(KeyFrom(source,Key.H)&&!playing,$"H routes from {name} and stops playback");
            check(KeyFrom(source,Key.D)&&playing&&Math.Abs(Player.Position.TotalSeconds-(end-.5))<.03&&Math.Abs(stopAt-end)<.001,$"D routes from {name} and plays last 500ms");
            KeyFrom(source,Key.H);
            check(KeyFrom(source,Key.E)&&playing&&Math.Abs(Player.Position.TotalSeconds-start)<.03&&Math.Abs(stopAt-(start+.5))<.001,$"E routes from {name} and plays first 500ms");
            KeyFrom(source,Key.H);
        }
        KeyFrom(SubtitleGrid,Key.S);await Task.Delay(250);
        check(Player.Position.TotalSeconds>start+.1,"keyboard S advances real media clock");KeyFrom(SubtitleGrid,Key.H);

        foreach(var source in new UIElement[]{TextEditor,StartBox,EndBox,LayerBox,ActorBox,StyleBox})
        {
            bool handled=false;foreach(var key in new[]{Key.S,Key.H,Key.D,Key.E,Key.Space,Key.NumPad5})handled|=KeyFrom(source,key);
            check(!handled&&!playing,$"{((FrameworkElement)source).Name} retains letters, spaces and digits for editing");
        }
        ActorBox.ApplyTemplate();var inner=(TextBox?)ActorBox.Template.FindName("PART_EditableTextBox",ActorBox);
        check(inner!=null&&!KeyFrom(inner,Key.S)&&!playing,"editable speaker combo protects its inner text box");
        EditorTabs.SelectedIndex=2;UpdateLayout();check(!KeyFrom(NoteBox,Key.S)&&!playing,"review note retains typing");EditorTabs.SelectedIndex=0;UpdateLayout();

        var composition=new TextComposition(InputManager.Current,TextEditor,"ni");
        TextEditor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputStartEvent});
        check(composing&&!KeyFrom(TextEditor,Key.Return)&&selected.Number==1,"simulated IME Enter does not submit a subtitle");
        check(KeyFrom(Wave,Key.S)&&playing,"stale text composition cannot disable waveform playback");KeyFrom(Wave,Key.H);
        TextEditor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,composition){RoutedEvent=TextCompositionManager.PreviewTextInputEvent,Handled=true});
        check(!composing,"handled IME completion still clears composition state");
        check(KeyFrom(TextEditor,Key.Return)&&selected.Number==2,"editor Enter submits next row after composition ends");
        SelectRow(document.Rows.First());Pause();
        check(!HandleShortcut(Key.Return,ModifierKeys.Shift,true,true,false),"Shift+Enter is left to the multiline editor");
        check(!HandleShortcut(Key.S,ModifierKeys.Alt,false,false,false)&&!HandleShortcut(Key.S,ModifierKeys.Control|ModifierKeys.Alt,false,false,false)&&!playing,"Alt and AltGr do not trigger S playback");
        check(!HandleShortcut(Key.S,ModifierKeys.Shift,false,false,false),"modified letters do not accidentally match unmodified shortcuts");
        check(!HandleShortcut(Key.Delete,ModifierKeys.Control,true,true,false),"Ctrl+Delete in text does not delete a subtitle row");
        check(!HandleShortcut(Key.Z,ModifierKeys.Control,true,true,false),"Ctrl+Z in text is delegated to native text undo");
        check(!KeyFrom(SubtitleGrid,Key.Left)&&!KeyFrom(PlayButton,Key.Return),"grid arrows and button Enter retain native navigation");
        check(KeyFrom(Wave,Key.NumPad5)&&playing&&KeyFrom(Wave,Key.NumPad8)&&!playing,"keypad 5 and 8 play and stop outside input fields");
        check(HandleShortcut(Key.P,ModifierKeys.Control,true,true,false)&&playing&&HandleShortcut(Key.P,ModifierKeys.Control,true,true,false)&&!playing,"Ctrl+P command works while editing text");
        Seek(1);check(HandleShortcut(Key.D3,ModifierKeys.Control,true,true,false)&&Math.Abs(selected!.Start-1)<.01,"Ctrl+3 command sets start to playback time");UndoClick(this,new RoutedEventArgs());
        Seek(2);check(HandleShortcut(Key.D4,ModifierKeys.Control,true,true,false)&&Math.Abs(selected!.End-2)<.01,"Ctrl+4 command sets end to playback time");UndoClick(this,new RoutedEventArgs());
        check(KeyFrom(TextEditor,Key.F2)&&!dirty&&File.Exists(subtitlePath),"F2 routes from editor and saves existing subtitle path");
        Pause();Seek(selected!.Start);
    }
}
