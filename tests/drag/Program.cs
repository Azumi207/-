using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
class Program
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
            var a=Assembly.LoadFrom(Path.GetFullPath(args[0]));var app=(Application)Activator.CreateInstance(a.GetType("Frameflow.App")!)!;app.GetType().GetMethod("InitializeComponent")!.Invoke(app,null);
            const BindingFlags f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            a.GetType("Frameflow.Core.Appearance")!.GetMethod("Initialize",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Path.Combine(output,"appearance.json"),false]);
            var w=(Window)Activator.CreateInstance(a.GetType("Frameflow.MainWindow")!)!;var t=w.GetType();object Get(string n)=>t.GetField(n,f)!.GetValue(w)!;void Set(string n,object v)=>t.GetField(n,f)!.SetValue(w,v);void Call(string n,params object[] p)=>t.GetMethod(n,f)!.Invoke(w,p);
            void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
            void Until(Func<bool> condition,string label){var deadline=DateTime.UtcNow.AddSeconds(20);while(!condition()&&DateTime.UtcNow<deadline)Pump(20);if(!condition())throw new Exception("Timeout: "+label);}
            t.GetProperty("SmokeMode")!.SetValue(w,true);w.ShowInTaskbar=false;w.ShowActivated=false;w.Show();Pump(100);SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(w.Dispatcher));((DispatcherTimer)Get("autosave")).Stop();
            var surface=(FrameworkElement)Get("preciseSurface");var overlay=(FrameworkElement)Get("Overlay");int Frames()=> (int)surface.GetType().GetProperty("FramesPresented",f)!.GetValue(surface)!;
            (int Count,double X,double Y) Pixels(bool green=false,string? image=null)
            {
                w.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
                if(image!=null){var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bitmap));using var s=File.Create(Path.Combine(output,image));enc.Save(s);}
                var b=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(b,bitmap.PixelWidth*4,0);int count=0;double x=0,y=0;
                for(int i=0;i<b.Length;i+=4)if(b[i+3]>30&&(green?b[i+1]>100&&b[i+2]<65&&b[i]<65:b[i+2]>100&&b[i+1]<65&&b[i]<65)){count++;x+=(i/4)%bitmap.PixelWidth;y+=(i/4)/bitmap.PixelWidth;}
                return(count,x/Math.Max(1,count),y/Math.Max(1,count));
            }
            Delegate Event(string name)=>(Delegate)overlay.GetType().GetField(name,f)!.GetValue(overlay)!;
            var results=new List<object>();bool allFixed=true;
            foreach(var scenario in new[]{(Multiple:false,Complex:false,Fullscreen:false),(Multiple:true,Complex:false,Fullscreen:false),(Multiple:false,Complex:true,Fullscreen:false),(Multiple:false,Complex:false,Fullscreen:true)})
            {
                bool multiple=scenario.Multiple;Call("SetWorkspaceFullscreen",scenario.Fullscreen);
                Set("dirty",false);Set("draftDirty",false);Call("OpenSubtitle",Path.GetFullPath(args[1]));Call("CreateDummyVideo",640,360,30d,10d,Colors.Black,false);Call("Pause");Call("Seek",1d,true);Call("SetPrecisePreview",true);
                if(multiple){var rows=((System.Collections.IEnumerable)Get("document").GetType().GetProperty("Rows")!.GetValue(Get("document"))!).Cast<object>().ToArray();rows[1].GetType().GetProperty("Comment")!.SetValue(rows[1],false);Call("MarkDirty");}
                if(scenario.Complex){var row=Get("selected");var text=row.GetType().GetProperty("Text")!;text.SetValue(row,@"{\clip(0,0,400,320)}"+(string)text.GetValue(row)!);Call("LoadEditor");Call("MarkDirty");}
                int current=Frames();Until(()=>Frames()>current,"initial real libass frame");Pump(50);var start=Pixels(image:multiple?"multi-before.png":"single-before.png");if(start.Count<100)throw new Exception("Fixture missing actual red glyphs");var green=Pixels(true);
                int undo=(int)Get("undo").GetType().GetProperty("Count")!.GetValue(Get("undo"))!;
                bool allowed=(bool)Event("BeginMove").DynamicInvoke()!;if(!allowed)throw new Exception("Drag was rejected");
                // Use the actual event handlers, keeping the mouse-capture state for the separate lost-capture test.
                overlay.GetType().GetField("dragging",f)!.SetValue(overlay,true);
                Event("Moved").DynamicInvoke(150d,110d);Pump(1);var immediate=Pixels();int beforeFrames=Frames();
                for(int i=0;i<32;i++){Event("Moved").DynamicInvoke(150d+i*2,110d);Pump(20);}
                var during=Pixels(image:multiple?"multi-during.png":"single-during.png");var greenDuring=Pixels(true);int liveFrames=Frames()-beforeFrames;
                bool bodyMovesDuring=during.X-start.X>30;bool otherStationary=!multiple||green.Count>100&&Math.Abs(greenDuring.X-green.X)<2;
                overlay.GetType().GetField("dragging",f)!.SetValue(overlay,false);Event("EndMove").DynamicInvoke();Pump(1);var release=Pixels();bool noBlank=release.Count>100;
                int beforeFinal=Frames();Until(()=>Frames()>beforeFinal,"release replacement frame");Pump(50);var final=Pixels(image:multiple?"multi-after.png":"single-after.png");
                int afterUndo=(int)Get("undo").GetType().GetProperty("Count")!.GetValue(Get("undo"))!;bool oneUndo=afterUndo==undo+1;
                Call("UndoClick",w,new RoutedEventArgs());int uf=Frames();Until(()=>Frames()>uf,"undo frame");Pump(30);var restored=Pixels();bool undoRestores=Math.Abs(restored.X-start.X)<2;
                Call("RedoClick",w,new RoutedEventArgs());int rf=Frames();Until(()=>Frames()>rf,"redo frame");Pump(30);var redo=Pixels();bool redoRestores=Math.Abs(redo.X-final.X)<2;
                bool immediateFollows=immediate.X-start.X>25;
                allFixed &= bodyMovesDuring&&noBlank&&otherStationary&&oneUndo&&undoRestores&&redoRestores&&(!multiple&&!scenario.Complex?immediateFollows:true);
                results.Add(new{multiple,complex=scenario.Complex,fullscreen=scenario.Fullscreen,allowed,start_count=start.Count,immediate_dx=immediate.X-start.X,during_dx=during.X-start.X,live_rendered_frames=liveFrames,body_moves_during=bodyMovesDuring,other_rows_stationary=otherStationary,release_nonblank=noBlank,final_dx=final.X-start.X,one_undo=oneUndo,undo_restores=undoRestores,redo_restores=redoRestores});
            }
            Event("BeginMove").DynamicInvoke();overlay.GetType().GetField("dragging",f)!.SetValue(overlay,true);Event("Moved").DynamicInvoke(260d,120d);
            overlay.GetType().GetMethod("OnLostMouseCapture",f)!.Invoke(overlay,[new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.LostMouseCaptureEvent}]);
            bool captureFinished=!(bool)overlay.GetType().GetField("dragging",f)!.GetValue(overlay)!;allFixed &= captureFinished;
            // Clean up baseline's old capture behavior explicitly only after recording it.
            if(!captureFinished){overlay.GetType().GetField("dragging",f)!.SetValue(overlay,false);Event("EndMove").DynamicInvoke();}
            Call("SetWorkspaceFullscreen",false);Set("dirty",false);Set("draftDirty",false);w.Close();app.Shutdown();
            var report=new{all_fixed=allFixed,lost_capture_finishes=captureFinished,input=Path.GetFullPath(args[1]),cases=results};string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(output,"drag-behavior.json"),json);Console.WriteLine(json);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
