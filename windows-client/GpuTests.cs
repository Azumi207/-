using System.Text.Json;
using Frameflow.Media;

namespace Frameflow;
internal static class GpuTests
{
    internal static void Run(string dir, Action<bool,string> check) => Task.Run(() => RunAsync(dir,check)).GetAwaiter().GetResult();
    static async Task RunAsync(string dir, Action<bool,string> check)
    {
        string input=EncodingTests.FixtureVideo(dir),ass=EncodingTests.StyledAss();
        string folder=Path.Combine(dir,"gpu-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string sourceHash=EncodingTests.Hash(input);var r=new EncodeRequest(input,Path.Combine(folder,"auto.mp4"),ass);
        check(r.Acceleration==EncodingAcceleration.Auto,"GPU mode defaults to automatic without manual configuration");
        bool invalid=false;try{BurnInEncoder.Validate(r with{Acceleration=(EncodingAcceleration)9});}catch(FormatException){invalid=true;}check(invalid,"unknown acceleration mode is rejected");
        check(HardwareEncoding.Candidates.Select(p=>p.Codec).SequenceEqual(new[]{"h264_nvenc","h264_qsv","h264_amf"}),"three supported hardware backends have explicit deterministic priority");
        var profiles=HardwareEncoding.Candidates;
        foreach(var p in profiles)
        {
            var a=HardwareEncoding.VideoArguments(p,0);
            check(a.Contains(p.Codec)&&a.Contains("nv12")&&!a.Contains("-crf"),p.Codec+" uses native rate-control options and upload-compatible NV12");
        }
        check(HardwareEncoding.VideoArguments(profiles[0],1).Contains("18")&&HardwareEncoding.VideoArguments(profiles[1],2).Contains("24")&&HardwareEncoding.VideoArguments(profiles[2],0).Contains("20"),"human quality presets map explicitly to each backend");
        check(HardwareEncoding.VideoArguments(HardwareEncoding.Cpu,0).Contains("-crf"),"CPU quality path retains x264 CRF");
        check(HardwareEncoding.Select([]).Profile==HardwareEncoding.Cpu,"empty or unavailable capability set selects CPU rather than invented GPU support");
        var selected=HardwareEncoding.Select(profiles.Reverse().Select(p=>new EncoderAttempt(p.Codec,true,0,"synthetic selection policy only")).ToArray());
        check(selected.Profile==profiles[0],"selection priority does not depend on detection result ordering");
        var args=BurnInEncoder.Arguments(r,"out.mp4",profiles[0]);var cpuArgs=BurnInEncoder.Arguments(r,"out.mp4",HardwareEncoding.Cpu);
        check(args[args.IndexOf("-vf")+1]==cpuArgs[cpuArgs.IndexOf("-vf")+1]&&!args.Contains("-hwaccel"),"hardware route uses identical CPU libass filter and does not claim GPU subtitle rendering or decoding");
        var log=new List<string>();void Log(string s)=>log.Add(s);
        var detection=await HardwareEncoding.DetectAsync(folder,0,CancellationToken.None,Log);
        check(detection.Attempts.Count==3,"automatic detection actually tests all three backends");
        foreach(var a in detection.Attempts)
            check(a.Available?a.ExitCode==0:a.ExitCode!=0&&a.Detail.Length>0,"actual backend result recorded: "+a.Codec+" available="+a.Available+" exit="+a.ExitCode);
        check(!Directory.GetFiles(folder,"probe-*.mp4").Any(),"probe success and failure leave no probe MP4 artifacts");
        check(log.Any(l=>l.Contains("command_execution PID="))&&log.Any(l=>l.Contains("EXIT_STATUS:")),"GPU capability is based on real child execution not FFmpeg encoder listings");
        var progress=new List<EncodeProgress>();var result=await BurnInEncoder.EncodeAsync(r,new EncodeTestProgress(progress.Add),CancellationToken.None);
        check(result.Video.Codec=="h264"&&result.Video.HasAudio&&result.Video.SubtitleStreams==0,"automatic hardware-or-CPU path creates actual hard-subtitle H264 and original audio");
        check(result.Encoder.Hardware||!detection.Profile.Hardware||result.FellBack,"actual encoder selection matches detection or explicit CPU recovery");
        check(progress.Any(p=>p.EncoderStatus?.Contains("试编码") == true)&&progress.Last().Fraction==1,"progress exposes actual probing and completes only after verification");
        await EncodingTests.FrameAsync(result.Output,Path.Combine(folder,"auto-green.png"),2.5);
        check(EncodingTests.Colors(Path.Combine(folder,"auto-green.png"),20,120).Green>300&&EncodingTests.Colors(Path.Combine(folder,"auto-green.png"),240,360).Red>300,"actual automatic output preserves positioned green and styled red subtitle pixels");
        await EncodingTests.FrameAsync(result.Output,Path.Combine(folder,"auto-animation.png"),7.6);
        check(EncodingTests.Colors(Path.Combine(folder,"auto-animation.png"),100,230).Blue>300&&EncodingTests.Colors(Path.Combine(folder,"auto-animation.png"),20,100).Yellow>2000,"actual automatic output preserves animated text and ASS vector drawing");
        var cpu=await BurnInEncoder.EncodeAsync(r with{Output=Path.Combine(folder,"cpu-preview.mp4"),Acceleration=EncodingAcceleration.CpuOnly,PreviewStart=5},null,CancellationToken.None);
        check(cpu.Encoder==HardwareEncoding.Cpu&&!cpu.FellBack&&!File.ReadAllText(cpu.LogPath).Contains("GPU DETECTION:"),"CPU-only option bypasses all GPU detection and still produces real preview");
        using var fixtures=JsonDocument.Parse(File.ReadAllText(Path.Combine(EncodingTests.FixtureFolder(dir),"results.json")));
        string vfrSource=fixtures.RootElement.GetProperty("vfrSource").GetString()!,oddSource=fixtures.RootElement.GetProperty("oddSource").GetString()!;
        var vfrResult=await BurnInEncoder.EncodeAsync(r with{Input=vfrSource,Output=Path.Combine(folder,"auto-vfr.mp4")},null,CancellationToken.None);
        async Task<double[]> Times(string file){var p=await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffprobe"),["-v","error","-select_streams","v:0","-show_entries","frame=best_effort_timestamp_time","-of","json",file],null,CancellationToken.None,Log);if(p.Exit!=0)throw new Exception(p.Error);using var d=JsonDocument.Parse(p.Output);return d.RootElement.GetProperty("frames").EnumerateArray().Select(f=>double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!,System.Globalization.CultureInfo.InvariantCulture)).ToArray();}
        var sourceTimes=await Times(vfrSource);var encodedTimes=await Times(vfrResult.Output);
        check(sourceTimes.Length==20&&encodedTimes.Length==20&&sourceTimes.Zip(encodedTimes,(a,b)=>Math.Abs(a-b)<.003).All(v=>v),"automatic route retains actual VFR frame timestamps without duplicates or drops ("+vfrResult.Encoder.Codec+")");
        var oddResult=await BurnInEncoder.EncodeAsync(r with{Input=oddSource,Output=Path.Combine(folder,"auto-odd.mp4")},null,CancellationToken.None);
        check(oddResult.Video.Width==162&&oddResult.Video.Height==92&&!oddResult.Video.HasAudio,"automatic route handles odd sizes and absent audio with verified output ("+oddResult.Encoder.Codec+")");
        var cancelLog=new List<string>();bool detectionCanceled=false;using(var stop=new CancellationTokenSource())try{await HardwareEncoding.DetectAsync(folder,0,stop.Token,s=>{cancelLog.Add(s);if(s.StartsWith("command_execution PID="))stop.Cancel();});}catch(OperationCanceledException){detectionCanceled=true;}
        check(detectionCanceled&&cancelLog.Count(s=>s.StartsWith("command_execution PID="))==1&&!Directory.GetFiles(folder,"probe-*.mp4").Any(),"cancel during actual GPU detection stops after first child and cleans probe output");
        log.AddRange(cancelLog);
        // Exercise the same recovery transaction using an actual failed child, not a fabricated exit code.
        string fallbackFile=Path.Combine(folder,"fallback.mp4");File.WriteAllText(Path.Combine(folder,"snapshot.ass"),ass);Directory.CreateDirectory(Path.Combine(folder,"fonts"));
        int attempts=0,resets=0;var notices=new List<string>();
        var recovered=await HardwareEncoding.RunWithFallbackAsync(profiles[0],async p=>{
            attempts++;
            if(p.Hardware){File.WriteAllText(fallbackFile,"owned partial output fixture");var failure=await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),["-not-a-real-option"],folder,CancellationToken.None,Log);if(failure.Exit!=0)throw new EncoderAttemptFailure("fault injection: actual FFmpeg exit "+failure.Exit);throw new Exception("Expected actual command failure");}
            var ok=await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),BurnInEncoder.Arguments(r with{PreviewStart=5},fallbackFile,p),folder,CancellationToken.None,Log);if(ok.Exit!=0)throw new EncoderAttemptFailure(ok.Error);return await BurnInEncoder.ProbeAsync(fallbackFile,CancellationToken.None,Log);
        },()=>{resets++;File.Delete(fallbackFile);},CancellationToken.None,Log,notices.Add);
        check(attempts==2&&resets==1&&recovered.FellBack&&recovered.Profile==HardwareEncoding.Cpu&&recovered.Result.Codec=="h264","real failed child triggers one cleanup and one successful CPU retry in shared encode transaction");
        check(notices.Single().Contains("从头重试")&&File.ReadAllBytes(fallbackFile).Length>1000,"fallback is visible and replaces only its own partial temporary output");
        attempts=0;bool doubleFailed=false;try{await HardwareEncoding.RunWithFallbackAsync<int>(profiles[0],p=>{attempts++;throw new EncoderAttemptFailure("injected unavailable encoder");},()=>{},CancellationToken.None,Log);}catch(EncoderAttemptFailure){doubleFailed=true;}
        check(doubleFailed&&attempts==2,"CPU failure after hardware failure is surfaced without retry loop");
        attempts=0;bool canceled=false;using(var cancel=new CancellationTokenSource(150))try{await HardwareEncoding.RunWithFallbackAsync(profiles[0],async p=>{attempts++;return await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),["-hide_banner","-nostdin","-re","-f","lavfi","-i","testsrc2=size=128x128:rate=24:duration=5","-f","null","-"],folder,cancel.Token,Log);},()=>{throw new Exception("Cancellation must not reset/retry");},cancel.Token,Log);}catch(OperationCanceledException){canceled=true;}
        check(canceled&&attempts==1,"cancel during actual child process kills it and never starts CPU fallback");
        attempts=0;canceled=false;try{await HardwareEncoding.RunWithFallbackAsync<int>(profiles[0],p=>{attempts++;return Task.FromResult(0);},()=>{},new CancellationToken(true),Log);}catch(OperationCanceledException){canceled=true;}
        check(canceled&&attempts==0,"pre-canceled job starts no hardware or CPU attempt");
        bool timedOut=false;try{await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),["-hide_banner","-nostdin","-re","-f","lavfi","-i","testsrc2=size=128x128:rate=24:duration=5","-f","null","-"],folder,CancellationToken.None,Log,null,1);}catch(TimeoutException){timedOut=true;}
        check(timedOut,"real timed-out probe child is terminated instead of hanging detection");
        // Independently render through every actually available hardware backend; never report an absent GPU as tested.
        foreach(var profile in profiles.Where(p=>detection.Attempts.Any(a=>a.Codec==p.Codec&&a.Available)))
        {
            string direct=Path.Combine(folder,profile.Codec+".mp4");var p=await BurnInEncoder.RunAsync(BurnInEncoder.Tool("ffmpeg"),BurnInEncoder.Arguments(r with{PreviewStart=1},direct,profile),folder,CancellationToken.None,Log,null,30);
            var info=await BurnInEncoder.ProbeAsync(direct,CancellationToken.None,Log);await EncodingTests.FrameAsync(direct,Path.Combine(folder,profile.Codec+".png"),1.5);
            check(p.Exit==0&&info.Codec=="h264"&&EncodingTests.Colors(Path.Combine(folder,profile.Codec+".png"),20,120).Green>300,"actual available backend burns and decodes styled pixels: "+profile.Codec);
        }
        check(EncodingTests.Hash(input)==sourceHash&&!Directory.GetDirectories(folder,".frameflow-encode-*").Any(),"GPU paths preserve original source and clean owned encoding staging");
        File.WriteAllText(Path.Combine(folder,"gpu-processes.txt"),string.Join(Environment.NewLine,log));
        var evidence=new{folder,displayAdapters=HardwareEncoding.DisplayAdapters(),detection,actualEncoder=result.Encoder,result.FellBack,result.LogPath,output=result.Output,sourceHash,cpuOutput=cpu.Output,fallbackFile,vfrEncoder=vfrResult.Encoder,vfrFellBack=vfrResult.FellBack,oddEncoder=oddResult.Encoder,progress};
        File.WriteAllText(Path.Combine(dir,"gpu-results.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }
}
