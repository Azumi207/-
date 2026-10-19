using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Frameflow.Media;
internal sealed class EncoderJob:IDisposable
{
    [StructLayout(LayoutKind.Sequential)]struct Basic{public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]struct Io{public ulong Read,Write,Other,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]struct Limits{public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern SafeFileHandle CreateJobObject(nint attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(SafeFileHandle job,int type,ref Limits data,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(SafeFileHandle job,nint process);
    readonly SafeFileHandle job;
    internal EncoderJob(Process process){job=CreateJobObject(0,null);var limits=new Limits{Basic=new Basic{Flags=0x2000}};if(job.IsInvalid||!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<Limits>())||!AssignProcessToJobObject(job,process.Handle)){if(!process.HasExited)process.Kill(true);job.Dispose();throw new InvalidOperationException("无法建立编码进程退出保护，已停止。");}}
    public void Dispose()=>job.Dispose();
}
