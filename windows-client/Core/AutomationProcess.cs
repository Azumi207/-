using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Frameflow.Core;
internal static class AutomationProcess
{
    [StructLayout(LayoutKind.Sequential)]struct BasicLimits{public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]struct Counters{public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]struct Limits{public BasicLimits Basic;public Counters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern SafeFileHandle CreateJobObject(nint attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(SafeFileHandle job,int type,ref Limits data,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(SafeFileHandle job,nint process);
    internal static async Task<AutomationResult> RunAsync(AutomationRequest request,CancellationToken token)
    {
        string folder=Path.Combine(Path.GetTempPath(),"Frameflow-macro-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string input=Path.Combine(folder,"request.json"),output=Path.Combine(folder,"result.json");
        try
        {
            await File.WriteAllTextAsync(input,JsonSerializer.Serialize(request),token);using var job=CreateJobObject(0,null);var limits=new Limits{Basic=new BasicLimits{Flags=0x2000|0x100|0x8,ActiveProcesses=1},ProcessMemory=(UIntPtr)(512*1024*1024)};
            if(job.IsInvalid||!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<Limits>()))throw new InvalidOperationException("无法建立 Lua 工作进程资源限制，未运行脚本。");
            var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"Frameflow.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in new[]{"--automation-worker",input,output})info.ArgumentList.Add(arg);using var process=Process.Start(info)??throw new InvalidOperationException("无法启动 Lua 工作进程。");
            try
            {
                if(!AssignProcessToJobObject(job,process.Handle))throw new InvalidOperationException("无法隔离 Lua 工作进程，未执行脚本。");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.StandardInput.WriteLineAsync("RUN");process.StandardInput.Close();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();token.ThrowIfCancellationRequested();throw new InvalidOperationException("Lua 工作进程超过 10 秒，已停止；字幕未修改。");}
                string error=await stderr;_=await stdout;if(process.ExitCode!=0)throw new InvalidOperationException("Lua 执行失败："+error[..Math.Min(3000,error.Length)]);if(!File.Exists(output)||new FileInfo(output).Length>32_000_000)throw new InvalidOperationException("Lua 返回数据缺失或过大。");return JsonSerializer.Deserialize<AutomationResult>(await File.ReadAllTextAsync(output,token))??throw new InvalidOperationException("Lua 返回数据无效。");
            }
            finally{if(!process.HasExited){process.Kill(true);process.WaitForExit();}}
        }
        finally{foreach(string file in new[]{input,output})try{File.Delete(file);}catch(IOException){}try{Directory.Delete(folder);}catch(IOException){}}
    }
    internal static void Worker(string input,string output)
    {
        if(Console.ReadLine()!="RUN")throw new InvalidOperationException("工作进程未获主程序许可。");if(new FileInfo(input).Length>32_000_000)throw new InvalidOperationException("输入快照过大。");var request=JsonSerializer.Deserialize<AutomationRequest>(File.ReadAllText(input))??throw new FormatException("无效请求。");var result=AutomationEngine.Execute(request);File.WriteAllText(output,JsonSerializer.Serialize(result));
    }
}
