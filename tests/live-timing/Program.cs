using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;

class Program
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
            string fixture=Path.GetFullPath(args[1]);
            var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var app=(Application)Activator.CreateInstance(assembly.GetType("Frameflow.App")!)!;
            app.GetType().GetMethod("InitializeComponent")!.Invoke(app,null);
            const BindingFlags f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            assembly.GetType("Frameflow.Core.Appearance")!.GetMethod("Initialize",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Path.Combine(output,"appearance.json"),false]);
            var window=(Window)Activator.CreateInstance(assembly.GetType("Frameflow.MainWindow")!)!;var type=window.GetType();
            object Get(string n)=>type.GetField(n,f)!.GetValue(window)!;
            void Set(string n,object? value)=>type.GetField(n,f)!.SetValue(window,value);
            object? Call(string n,params object?[] values)=>type.GetMethod(n,f)!.Invoke(window,values);
            object Prop(object o,string n)=>o.GetType().GetProperty(n,f)!.GetValue(o)!;
            double Position()=>(double)type.GetProperty("PlaybackPosition",f)!.GetValue(window)!;
            double Start()=>Convert.ToDouble(Prop(Get("selected"),"Start"));double End()=>Convert.ToDouble(Prop(Get("selected"),"End"));
            bool Playing()=>(bool)Get("playing");
            bool Dispatch(Key key,bool text=false)=>(bool)Call("HandleShortcut",key,ModifierKeys.None,text,text,!text)!;
            bool Routed(Key key,UIElement source){var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent};source.RaiseEvent(e);return e.Handled;}
            void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
            void Until(Func<bool> predicate,string label){var until=DateTime.UtcNow.AddSeconds(20);while(!predicate()&&DateTime.UtcNow<until)Pump(20);if(!predicate())throw new Exception("TIMEOUT "+label);}
            var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception("FAIL "+label);checks.Add(label);Console.WriteLine("PASS "+label);}
            type.GetProperty("SmokeMode")!.SetValue(window,true);window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();Pump(100);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));((DispatcherTimer)Get("autosave")).Stop();
            var cases=new List<object>();bool available=type.GetMethod("LiveStartClick",f)!=null;
            foreach(var mode in new[]{"video","audio-only","fullscreen"})
            {
                Call("Pause");Set("dirty",false);Set("draftDirty",false);Call("OpenSubtitle",Path.Combine(fixture,"live.ass"));
                ((CheckBox)Get("AudioOnly")).IsChecked=false;Call("OpenMedia",Path.Combine(fixture,mode=="audio-only"?"sample.wav":"sample.avi"));
                Until(()=>(bool)Get("mediaReady"),"media open");
                if(mode=="audio-only"){((CheckBox)Get("AudioOnly")).IsChecked=true;Until(()=>(bool)Get("audioReady"),"audio cache");}
                Call("SetWorkspaceFullscreen",mode=="fullscreen");Pump(120);
                Call("PlayRange",.2,.8);Until(()=>Position()>=.38,"playing before mark");
                double atStart=Position();bool first=Routed(Key.F8,(UIElement)Get("Wave"));double postStart=Position();
                double boundary=(double)Get("stopAt");Pump(850);
                double beforeEnd=Position();bool stillPlaying=Playing();bool last=Routed(Key.F9,(UIElement)Get("TextEditor"));double afterEnd=Position();Pump(180);
                double later=Position();
                cases.Add(new{mode,start_handled=first,end_handled=last,at_start=atStart,after_start=postStart,stop_at=double.IsFinite(boundary)?boundary:-1,clock_at_end=beforeEnd,clock_after_end=afterEnd,clock_later=later,playing_past_original_end=stillPlaying,playing_after_end=Playing(),row_start=Start(),row_end=End()});
                if(available)
                {
                    Check(first&&last,mode+" F8/F9 routed from waveform and editor");
                    Check(Math.Abs(postStart-atStart)<.12&&Math.Abs(afterEnd-beforeEnd)<.12,mode+" marking never seeks or restarts the media clock");
                    Check(stillPlaying&&Playing()&&later>beforeEnd+.08,mode+" sound clock continues beyond old selection end and after F9");
                    Check(Math.Abs(Start()-atStart)<.12&&Math.Abs(End()-beforeEnd)<.12&&End()>Start(),mode+" pair uses active player's timestamps");
                    Call("CapturePng",Path.Combine(output,mode+".png"));
                    double markedStart=Start(),markedEnd=End();Call("UndoClick",window,new RoutedEventArgs());
                    Check(Math.Abs(Start()-.2)<.001&&Math.Abs(End()-.8)<.001,mode+" one undo restores complete original pair");
                    Call("RedoClick",window,new RoutedEventArgs());Check(Start()==markedStart&&End()==markedEnd,mode+" redo restores both boundaries");
                }
                Call("Pause");
            }
            if(available)
            {
                Call("SetWorkspaceFullscreen",false);((CheckBox)Get("AudioOnly")).IsChecked=false;
                void Reset(){Call("Pause");Set("dirty",false);Set("draftDirty",false);Call("OpenSubtitle",Path.Combine(fixture,"live.ass"));Call("PlayRange",1d,5d);Pump(100);}
                Reset();double originalStart=Start(),originalEnd=End();Dispatch(Key.F8);Check(Start()==originalStart&&End()==originalEnd,"F8 after old end stages a point without invalidating ASS");
                Call("AutoSave");Check(Start()==originalStart&&End()==originalEnd,"autosave does not commit a provisional time");
                var wave=Get("Wave");Check(Prop(wave,"PendingStart") is double,"waveform has separate pending-start marker");
                Pump(70);int renders=(int)Prop(wave,"StaticRenders"),builds=(int)Prop(wave,"BackgroundBuilds");
                for(int i=0;i<100;i++)wave.GetType().GetProperty("Position")!.SetValue(wave,1d+i*.001);Pump(50);
                Check((int)Prop(wave,"StaticRenders")==renders&&(int)Prop(wave,"BackgroundBuilds")==builds,"pending marker retains lightweight moving playhead");
                Check(Dispatch(Key.Escape)&&Get("liveTimingRow")==null&&Playing(),"Escape cancels only pending mark, playback continues");
                Dispatch(Key.F8);Call("Seek",2d,true);Check(Get("liveTimingRow")==null,"seek cancels stale pending mark");
                Dispatch(Key.F8);Call("Pause");Check(Get("liveTimingRow")==null,"pause cancels pending mark");
                Check(Dispatch(Key.F8)&&Get("liveTimingRow")==null,"F8 while paused gives hint without playback or edit");
                Reset();Dispatch(Key.F8);Call("Next",false);Check(Get("liveTimingRow")==null,"switching subtitle cancels pending mark");
                Reset();Dispatch(Key.F8);Set("liveTimingStart",Position()+1);Dispatch(Key.F9);
                Check(Start()==.2&&End()==.8&&Get("liveTimingRow")!=null,"end before start is rejected non-modally without edit");
                Dispatch(Key.Escape);Reset();var editor=(TextBox)Get("TextEditor");editor.Text="正文不会丢失";
                Check(Routed(Key.F8,editor),"F8 works while text editor has focus");Pump(160);Routed(Key.F9,editor);
                Check((string)Prop(Get("selected"),"Text")=="正文不会丢失","F9 preserves uncommitted subtitle text");
                Reset();((CheckBox)Get("LiveNextCheck")).IsChecked=true;Dispatch(Key.F8);Pump(160);double beforeNext=Position();Dispatch(Key.F9);
                Check(Convert.ToInt32(Prop(Get("selected"),"Number"))==2&&Math.Abs(Position()-beforeNext)<.12&&Playing(),"optional next row never seeks to that row's old time");
                Dispatch(Key.F8);Pump(160);Dispatch(Key.F9);Check(Convert.ToInt32(Prop(Get("selected"),"Number"))==3&&Playing(),"last row creates next empty subtitle without interrupting playback");
                ((CheckBox)Get("LiveNextCheck")).IsChecked=false;
                Reset();Call("Pause");Check(!Dispatch(Key.S,true)&&!Playing(),"S remains ordinary typing in editor");
                Check(Dispatch(Key.S)&&Playing()&&Math.Abs((double)Get("stopAt")-.8)<.001,"S still plays original selection when not marking");
                Dispatch(Key.H);Check(!Playing(),"H remains stop");Dispatch(Key.D);Check(Playing(),"D remains ending preview");Dispatch(Key.H);Dispatch(Key.E);Check(Playing(),"E remains beginning preview");Dispatch(Key.H);
                Set("composing",true);Check(!Routed(Key.F8,(UIElement)Get("TextEditor")),"active IME composition remains protected");Set("composing",false);
                Call("CloseAudio");Set("mediaReady",false);Dispatch(Key.F8);Check(Get("liveTimingRow")==null,"no-media F8 is harmless");
            }
            var report=new{feature_available=available,cases,checks,check_count=checks.Count,input=fixture,method="Actual production WPF key routing and real media clocks. Muted fixture audio; no physical listening or hardware latency claim."};
            string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(output,"behavior.json"),json);Console.WriteLine(json);
            Set("dirty",false);Set("draftDirty",false);window.Close();app.Shutdown();return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
