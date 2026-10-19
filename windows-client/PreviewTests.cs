using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Media;
namespace Frameflow;
internal static class PreviewTests
{
    internal const string Script="""
[Script Info]
ScriptType: v4.00+
PlayResX: 640
PlayResY: 360
ScaledBorderAndShadow: yes
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,46,&H000000FF,&H00FF0000,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\an7\pos(10,10)\p1\c&H0000FF&}m 0 0 l 80 0 80 60 0 60
Dialogue: 0,0:00:02.00,0:00:04.00,Default,,0,0,0,,{\an7\pos(10,10)\p1\c&H00FF00&\clip(10,10,50,50)}m 0 0 l 80 0 80 60 0 60
Dialogue: 0,0:00:04.00,0:00:06.00,Default,,0,0,0,,{\an7\move(20,30,200,30)\p1\c&HFF0000&\t(0,2000,\fscx180)}m 0 0 l 40 0 40 30 0 30
Dialogue: 0,0:00:06.00,0:00:08.00,Default,,0,0,0,,{\an7\pos(40,70)\kf100}HELLO{\kf100}WORLD
Dialogue: 0,0:00:08.00,0:00:10.00,Default,,0,0,0,,{\an7\pos(10,10)\p1\c&H00FFFF&\iclip(m 10 10 l 50 10 50 50 10 50)}m 0 0 l 80 0 80 60 0 60
Dialogue: 0,0:00:10.00,0:00:12.00,Default,,0,0,0,,{\an7\pos(40,100)\fnFrameflow Missing Font 7123\fad(200,300)\t(0,1500,\frz25\fs64)}Fallback test
Comment: 0,0:00:00.00,0:00:12.00,Default,,0,0,0,,THIS COMMENT MUST NOT BE VISIBLE
""";
    internal static string Hash(byte[] b,int length)=>Convert.ToHexString(SHA256.HashData(b.AsSpan(0,length)));
    internal static void Save(AssFrame frame,string path)
    {
        var bitmap=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Pbgra32,null,frame.Pixels,frame.Width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
    }
    internal static async Task<AssFrame> Frame(AssPreviewWorkspace work,double time)
    {
        AssFrame? result=null;await work.RenderAsync(640,360,time,false,f=>{result=f;return Task.CompletedTask;},CancellationToken.None);return result??throw new Exception("No rendered frame");
    }
    static int Pixels(AssFrame f,Func<byte,byte,byte,byte,bool> predicate){int n=0;for(int i=0;i<f.Width*f.Height*4;i+=4)if(predicate(f.Pixels[i],f.Pixels[i+1],f.Pixels[i+2],f.Pixels[i+3]))n++;return n;}
    internal static async Task RunAsync(string dir,Action<bool,string> check)
    {
        Directory.CreateDirectory(dir);string ass=Path.Combine(dir,"complex.ass"),video=Path.Combine(dir,"source.mp4");File.WriteAllText(ass,Script);
        var generated=await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),["-hide_banner","-nostdin","-y","-f","lavfi","-i","color=c=0x202040:s=640x360:r=24:d=12","-f","lavfi","-i","sine=frequency=330:duration=12","-c:v","libx264","-pix_fmt","yuv420p","-threads","2","-c:a","aac","-t","12",video],null,CancellationToken.None,null,null,30);
        check(generated.Exit==0,"actual video/audio fixture generated");string sourceHash=Hash(File.ReadAllBytes(video),(int)new FileInfo(video).Length),assHash=Hash(File.ReadAllBytes(ass),(int)new FileInfo(ass).Length);
        using(var work=new AssPreviewWorkspace(Script,"",CancellationToken.None))
        {
            using var red=await Frame(work,.5);Save(red,Path.Combine(dir,"vector.png"));
            check(Pixels(red,(b,g,r,a)=>r>200&&b<20&&g<20&&a>200)>3000,"libass renders ASS vector drawing");
            check(Pixels(red,(b,g,r,a)=>a==0)>220000,"background is genuinely transparent");
            check(Pixels(red,(b,g,r,a)=>Math.Max(b,Math.Max(g,r))>a)==0,"FFmpeg overlay has premultiplied alpha for WPF");
            using var clip=await Frame(work,2.5);Save(clip,Path.Combine(dir,"clip.png"));
            check(Pixels(clip,(b,g,r,a)=>g>200&&b<20&&r<20&&a>200) is >1400 and <1800,"rectangular ASS clip limits actual rendered pixels");
            using var inverse=await Frame(work,8.5);Save(inverse,Path.Combine(dir,"inverse-vector-clip.png"));
            check(inverse.Pixels[(30*640+30)*4+3]==0&&inverse.Pixels[(60*640+70)*4+3]>200,"inverse vector clip removes its mask without removing outer drawing");
            using var early=await Frame(work,4.2);using var late=await Frame(work,5.5);
            check(Hash(early.Pixels,640*360*4)!=Hash(late.Pixels,640*360*4),"move and animated transform change real frames with absolute ASS time");
            Save(early,Path.Combine(dir,"animation-early.png"));Save(late,Path.Combine(dir,"animation-late.png"));
            using var k0=await Frame(work,6.1);using var k1=await Frame(work,7.8);
            check(Pixels(k1,(b,g,r,a)=>r>150&&b<60&&a>150)>Pixels(k0,(b,g,r,a)=>r>150&&b<60&&a>150),"progressive karaoke switches secondary to primary color over time");
            Save(k0,Path.Combine(dir,"karaoke-early.png"));Save(k1,Path.Combine(dir,"karaoke-late.png"));
            using var fallback=await Frame(work,10.8);
            check(Pixels(fallback,(b,g,r,a)=>a>20)>100,"missing requested font still renders through real provider fallback");
            check(work.Diagnostics.Contains("fontselect: (Frameflow Missing Font 7123")&&work.Diagnostics.Contains(" -> "),"actual font replacement is observable in engine diagnostics");
            check(work.Diagnostics.Contains("libass API version")&&work.LastExitCode==0,"observed engine process exit confirms libass, not a simplified tag parser");
            File.WriteAllText(Path.Combine(dir,"font-diagnostics.txt"),work.Diagnostics);
        }
        var fontDir=Path.Combine(dir,"fonts [字体]");Directory.CreateDirectory(fontDir);string originalFont=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"arial.ttf");string custom=Path.Combine(fontDir,"custom.ttf");File.Copy(originalFont,custom,true);string fontHash=Hash(File.ReadAllBytes(custom),(int)new FileInfo(custom).Length);
        using(var native=new NativeAssRenderer(fontDir))
        using(var reference=new AssPreviewWorkspace(Script,fontDir,CancellationToken.None))
        {
            var samples=new List<object>();
            foreach(double time in new[]{.5,2.5,4.2,5.5,6.1,7.8,8.5,10.8})
            {
                using var exact=await Frame(reference,time);using var live=native.Render(Script,640,360,time,CancellationToken.None);
                Save(exact,Path.Combine(dir,"ref-"+time+".png"));Save(live,Path.Combine(dir,"live-"+time+".png"));
                int foreground=0;double error=0;
                for(int i=0;i<640*360*4;i+=4)if(exact.Pixels[i+3]>0||live.Pixels[i+3]>0){foreground++;for(int c=0;c<4;c++)error+=Math.Abs(exact.Pixels[i+c]-live.Pixels[i+c]);}
                double mae=error/Math.Max(1,foreground*4);samples.Add(new{time,foreground,rgba_mae=mae});
                check(foreground>100&&mae<2,"native cached libass agrees with FFmpeg foreground at ASS time "+time+"; RGBA MAE="+mae.ToString("0.###"));
                check(Pixels(live,(b,g,r,a)=>Math.Max(b,Math.Max(g,r))>a)==0,"native blend remains premultiplied at "+time);
            }
            int processes=AssPreviewWorkspace.ProcessLaunches;var times=new List<double>();
            for(int i=0;i<60;i++)
            {
                var watch=Stopwatch.StartNew();using var frame=native.Render(Script.Replace(@"\pos(40,70)",@"\pos("+(40+i)+",70)"),3840,2160,6.5,CancellationToken.None);times.Add(watch.Elapsed.TotalMilliseconds);
            }
            check(AssPreviewWorkspace.ProcessLaunches==processes&&native.RenderCount==68,"60 native 4K-input updates reuse renderer and launch no FFmpeg processes");
            check(native.Diagnostics.Contains("fontselect")&&native.Diagnostics.Contains("Frameflow Missing Font 7123"),"native diagnostics record actual font lookup and fallback");
            foreach(string matrix in new[]{"None","TV.709","PC.709","PC.601"})
            {
                string script=Script.Replace("ScriptType: v4.00+","ScriptType: v4.00+\nYCbCr Matrix: "+matrix);
                using var compare=new AssPreviewWorkspace(script,fontDir,CancellationToken.None);using var exact=await Frame(compare,.5);using var live=native.Render(script,640,360,.5,CancellationToken.None);
                int i=(30*640+30)*4;
                check(Enumerable.Range(0,4).All(c=>Math.Abs(exact.Pixels[i+c]-live.Pixels[i+c])<=1),"native RGB range agrees with pinned FFmpeg for "+matrix);
            }
            File.WriteAllText(Path.Combine(dir,"native-comparison.json"),System.Text.Json.JsonSerializer.Serialize(new{samples,render_ms=times,median_ms=times.Order().ElementAt(30),p95_ms=times.Order().ElementAt(56),diagnostics=native.Diagnostics},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
        using(var native=new NativeAssRenderer(""))
        {
            using var cancel=new CancellationTokenSource();cancel.Cancel();bool canceled=false;
            try{using var ignored=native.Render(Script,640,360,1,cancel.Token);}catch(OperationCanceledException){canceled=true;}
            check(canceled,"native render honors cancellation before touching track state");native.Dispose();bool disposed=false;
            try{using var ignored=native.Render(Script,640,360,1,CancellationToken.None);}catch(ObjectDisposedException){disposed=true;}
            check(disposed,"native handles dispose idempotently and reject use after free");
        }
        string owned;using(var work=new AssPreviewWorkspace(Script,fontDir,CancellationToken.None)){owned=work.DirectoryPath;using var frame=await Frame(work,6.5);check(work.Diagnostics.Contains("FONT: "+custom)&&work.Diagnostics.Contains("fontselect"),"custom font folder is staged read-only and used by preview");}
        check(!Directory.Exists(owned)&&Hash(File.ReadAllBytes(custom),(int)new FileInfo(custom).Length)==fontHash,"preview workspace is removed, original font is unchanged");
        using(var work=new AssPreviewWorkspace(Script,"",CancellationToken.None))
        using(var cancel=new CancellationTokenSource())
        {
            var buffer=new AssFrameBuffer();var task=work.RenderAsync(640,360,0,true,f=>buffer.AddAsync(f,cancel.Token),cancel.Token);var deadline=DateTime.UtcNow.AddSeconds(10);
            while(buffer.Count<3&&DateTime.UtcNow<deadline)await Task.Delay(30);
            await Task.Delay(100);check(buffer.Count==3,"continuous rendering is backpressured at three queued frames, not unbounded RAM");
            using var current=buffer.Take(.04);check(current!=null&&current.Time<=.042,"consumer takes latest eligible frame, never displays future subtitles");
            cancel.Cancel();try{await task;}catch(OperationCanceledException){}buffer.Clear();
            check(work.LastExitCode!=null&&buffer.Count==0,"cancel terminates actual render child and releases queued buffers");
            bool alive;try{using var p=Process.GetProcessById(work.LastProcessId!.Value);alive=!p.HasExited;}catch(ArgumentException){alive=false;}check(!alive,"no preview FFmpeg process survives cancellation");
        }
        check(Hash(File.ReadAllBytes(video),(int)new FileInfo(video).Length)==sourceHash&&Hash(File.ReadAllBytes(ass),(int)new FileInfo(ass).Length)==assHash,"all preview checks preserve original video and ASS bytes");
    }
}
