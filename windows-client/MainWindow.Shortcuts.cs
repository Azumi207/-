using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Frameflow;
public partial class MainWindow
{
    static DependencyObject? ParentOf(DependencyObject node)=>node is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    static bool HasAncestor<T>(DependencyObject? source) where T:DependencyObject
    {
        for(var p=source;p!=null;p=ParentOf(p))if(p is T)return true;
        return false;
    }

    static bool ProtectTyping(DependencyObject? source)=>HasAncestor<TextBoxBase>(source)||HasAncestor<PasswordBox>(source)||HasAncestor<ComboBox>(source);

    void UpdateShortcutHint()
    {
        if(!ready)return;
        ShortcutHint.Text=ProtectTyping(Keyboard.FocusedElement as DependencyObject)
            ? "文字输入中 · Ctrl+P 播放 / 暂停"
            : "打轴键已启用 · S 播放  H 停止  D 句尾  E 句首";
    }

    void WindowKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Handled||e.IsRepeat)return;
        // Real key events originate at the focused element. Use that route rather
        // than requiring the waveform to retain focus after clicking a button.
        var source=e.OriginalSource as DependencyObject??Keyboard.FocusedElement as DependencyObject;
        bool text=ProtectTyping(source);
        if(HasAncestor<MenuItem>(source))return;
        bool editor=source==TextEditor||(source!=null&&TextEditor.IsAncestorOf(source));
        if(e.Key==Key.Escape){composing=false;e.Handled=HandleShortcut(Key.Escape,Keyboard.Modifiers,text,editor,false);return;}
        if(text&&(composing||e.Key==Key.ImeProcessed))return;
        // Non-text controls do not accept IME composition. The original physical
        // key remains usable if Windows marks the event as ImeProcessed.
        var key=e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;
        if(HandleShortcut(key,Keyboard.Modifiers,text,editor,HasAncestor<Frameflow.Controls.Waveform>(source)))e.Handled=true;
    }

    bool HandleShortcut(Key key,ModifierKeys modifiers,bool text,bool editor,bool wave)
    {
        // Never confuse AltGr / Alt / Windows shortcuts with an unmodified letter.
        if((modifiers&(ModifierKeys.Alt|ModifierKeys.Windows))!=0)return false;
        var e=new RoutedEventArgs();
        if(modifiers==ModifierKeys.Control||modifiers==(ModifierKeys.Control|ModifierKeys.Shift))
        {
            bool shift=modifiers.HasFlag(ModifierKeys.Shift);
            if(shift&&key!=Key.S&&key!=Key.N&&key!=Key.V)return false;
            switch(key)
            {
                case Key.Return:if(!editor)return false;ApplyEditor();break;
                case Key.S:Save(shift);break;
                case Key.O:OpenClick(this,e);break;
                case Key.N:if(shift)NewWindowClick(this,e);else NewClick(this,e);break;
                case Key.C:if(text)return false;CopyRowsClick(this,e);break;
                case Key.X:if(text)return false;CutRowsClick(this,e);break;
                case Key.V:if(text)return false;if(shift)SelectivePasteClick(this,e);else PasteRowsClick(this,e);break;
                case Key.F:FindClick(this,e);break;
                case Key.G:if(VideoAvailable)Guard(()=>RunVideoOperation("jump"));else Status.Text="跳转需要先打开视频。";break;
                case Key.Z:if(text)return false;UndoClick(this,e);break;
                case Key.Y:if(text)return false;RedoClick(this,e);break;
                case Key.D3:SetStartClick(this,e);break;
                case Key.D4:SetEndClick(this,e);break;
                case Key.I:ShiftTimesClick(this,e);break;
                case Key.D5:
                    if(!VideoAvailable||timingFrames?.Keyframes.Length is not >0||selected==null)Status.Text="贴齐场景需要视频、选中字幕及帧时间表/关键帧。";
                    else SnapSceneClick(this,e);break;
                case Key.D6:
                    if(!VideoAvailable||selected==null)Status.Text="整体对齐需要视频与选中字幕。";
                    else AlignToCurrentClick(this,e);break;
                case Key.D1:if(selected!=null){if(VideoAvailable)SeekVideoOnly(Math.Min(VideoDuration,selected.Start));else Seek(selected.Start);}break;
                case Key.D2:if(selected!=null){if(VideoAvailable)SeekVideoOnly(Math.Min(VideoDuration,selected.End));else Seek(selected.End);}break;
                case Key.P:PlayClick(this,e);break;
                case Key.Delete:if(text)return false;DeleteClick(this,e);break;
                default:return false;
            }
            return true;
        }
        if(key==Key.F3&&(modifiers==ModifierKeys.None||modifiers==ModifierKeys.Shift)){RepeatFind(modifiers==ModifierKeys.Shift);return true;}
        if(modifiers!=ModifierKeys.None)return false; // Shift+Enter stays a newline.
        if(key==Key.F2){Save(false);return true;}
        if(key==Key.Escape&&workspaceFullscreen){SetWorkspaceFullscreen(false);return true;}
        if(key==Key.F11){SetWorkspaceFullscreen(!workspaceFullscreen);return true;}
        if(editor&&key==Key.Return){CommitNextClick(this,e);return true;}
        if(text)return false;
        switch(key)
        {
            case Key.S:case Key.Space:case Key.R:case Key.NumPad5:PlaySelectionClick(this,e);break;
            case Key.H:case Key.NumPad8:Pause();break;
            case Key.D:PlayEndingClick(this,e);break;
            case Key.E:PlayBeginningClick(this,e);break;
            case Key.B:PlayClick(this,e);break;
            case Key.T:PlayRange(PlaybackPosition,duration);break;
            case Key.Q:case Key.NumPad1:if(selected!=null)PlayRange(Math.Max(0,selected.Start-.5),selected.Start);break;
            case Key.W:case Key.NumPad3:if(selected!=null)PlayRange(selected.End,selected.End+.5);break;
            case Key.G:Next(true);Wave.Focus();break;
            case Key.Return:if(!wave)return false;Next(true);Wave.Focus();break;
            case Key.Z:case Key.NumPad0:PreviousClick(this,e);break;
            case Key.X:case Key.NumPad2:Next();break;
            // Arrow and Enter remain local so grid, slider and button navigation works.
            case Key.Left:if(!wave)return false;PreviousClick(this,e);break;
            case Key.Right:if(!wave)return false;Next();break;
            case Key.A:WaveScroll.Value=Math.Max(0,Wave.ViewStart-Wave.Span*.5);break;
            case Key.F:WaveScroll.Value=Wave.ViewStart+Wave.Span*.5;break;
            default:return false;
        }
        return true;
    }
}
