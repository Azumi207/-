using System.Runtime.InteropServices;
using Frameflow.Core;

namespace Frameflow.Media;
public static unsafe class VideoFrameIndex
{
    [DllImport("mfplat.dll",ExactSpelling=true)]static extern int MFStartup(int version,int flags);
    [DllImport("mfplat.dll",ExactSpelling=true)]static extern int MFShutdown();
    [DllImport("mfreadwrite.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int MFCreateSourceReaderFromURL(string url,nint attributes,out nint reader);
    [DllImport("ole32.dll")]static extern int CoInitializeEx(nint reserved,uint mode);
    [DllImport("ole32.dll")]static extern void CoUninitialize();
    static nint Slot(nint obj,int slot)=>((nint*)(*(nint*)obj))[slot];
    static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    static void Release(nint obj){if(obj!=0)((delegate* unmanaged[Stdcall]<nint,uint>)Slot(obj,2))(obj);}
    public static FrameTimeline Read(string path,CancellationToken token,IProgress<int>? progress=null)
    {
        const uint Video=0xfffffffc,All=0xfffffffe;var cleanPoint=new Guid("9cdf01d8-a0f0-43ba-b077-eaa06cbd728a");
        int initialized=CoInitializeEx(0,0);bool started=false;nint reader=0,native=0;
        try
        {
            if(initialized<0&&initialized!=unchecked((int)0x80010106))Check(initialized);
            token.ThrowIfCancellationRequested();Check(MFStartup(0x20070,0));started=true;Check(MFCreateSourceReaderFromURL(Path.GetFullPath(path),0,out reader));
            Check(((delegate* unmanaged[Stdcall]<nint,uint,int,int>)Slot(reader,4))(reader,All,0));Check(((delegate* unmanaged[Stdcall]<nint,uint,int,int>)Slot(reader,4))(reader,Video,1));
            nint type=0;Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,nint*,int>)Slot(reader,5))(reader,Video,0,&type));native=type;
            // Native compressed samples: demux only, do not compete with playback decoding.
            Check(((delegate* unmanaged[Stdcall]<nint,uint,nint,nint,int>)Slot(reader,7))(reader,Video,0,native));
            var samples=new List<(double Start,double Duration,bool Key)>();var clock=System.Diagnostics.Stopwatch.StartNew();int empty=0;
            while(true)
            {
                token.ThrowIfCancellationRequested();uint actual=0,flags=0;long timestamp=0;nint sample=0;
                int hr=((delegate* unmanaged[Stdcall]<nint,uint,uint,uint*,uint*,long*,nint*,int>)Slot(reader,9))(reader,Video,0,&actual,&flags,&timestamp,&sample);
                try
                {
                    Check(hr);if((flags&0x20)!=0)throw new InvalidOperationException("视频格式在途中变化，帧表未提交；请导入已校验的 timecodes v2。");
                    if(sample!=0)
                    {
                        long length=0;int durationHr=((delegate* unmanaged[Stdcall]<nint,long*,int>)Slot(sample,37))(sample,&length);uint key=0;
                        int keyHr=((delegate* unmanaged[Stdcall]<nint,Guid*,uint*,int>)Slot(sample,7))(sample,&cleanPoint,&key);
                        samples.Add((timestamp/10000000d,durationHr>=0?length/10000000d:0,keyHr>=0&&key!=0));empty=0;
                        if(samples.Count>2_000_000)throw new InvalidOperationException("当前帧索引最多支持 200 万帧。");
                    }
                    else if(++empty>1000&&(flags&2)==0)throw new InvalidOperationException("连续读取到空视频样本，已停止索引。");
                    if((flags&2)!=0)break;
                    if(clock.ElapsedMilliseconds>200){progress?.Report(samples.Count);clock.Restart();}
                }
                finally{Release(sample);}
            }
            token.ThrowIfCancellationRequested();var sorted=samples.OrderBy(s=>s.Start).ToArray();if(sorted.Length==0||sorted[^1].Duration<=0)throw new InvalidOperationException("视频未提供完整样本时间/末帧时长，请手动导入帧表。");
            return new(sorted.Select(s=>s.Start),sorted[^1].Start+sorted[^1].Duration,sorted.Select((s,i)=>(s,i)).Where(x=>x.s.Key).Select(x=>x.i),"Windows 媒体样本 PTS / CleanPoint（不是画面切镜检测）");
        }
        finally{Release(native);Release(reader);if(started)MFShutdown();if(initialized>=0)CoUninitialize();}
    }
}
