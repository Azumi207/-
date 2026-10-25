using System.Reflection;
using System.Diagnostics;
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
  try {
   string output=Path.GetFullPath(args[2]),fixture=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
   var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));var app=(Application)Activator.CreateInstance(assembly.GetType("Frameflow.App")!)!;app.GetType().GetMethod("InitializeComponent")!.Invoke(app,null);
   const BindingFlags f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
   assembly.GetType("Frameflow.Core.Appearance")!.GetMethod("Initialize",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Path.Combine(output,"appearance.json"),false]);
   var w=(Window)Activator.CreateInstance(assembly.GetType("Frameflow.MainWindow")!)!;var t=w.GetType();
   object Get(string n)=>t.GetField(n,f)!.GetValue(w)!;
   void Set(string n,object? v)=>t.GetField(n,f)!.SetValue(w,v);
   object? Call(string n,params object?[] a)=>t.GetMethod(n,f)!.Invoke(w,a);
   object P(object o,string n)=>o.GetType().GetProperty(n,f)!.GetValue(o)!;
   void SP(object o,string n,object v)=>o.GetType().GetProperty(n,f)!.SetValue(o,v);
   void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
   void Until(Func<bool> test,string label){var end=DateTime.UtcNow.AddSeconds(30);while(!test()&&DateTime.UtcNow<end)Pump(20);if(!test())throw new Exception("TIMEOUT "+label);}
   var checks=new List<string>();void Check(bool b,string label){if(!b)throw new Exception("FAIL "+label);checks.Add(label);Console.WriteLine("PASS "+label);}
   t.GetProperty("SmokeMode")!.SetValue(w,true);w.ShowActivated=false;w.ShowInTaskbar=false;w.Show();Pump(100);SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(w.Dispatcher));((DispatcherTimer)Get("autosave")).Stop();
   bool available=t.GetMethod("FineNudge",f)!=null;
   void Reset(){Call("Pause");Set("dirty",false);Set("draftDirty",false);Call("OpenSubtitle",Path.Combine(fixture,"live.ass"));}
   Reset();Call("OpenMedia",Path.Combine(fixture,"sample.avi"));Until(()=>(bool)Get("mediaReady")&&((Array)P(Get("Wave"),"Peaks")).Length>0,"media and peaks");Call("SetPrecisePreview",true);Pump(1600);
   var cases=new List<object>();
   foreach(bool full in new[]{false,true}) {
    Reset();Call("SetWorkspaceFullscreen",full);Pump(300);
    var wave=Get("Wave");var wt=wave.GetType();SP(wave,"ViewStart",0d);SP(wave,"Span",2d);((UIElement)wave).InvalidateVisual();Pump(100);
    var begin=(Func<bool>)wt.GetField("BeginEdit",f)!.GetValue(wave)!;var end=(Action)wt.GetField("EndEdit",f)!.GetValue(wave)!;
    Check(begin(),"begin drag "+full);SP(wave,"Dragging",true);wt.GetField("dragStart",f)!.SetValue(wave,true);
    long generation=(long)Get("previewGeneration");int updates=(int)P(w,"PlaybackUiUpdates"),backgrounds=(int)P(wave,"BackgroundBuilds"),renders=(int)P(wave,"StaticRenders");
    var durations=new List<double>();
    for(int i=0;i<120;i++) {var sw=Stopwatch.StartNew();wt.GetMethod("UpdateDrag",f)!.Invoke(wave,[(.1+i*.003)/2*((FrameworkElement)wave).ActualWidth]);sw.Stop();durations.Add(sw.Elapsed.TotalMilliseconds);if(i%6==0)Pump(16);}
    int seeks=(int)P(w,"PlaybackUiUpdates")-updates,builds=(int)P(wave,"BackgroundBuilds")-backgrounds,draws=(int)P(wave,"StaticRenders")-renders;long generations=(long)Get("previewGeneration")-generation;
    Console.WriteLine($"DRAG full={full} seeks={seeks} generations={generations} builds={builds} renders={draws}");
    if(available){Check(seeks<30,"120 pointer updates coalesced to <30 video seeks "+full);Check(generations<3,"no per-move ASS cancellation "+full);Check(builds==0&&draws<=1,"retained waveform background; at most initial redraw "+full);Check(Convert.ToDouble(P(Get("selected"),"Start"))==.2,"document not rewritten during drag "+full);}
    SP(wave,"Dragging",false);end();Pump(200);
    if(available){Until(()=>(string)Get("shownSnapshot")==Get("document").GetType().GetMethod("Serialize")!.Invoke(Get("document"),null)!.ToString(),"final precise preview");Check(!double.IsNaN((double)P(Get("preciseSurface"),"PresentedTime")),"final precise preview displayed "+full);}
    double final=Convert.ToDouble(P(Get("selected"),"Start"));Check(Math.Abs(final-.46)<.011,"final boundary committed "+full);
    Call("UndoClick",w,new RoutedEventArgs());Check(Convert.ToDouble(P(Get("selected"),"Start"))==.2,"single undo restores drag "+full);
    cases.Add(new{fullscreen=full,pointer_updates=120,media_ui_updates=seeks,preview_generations=generations,wave_background_rebuilds=builds,wave_full_renders=draws,median_callback_ms=durations.Order().ElementAt(60),max_callback_ms=durations.Max(),final_start=final});
   }
   if(available) {
    Call("SetWorkspaceFullscreen",false);Reset();
    var wave=Get("Wave");var wt=wave.GetType();
    long g=(long)Get("previewGeneration");int u=(int)P(w,"PlaybackUiUpdates");
    for(int i=0;i<120;i++){((Slider)Get("WaveScroll")).Value=i*.01;if(i%6==0)Pump(16);}
    Check((long)Get("previewGeneration")==g&&(int)P(w,"PlaybackUiUpdates")==u,"horizontal waveform panning never seeks or rerenders ASS");
    ((CheckBox)Get("Linked")).IsChecked=false;
    var begin=(Func<bool>)wt.GetField("BeginEdit",f)!.GetValue(wave)!;begin();SP(wave,"Dragging",true);wt.GetField("dragStart",f)!.SetValue(wave,false);
    SP(wave,"ViewStart",0d);SP(wave,"Span",2d);u=(int)P(w,"PlaybackUiUpdates");
    wt.GetMethod("UpdateDrag",f)!.Invoke(wave,[.5*((FrameworkElement)wave).ActualWidth]);Pump(140);
    wt.GetMethod("OnLostMouseCapture",f)!.Invoke(wave,[new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount)]);
    Check((int)P(w,"PlaybackUiUpdates")==u&&Convert.ToDouble(P(Get("selected"),"End"))==1,"lost capture commits end once without seeking when unlinked");
    Call("UndoClick",w,new RoutedEventArgs());Check(Convert.ToDouble(P(Get("selected"),"End"))==.8,"lost capture remains one undo");
    ((CheckBox)Get("Linked")).IsChecked=true;Reset();var exp=(Expander)Get("FineTimingPanel");exp.IsExpanded=true;Pump(100);Call("RefreshFineTiming",true);
    double Start()=>Convert.ToDouble(P(Get("selected"),"Start"));double End()=>Convert.ToDouble(P(Get("selected"),"End"));
    Call("FineNudge",10);Check(Math.Abs(Start()-.21)<.00001&&End()==.8,"+10ms affects only start");Call("FineNudge",-50);Check(Math.Abs(Start()-.16)<.00001,"-50ms exact centiseconds");
    ((RadioButton)Get("FineEndChoice")).IsChecked=true;Call("FineNudge",100);Check(Math.Abs(End()-.9)<.00001&&Math.Abs(Start()-.16)<.00001,"+100ms affects only end");
    Call("FineNudge",-10000);Check(End()>Start()&&Math.Abs(End()-Start()-.01)<.00001,"cannot cross other boundary");Call("UndoClick",w,new RoutedEventArgs());Check(Math.Abs(End()-.9)<.00001,"nudge undo");
    ((RadioButton)Get("FineStartChoice")).IsChecked=true;Call("FineNudge",-10000);Check(Start()==0,"start clamps to zero");Reset();Call("RefreshFineTiming",true);
    double overview=(double)P(Get("Wave"),"Span");((ComboBox)Get("FineZoomBox")).SelectedIndex=0;Check((double)P(Get("FineWave"),"Span")==.5&&(double)P(Get("Wave"),"Span")==overview,"detail zoom independent of overview");
    ((CheckBox)Get("FineLoopCheck")).IsChecked=true;Call("StartFineAudition",false);Until(()=>(int)P(w,"FineLoopRestarts")>0,"real loop restart");Check((bool)Get("playing"),"boundary loop continues on actual media clock");
    Call("FineNudge",10);Check((bool)Get("playing")&&Get("fineAudition")!=null,"nudge re-auditions active loop");Call("Pause");Check(Get("fineAudition")==null&&!(bool)Get("playing"),"stop cancels loop");
    ((CheckBox)Get("AudioOnly")).IsChecked=true;Until(()=>(bool)Get("audioReady"),"audio-only ready");int loops=(int)P(w,"FineLoopRestarts");Call("StartFineAudition",true);Until(()=>(int)P(w,"FineLoopRestarts")>loops,"audio-only actual selection loop");Call("Pause");
    Check((double)Get("fineAuditionStart")>=0&&(double)Get("fineAuditionEnd")<=Convert.ToDouble(Get("duration")),"audition clamped to media");
    Call("StartFineAudition",false);Call("Next",false);Check(Get("fineAudition")==null&&!(bool)Get("playing"),"row switch stops old audition");
    Reset();Call("StartFineAudition",false);exp.IsExpanded=false;Check(Get("fineAudition")==null&&!(bool)Get("playing"),"collapse stops audition");
    exp.IsExpanded=true;Pump(150);
    Call("AppearanceClick",Get("ThemeLightButton"),new RoutedEventArgs());Pump(150);Call("CapturePng",Path.Combine(output,"fine-light.png"));
    Call("AppearanceClick",Get("ThemeDarkButton"),new RoutedEventArgs());Pump(150);Call("CapturePng",Path.Combine(output,"fine-dark.png"));
    w.Width=1080;w.Height=760;Pump(100);exp.BringIntoView();Pump(150);Call("CapturePng",Path.Combine(output,"fine-small.png"));
    var detail=Get("FineWave");SP(detail,"Span",.5d);SP(detail,"ViewStart",0d);
    ((Func<bool>)detail.GetType().GetField("BeginEdit",f)!.GetValue(detail)!)();SP(detail,"Dragging",true);detail.GetType().GetField("dragStart",f)!.SetValue(detail,true);
    detail.GetType().GetMethod("UpdateDrag",f)!.Invoke(detail,[.33/.5*((FrameworkElement)detail).ActualWidth]);SP(detail,"Dragging",false);((Action)detail.GetType().GetField("EndEdit",f)!.GetValue(detail)!)();
    Check(Math.Abs(Start()-.33)<.00001,"detail waveform drag commits exact centiseconds");
    Call("UndoClick",w,new RoutedEventArgs());Check(Math.Abs(Start()-.2)<.00001,"detail drag can be undone");
    Reset();((RadioButton)Get("FineEndChoice")).IsChecked=true;((TextBox)Get("StartBox")).Text="0:00:00.40";
    ((Func<bool>)detail.GetType().GetField("BeginEdit",f)!.GetValue(detail)!)();SP(detail,"Dragging",true);detail.GetType().GetField("dragStart",f)!.SetValue(detail,false);
    SP(detail,"ViewStart",0d);SP(detail,"Span",2d);detail.GetType().GetMethod("UpdateDrag",f)!.Invoke(detail,[.5*((FrameworkElement)detail).ActualWidth]);SP(detail,"Dragging",false);((Action)detail.GetType().GetField("EndEdit",f)!.GetValue(detail)!)();
    Check(Start()==.4&&End()==1,"detail end drag preserves newly typed start");
    Reset();((RadioButton)Get("FineStartChoice")).IsChecked=true;((TextBox)Get("EndBox")).Text="0:00:01.20";
    ((Func<bool>)detail.GetType().GetField("BeginEdit",f)!.GetValue(detail)!)();SP(detail,"Dragging",true);detail.GetType().GetField("dragStart",f)!.SetValue(detail,true);
    SP(detail,"ViewStart",0d);SP(detail,"Span",2d);detail.GetType().GetMethod("UpdateDrag",f)!.Invoke(detail,[.15*((FrameworkElement)detail).ActualWidth]);SP(detail,"Dragging",false);((Action)detail.GetType().GetField("EndEdit",f)!.GetValue(detail)!)();
    Check(Start()==.3&&End()==1.2,"detail start drag preserves newly typed end");
    Reset();Call("StartFineAudition",false);((TextBox)Get("StartBox")).Text="invalid";
    Check(!(bool)Call("RestartFineAudition")!&&!(bool)Get("playing")&&Get("fineAudition")==null,"invalid draft stops automatic loop without modal reentry");((TextBox)Get("StartBox")).Text="0:00:00.20";
    Reset();Call("StartFineAudition",true);((TextBox)Get("StartBox")).Text="0:00:50.00";((TextBox)Get("EndBox")).Text="0:00:51.00";Call("StartFineAudition",true);
    Check(!(bool)Get("playing")&&Get("fineAudition")==null,"out-of-media range cancels previous loop");
    ((TextBox)Get("StartBox")).Text="0:00:00.20";((TextBox)Get("EndBox")).Text="0:00:00.80";Call("ApplyEditor",false);
    Reset();((ComboBox)Get("FineZoomBox")).SelectedIndex=1;Pump(40);
    detail.GetType().GetMethod("OnMouseWheel",f)!.Invoke(detail,[new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=Mouse.MouseWheelEvent}]);
    Check((double)P(detail,"Span")==.5&&((ComboBox)Get("FineZoomBox")).SelectedIndex==0,"detail mouse wheel syncs zoom preset");Call("FineNudge",10);Check((double)P(detail,"Span")==.5,"nudge retains detail wheel zoom");
    Reset();Call("CloseAudio");Set("mediaReady",false);((CheckBox)Get("AudioOnly")).IsChecked=false;Call("StartFineAudition",false);Check(!(bool)Get("playing")&&Get("fineAudition")==null,"no-media audition is harmless");
   }
   var report=new{feature_available=available,cases,checks,check_count=checks.Count,input=fixture,method="Production WPF drag callbacks at 120 pointer updates, real media clocks, muted fixtures. Callback duration is not end-to-end mouse latency."};var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(output,"behavior.json"),json);Console.WriteLine(json);Call("Pause");Set("dirty",false);Set("draftDirty",false);w.Close();app.Shutdown();return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
