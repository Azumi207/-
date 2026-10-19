using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Frameflow.Core;

namespace Frameflow.Media;
internal sealed record EncodeRequest(string Input,string Output,string Ass,int Quality=0,bool Audio=true,string FontDirectory="",double? PreviewStart=null,EncodingAcceleration Acceleration=EncodingAcceleration.Auto);
internal sealed record VideoFacts(int Width,int Height,double Duration,bool HasAudio,bool Hdr,string Codec,int SubtitleStreams);
internal sealed record EncodeProgress(double Fraction,string Text,string? EncoderStatus=null);
internal sealed record EncodeResult(string Output,string LogPath,VideoFacts Video,string FontLog,EncoderProfile Encoder,bool FellBack,string AccelerationNotice);
internal static class BurnInEncoder
{
    internal static string EngineDirectory=>Environment.GetEnvironmentVariable("FRAMEFLOW_FFMPEG_DIR") is string v&&v.Length>0?Path.GetFullPath(v):Path.Combine(AppContext.BaseDirectory,"Engine");
    internal static string Tool(string name){string path=Path.Combine(EngineDirectory,name+".exe");if(!File.Exists(path))throw new FileNotFoundException("压制引擎缺失，请使用含 Engine 文件夹的完整发行包。",path);return path;}
    internal static string SuggestOutput(string input,bool preview=false){string folder=Path.GetDirectoryName(Path.GetFullPath(input))!;string stem=Path.GetFileNameWithoutExtension(input)+(preview?"_字幕预览":"_字幕");for(int i=0;i<10000;i++){string p=Path.Combine(folder,stem+(i==0?"":"_"+i)+".mp4");if(!File.Exists(p)&&!Directory.Exists(p))return p;}throw new IOException("请另选输出文件名。");}
    internal static void Validate(EncodeRequest r)
    {
        if(!File.Exists(r.Input))throw new FileNotFoundException("请选择存在的本地视频。",r.Input);
        string input=Path.GetFullPath(r.Input),output=Path.GetFullPath(r.Output);
        if(input.Equals(output,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("不能覆盖原视频，请另选输出路径。");
        if(Path.GetExtension(output).ToLowerInvariant()!=".mp4")throw new FormatException("本版输出格式为 MP4。");
        if(File.Exists(output)||Directory.Exists(output))throw new IOException("输出已存在。为保护文件，请更换名称（不会覆盖）。");
        if(!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException("输出文件夹不存在。");
        if(r.Quality is <0 or >2||r.PreviewStart is double time&&(!double.IsFinite(time)||time<0))throw new FormatException("无效压制参数。");
        if(!Enum.IsDefined(r.Acceleration))throw new FormatException("无效加速模式。");
        var doc=SubtitleDocument.Parse(r.Ass);if(doc.Warnings.Count>0)throw new FormatException("字幕有解析警告，请先修复，避免漏压行。");
        if(!doc.Rows.Any(x=>!x.Comment&&!string.IsNullOrWhiteSpace(x.Text)))throw new InvalidOperationException("没有可压制的字幕正文。");
        if(doc.Rows.Any(x=>!x.Comment&&(x.End<=x.Start||x.End>86400)))throw new FormatException("字幕存在无效时间，请先修复。");
        if(Encoding.UTF8.GetByteCount(r.Ass)>64*1024*1024)throw new FormatException("字幕快照超过 64 MB。");
        if(r.FontDirectory.Length>0&&!Directory.Exists(r.FontDirectory))throw new DirectoryNotFoundException("附加字体目录不存在。");
    }
    internal static IEnumerable<string> ReferencedFonts(string ass)
    {
        var doc=SubtitleDocument.Parse(ass);return doc.Styles.Select(s=>s.Get("Fontname")).Concat(Regex.Matches(ass,@"\\fn([^\\}\r\n]+)").Select(m=>m.Groups[1].Value)).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    internal static VideoFacts ParseProbe(string json)
    {
        using var d=JsonDocument.Parse(json);var streams=d.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var p)?p.ToString():"";
        var video=streams.FirstOrDefault(x=>Text(x,"codec_type")=="video"&&(!x.TryGetProperty("disposition",out var disp)||!disp.TryGetProperty("attached_pic",out var pic)||pic.GetInt32()==0));
        if(video.ValueKind==JsonValueKind.Undefined)throw new FormatException("文件没有可压制的视频画面。");
        int w=video.GetProperty("width").GetInt32(),h=video.GetProperty("height").GetInt32();
        string durationText=Text(video,"duration");if(!double.TryParse(durationText,NumberStyles.Float,CultureInfo.InvariantCulture,out double seconds)&&d.RootElement.TryGetProperty("format",out var format))double.TryParse(Text(format,"duration"),NumberStyles.Float,CultureInfo.InvariantCulture,out seconds);
        if(w<1||h<1||w>16384||h>16384||!double.IsFinite(seconds)||seconds<=0||seconds>86400)throw new FormatException("视频尺寸或时长无法可靠识别（限 24 小时）。");
        bool hdr=Text(video,"color_transfer") is "smpte2084" or "arib-std-b67"||video.ToString().Contains("DOVI",StringComparison.OrdinalIgnoreCase);
        return new(w,h,seconds,streams.Any(x=>Text(x,"codec_type")=="audio"),hdr,Text(video,"codec_name"),streams.Count(x=>Text(x,"codec_type")=="subtitle"));
    }
    internal static async Task<VideoFacts> ProbeAsync(string path,CancellationToken token,Action<string>? log=null)
    {
        var result=await RunAsync(Tool("ffprobe"),["-v","error","-protocol_whitelist","file,pipe","-show_streams","-show_format","-of","json",Path.GetFullPath(path)],null,token,log,null,20);
        if(result.Exit!=0)throw new InvalidOperationException("视频检查失败："+result.Error);return ParseProbe(result.Output);
    }
    internal static async Task CheckEngineAsync(CancellationToken token)
    {
        var filters=await RunAsync(Tool("ffmpeg"),["-hide_banner","-filters"],null,token,null,null,15);
        var encoders=await RunAsync(Tool("ffmpeg"),["-hide_banner","-encoders"],null,token,null,null,15);
        if(filters.Exit!=0||encoders.Exit!=0||!Regex.IsMatch(filters.Output,@"\bass\s+V->V")||!encoders.Output.Contains("libx264")||!Regex.IsMatch(encoders.Output,@"\baac\s"))throw new InvalidOperationException("压制引擎需要 libass、libx264 和 AAC 支持。");
    }
    internal static List<string> Arguments(EncodeRequest r,string temporaryOutput,EncoderProfile? encoder=null)
    {
        var args=new List<string>{"-hide_banner","-nostdin","-n","-xerror","-loglevel","info","-stats_period","0.25","-progress","pipe:1","-nostats","-filter_threads","2","-protocol_whitelist","file,pipe"};
        string S(double v)=>v.ToString("0.######",CultureInfo.InvariantCulture);double start=r.PreviewStart??0;
        if(r.PreviewStart!=null)args.AddRange(["-ss",S(start)]);args.AddRange(["-i",Path.GetFullPath(r.Input),"-map","0:V:0"]);
        if(r.Audio)args.AddRange(["-map","0:a:0?"]);else args.Add("-an");
        string filter=(r.PreviewStart!=null?$"setpts=PTS+{S(start)}/TB,":"")+"ass=filename=snapshot.ass:fontsdir=fonts"+(r.PreviewStart!=null?$",setpts=PTS-{S(start)}/TB":"")+",pad=ceil(iw/2)*2:ceil(ih/2)*2";
        args.AddRange(["-vf",filter]);args.AddRange(HardwareEncoding.VideoArguments(encoder??HardwareEncoding.Cpu,r.Quality));args.AddRange(["-threads",Math.Clamp(Environment.ProcessorCount/2,1,4).ToString(),"-fps_mode","vfr"]);
        if(r.Audio)args.AddRange(["-c:a","aac","-b:a","192k"]);
        args.AddRange(["-sn","-dn","-map_metadata","0","-map_chapters",r.PreviewStart==null?"0":"-1","-movflags","+faststart"]);
        if(r.PreviewStart!=null)args.AddRange(["-t","10"]);args.AddRange(["-f","mp4",temporaryOutput]);return args;
    }
    internal static async Task<EncodeResult> EncodeAsync(EncodeRequest r,IProgress<EncodeProgress>? progress,CancellationToken token)
    {
        Validate(r);token.ThrowIfCancellationRequested();await CheckEngineAsync(token);var facts=await ProbeAsync(r.Input,token);
        if(facts.Hdr)throw new InvalidOperationException("当前简易压制仅支持 SDR。检测到 HDR / Dolby Vision；为避免偏色，尚不执行自动转换。");
        if(r.PreviewStart>=facts.Duration)throw new FormatException("预览起点超出视频时长。");
        if(!SubtitleDocument.Parse(r.Ass).Rows.Any(x=>!x.Comment&&x.Start<facts.Duration&&x.End>0))throw new FormatException("字幕时间与视频没有交集，请先检查时间轴。");
        string output=Path.GetFullPath(r.Output),id=Guid.NewGuid().ToString("N"),folder=Path.GetDirectoryName(output)!,work=Path.Combine(folder,".frameflow-encode-"+id),temp=Path.Combine(work,"encoded.mp4"),fontDir=Path.Combine(work,"fonts"),logPath=output+".encode-"+id+".log.txt";
        var owned=new List<string>();object logLock=new();StreamWriter? writer=null;int logChars=0;bool truncated=false;
        void Log(string line){lock(logLock){if(writer==null)return;if(logChars<4_000_000){writer.WriteLine(line);writer.Flush();logChars+=line.Length;}else if(!truncated){writer.WriteLine("[log truncated at 4 MB characters]");truncated=true;}}}
        try
        {
            Directory.CreateDirectory(work);Directory.CreateDirectory(fontDir);writer=new StreamWriter(new FileStream(logPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
            Log("Frameflow burn-in / source read-only / no overwrite / "+DateTimeOffset.Now);Log("INPUT: "+Path.GetFullPath(r.Input));Log("OUTPUT: "+output);Log("ASS_SHA256: "+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(r.Ass))));Log("FACTS: "+JsonSerializer.Serialize(facts));Log("Requested fonts: "+string.Join(", ",ReferencedFonts(r.Ass)));
            await File.WriteAllTextAsync(Path.Combine(work,"snapshot.ass"),r.Ass,new UTF8Encoding(false),token);owned.Add(Path.Combine(work,"snapshot.ass"));
            if(r.FontDirectory.Length>0){var fonts=Directory.GetFiles(r.FontDirectory).Where(f=>new[]{".ttf",".otf",".ttc",".otc"}.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();if(fonts.Length>200||fonts.Sum(f=>new FileInfo(f).Length)>300_000_000)throw new IOException("字体目录超过 200 个文件 / 300 MB，请选择更小的专用目录。");for(int i=0;i<fonts.Length;i++){token.ThrowIfCancellationRequested();string to=Path.Combine(fontDir,i.ToString("000")+Path.GetExtension(fonts[i]));File.Copy(fonts[i],to,false);owned.Add(to);Log("FONT: "+fonts[i]);}}
            double length=Math.Min(r.PreviewStart!=null?10:facts.Duration,facts.Duration-(r.PreviewStart??0));
            var selection=r.Acceleration==EncodingAcceleration.CpuOnly
                ?new EncoderSelection(HardwareEncoding.Cpu,[],"已选择仅 CPU；不检测或启动显卡编码器。")
                :await HardwareEncoding.DetectAsync(work,r.Quality,token,Log,s=>progress?.Report(new(0,s,s))).ConfigureAwait(false);
            string encoderStatus=selection.Notice;Log("SELECTED: "+selection.Profile.Codec+" / "+selection.Notice);
            async Task<(ProcessResult Process,VideoFacts Video)> Attempt(EncoderProfile profile)
            {
                progress?.Report(new(0,"正在压制 · "+profile.Label,encoderStatus));
                var args=Arguments(r,temp,profile);if(r.PreviewStart==null)args.InsertRange(args.Count-1,["-t",facts.Duration.ToString("0.######",CultureInfo.InvariantCulture)]);
                var result=await RunAsync(Tool("ffmpeg"),args,work,token,Log,line=>{if(line.StartsWith("out_time_us=")&&double.TryParse(line[12..],NumberStyles.Float,CultureInfo.InvariantCulture,out double us))progress?.Report(new(Math.Clamp(us/1_000_000/length,0,.99),$"已处理 {Math.Min(length,us/1_000_000):0.0} / {length:0.0} 秒 · {profile.Label}",encoderStatus));}).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();if(result.Exit!=0)throw new EncoderAttemptFailure("FFmpeg "+profile.Label+" 压制失败（退出码 "+result.Exit+"）。\n"+result.Error[^Math.Min(1800,result.Error.Length)..]);
                progress?.Report(new(.99,"正在检查输出文件…",encoderStatus));
                VideoFacts encoded;
                try{encoded=await ProbeAsync(temp,token,Log).ConfigureAwait(false);}
                catch(Exception e) when(e is InvalidOperationException or FormatException or JsonException or TimeoutException){throw new EncoderAttemptFailure("输出无法通过检查。",e);}
                if(encoded.Codec!="h264"||encoded.SubtitleStreams!=0||encoded.Duration<Math.Min(.1,length)||Math.Abs(encoded.Duration-length)>Math.Max(.6,length*.005)||r.Audio&&facts.HasAudio&&!encoded.HasAudio)throw new EncoderAttemptFailure("输出检查未通过（编码、时长或音轨异常）；未生成正式文件。");
                return(result,encoded);
            }
            var run=await HardwareEncoding.RunWithFallbackAsync(selection.Profile,Attempt,()=>File.Delete(temp),token,Log,s=>{encoderStatus=s;progress?.Report(new(0,s,s));}).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();File.Move(temp,output,false);
            try{Log("SUCCESS: output verified and atomically committed. ENCODER="+run.Profile.Codec+" FALLBACK="+run.FellBack);}catch(IOException){}
            progress?.Report(new(1,"压制完成 · "+run.Profile.Label,encoderStatus));string fontLog=string.Join('\n',run.Result.Process.Error.Split('\n').Where(s=>s.Contains("fontselect",StringComparison.OrdinalIgnoreCase)||s.Contains("Glyph",StringComparison.OrdinalIgnoreCase)));return new(output,logPath,run.Result.Video,fontLog,run.Profile,run.FellBack,encoderStatus);
        }
        catch(Exception e){try{Log((e is OperationCanceledException?"CANCELED: ":"FAILED: ")+e.Message);}catch(IOException){}catch(UnauthorizedAccessException){}throw new EncodeFailure(e is OperationCanceledException,"压制"+(e is OperationCanceledException?"已取消，未生成正式输出。":"失败："+e.Message)+(File.Exists(logPath)?"\n日志："+logPath:""),logPath,e);}
        finally
        {
            try{writer?.Dispose();}catch(IOException){}
            // Only our fixed temporary names and copied font files are removed, never a source directory.
            foreach(string f in owned.Append(Path.Combine(work,"snapshot.ass")).Append(temp).Distinct())try{File.Delete(f);}catch(IOException){}catch(UnauthorizedAccessException){}
            try{Directory.Delete(fontDir);Directory.Delete(work);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    internal sealed record ProcessResult(int Exit,string Output,string Error);
    internal static async Task<ProcessResult> RunAsync(string executable,IEnumerable<string> arguments,string? cwd,CancellationToken token,Action<string>? log=null,Action<string>? outputLine=null,int? timeoutSeconds=null)
    {
        token.ThrowIfCancellationRequested();var args=arguments.ToArray();log?.Invoke("COMMAND: "+JsonSerializer.Serialize(new{executable,args,cwd}));
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};if(cwd!=null)start.WorkingDirectory=cwd;foreach(string arg in args)start.ArgumentList.Add(arg);
        using var child=Process.Start(start)??throw new InvalidOperationException("无法启动 FFmpeg。");log?.Invoke("command_execution PID="+child.Id);using var lifetime=new EncoderJob(child);using var linked=CancellationTokenSource.CreateLinkedTokenSource(token);if(timeoutSeconds.HasValue)linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));
        async Task<string> Read(StreamReader reader,bool stdout){var text=new StringBuilder();while(await reader.ReadLineAsync() is string line){log?.Invoke((stdout?"STDOUT: ":"STDERR: ")+line);if(stdout)outputLine?.Invoke(line);text.AppendLine(line);if(text.Length>2_000_000)text.Remove(0,text.Length-1_000_000);}return text.ToString();}
        var stdout=Read(child.StandardOutput,true);var stderr=Read(child.StandardError,false);
        void StopFailedReader(Task task){if(task.IsFaulted)try{if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}}
        _=stdout.ContinueWith(StopFailedReader,TaskContinuationOptions.OnlyOnFaulted);_=stderr.ContinueWith(StopFailedReader,TaskContinuationOptions.OnlyOnFaulted);
        try{await child.WaitForExitAsync(linked.Token);string o=await stdout,e=await stderr;log?.Invoke("EXIT_STATUS: "+child.ExitCode);return new(child.ExitCode,o,e);}
        catch(OperationCanceledException){if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();await Task.WhenAll(stdout,stderr);log?.Invoke("EXIT_STATUS: "+child.ExitCode+" (canceled/timeout)");token.ThrowIfCancellationRequested();throw new TimeoutException("媒体检查超时。");}
        finally{if(!child.HasExited){child.Kill(true);child.WaitForExit();}}
    }
}
internal sealed class EncodeFailure(bool canceled,string message,string logPath,Exception inner):Exception(message,inner){internal bool Canceled{get;}=canceled;internal string LogPath{get;}=logPath;}
