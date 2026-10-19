using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Frameflow.Media;

// The interactive overlay and final encoder use the same FFmpeg/libass build,
// original script, font directory and font provider. No tags are reimplemented.
internal static class AssRendering
{
    internal static string Filter(bool alpha=false)=>"ass=filename=snapshot.ass:fontsdir=fonts"+(alpha?":alpha=1":"");
    internal static string Number(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
    internal static string[] CopyFonts(string source,string destination,CancellationToken token,Action<string>? log=null)
    {
        if(source.Length==0)return [];
        if(!Directory.Exists(source))throw new DirectoryNotFoundException("附加字体目录不存在。");
        var files=Directory.GetFiles(source).Where(f=>new[]{".ttf",".otf",".ttc",".otc"}.Contains(Path.GetExtension(f).ToLowerInvariant())).Order(StringComparer.Ordinal).ToArray();
        if(files.Length>200||files.Sum(f=>new FileInfo(f).Length)>300_000_000)throw new IOException("字体目录超过 200 个文件 / 300 MB。");
        var copied=new List<string>();
        try{for(int i=0;i<files.Length;i++){token.ThrowIfCancellationRequested();string target=Path.Combine(destination,i.ToString("000")+Path.GetExtension(files[i]));File.Copy(files[i],target,false);copied.Add(target);log?.Invoke("FONT: "+files[i]);}}
        catch{foreach(string path in copied)try{File.Delete(path);}catch(IOException){}throw;}
        return copied.ToArray();
    }
}
internal sealed class AssFrame:IDisposable
{
    internal readonly byte[] Pixels;internal readonly int Width,Height;internal readonly double Time;bool returned;
    internal AssFrame(int width,int height,double time){Width=width;Height=height;Time=time;Pixels=ArrayPool<byte>.Shared.Rent(checked(width*height*4));}
    public void Dispose(){if(returned)return;returned=true;ArrayPool<byte>.Shared.Return(Pixels);}
}
internal sealed class AssFrameBuffer
{
    readonly ConcurrentQueue<AssFrame> frames=new();readonly SemaphoreSlim slots=new(3,3);
    internal int Count=>frames.Count;
    internal async Task AddAsync(AssFrame frame,CancellationToken token)
    {
        try{await slots.WaitAsync(token);frames.Enqueue(frame);}catch{frame.Dispose();throw;}
    }
    internal AssFrame? Take(double time)
    {
        AssFrame? latest=null;
        while(frames.TryPeek(out var f)&&f.Time<=time+.002&&frames.TryDequeue(out f)){slots.Release();latest?.Dispose();latest=f;}
        return latest;
    }
    internal void Clear(){while(frames.TryDequeue(out var f)){slots.Release();f.Dispose();}}
}
internal sealed class AssPreviewWorkspace:IDisposable
{
    internal static int ProcessLaunches;
    internal readonly string DirectoryPath;
    internal string SnapshotPath=>Path.Combine(DirectoryPath,"snapshot.ass");
    readonly ConcurrentQueue<string> diagnostics=new();int diagnosticChars;
    internal string Diagnostics=>string.Join('\n',diagnostics);
    internal int? LastProcessId {get;private set;}
    internal int? LastExitCode {get;private set;}
    internal AssPreviewWorkspace(string ass,string fonts,CancellationToken token)
    {
        if(Encoding.UTF8.GetByteCount(ass)>64*1024*1024)throw new FormatException("字幕快照超过 64 MB。");
        DirectoryPath=Path.Combine(Path.GetTempPath(),"Frameflow-ass-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(DirectoryPath);
        try{File.WriteAllText(SnapshotPath,ass,new UTF8Encoding(false));Directory.CreateDirectory(Path.Combine(DirectoryPath,"fonts"));AssRendering.CopyFonts(fonts,Path.Combine(DirectoryPath,"fonts"),token,Log);}catch{Dispose();throw;}
    }
    void Log(string line){if(Interlocked.Add(ref diagnosticChars,line.Length)<100_000)diagnostics.Enqueue(line);}
    internal async Task RenderAsync(int width,int height,double start,bool continuous,Func<AssFrame,Task> receive,CancellationToken token,int fps=30,int maxWidth=1280)
    {
        if(width<16||height<16||width>7680||height>4320||!double.IsFinite(start)||start<0||start>86400||fps<1||fps>60||maxWidth<16||maxWidth>1920)throw new ArgumentException("预览尺寸 / 时间无效（最高 8K 宽、4K 高）。");
        double scale=Math.Min(1,Math.Min((double)maxWidth/width,720d/height));int w=Math.Max(1,(int)Math.Round(width*scale)),h=Math.Max(1,(int)Math.Round(height*scale));
        string filter=$"settb=1/1000,setpts=PTS+{AssRendering.Number(start*1000)},"+AssRendering.Filter(true)+$",scale={w}:{h}:flags=bilinear,format=bgra";
        var args=new List<string>{"-hide_banner","-nostdin","-nostats","-loglevel","info","-filter_threads","2","-f","lavfi","-i",$"color=c=black@0:s={width}x{height}:r={fps},format=bgra","-vf",filter,"-an","-sn","-threads","1","-fps_mode","passthrough","-pix_fmt","bgra"};
        if(!continuous)args.AddRange(["-frames:v","1"]);args.AddRange(["-f","rawvideo","pipe:1"]);
        var info=new ProcessStartInfo(BurnInEncoder.Tool("ffmpeg")){WorkingDirectory=DirectoryPath,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8};foreach(string a in args)info.ArgumentList.Add(a);
        Log("COMMAND: "+System.Text.Json.JsonSerializer.Serialize(new{info.FileName,args,info.WorkingDirectory}));token.ThrowIfCancellationRequested();
        using var process=Process.Start(info)??throw new IOException("无法启动 libass 预览进程。");Interlocked.Increment(ref ProcessLaunches);LastProcessId=process.Id;Log("command_execution PID="+process.Id);
        using var job=new EncoderJob(process);try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}
        using var cancelRegistration=token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        async Task Errors(){while(await process.StandardError.ReadLineAsync() is string line)Log(line);}
        var errors=Errors();long index=0;
        try
        {
            while(true)
            {
                token.ThrowIfCancellationRequested();var frame=new AssFrame(w,h,start+(double)index/fps);bool handed=false;
                try
                {
                    int read=0,length=w*h*4;using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    while(read<length){int count=await process.StandardOutput.BaseStream.ReadAsync(frame.Pixels.AsMemory(read,length-read),deadline.Token);if(count==0)break;read+=count;}
                    if(read==0)break;if(read!=length)throw new IOException("libass 返回了不完整的预览帧。");
                    handed=true;await receive(frame);index++;
                    if(!continuous)break;
                }
                finally{if(!handed)frame.Dispose();}
            }
            await process.WaitForExitAsync(token);await errors;LastExitCode=process.ExitCode;Log("EXIT_STATUS: "+process.ExitCode);
            token.ThrowIfCancellationRequested();if(process.ExitCode!=0||index==0)throw new IOException("libass 预览失败：\n"+Diagnostics[^Math.Min(1500,Diagnostics.Length)..]);
        }
        finally
        {
            if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await errors;LastExitCode=process.ExitCode;Log("EXIT_STATUS: "+process.ExitCode);
        }
    }
    internal async Task ReferenceFrameAsync(string video,double start,string output,CancellationToken token)
    {
        var args=new[]{"-hide_banner","-nostdin","-n","-filter_threads","2","-protocol_whitelist","file,pipe","-ss",AssRendering.Number(start),"-i",Path.GetFullPath(video),"-map","0:V:0","-vf",$"setpts=PTS+{AssRendering.Number(start)}/TB,"+AssRendering.Filter()+",pad=ceil(iw/2)*2:ceil(ih/2)*2","-frames:v","1","-threads","1",output};
        var r=await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),args,DirectoryPath,token,Log,null,40);if(r.Exit!=0)throw new IOException("对照基准帧失败："+r.Error);
    }
    internal static async Task DecodeFrameAsync(string video,string output,CancellationToken token)
    {
        var r=await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),["-hide_banner","-nostdin","-n","-protocol_whitelist","file,pipe","-i",Path.GetFullPath(video),"-map","0:V:0","-frames:v","1","-threads","1",output],null,token,null,null,30);if(r.Exit!=0)throw new IOException("成片帧解码失败："+r.Error);
    }
    public void Dispose()
    {
        // Only this freshly-created GUID-owned workspace can ever be deleted.
        string full=Path.GetFullPath(DirectoryPath),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(full).Length!="Frameflow-ass-".Length+32||!Path.GetFileName(full).StartsWith("Frameflow-ass-",StringComparison.Ordinal))return;
        try{if(Directory.Exists(full))Directory.Delete(full,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
