using System.Runtime.InteropServices;

namespace Frameflow.Media;

// Windows Media Foundation Source Reader. No bundled decoder or external process.
// COM vtable slots follow mfobjects.h / mfreadwrite.h; all interfaces released in finally.
public static unsafe class AudioPeaks
{
    [DllImport("mfplat.dll",ExactSpelling=true)]static extern int MFStartup(int version,int flags);
    [DllImport("mfplat.dll",ExactSpelling=true)]static extern int MFShutdown();
    [DllImport("mfplat.dll",ExactSpelling=true)]static extern int MFCreateMediaType(out nint type);
    [DllImport("mfreadwrite.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int MFCreateSourceReaderFromURL(string url,nint attributes,out nint reader);
    const uint Audio=0xfffffffd,All=0xfffffffe;
    static readonly Guid Major=new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"),Subtype=new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),AudioType=new("73647561-0000-0010-8000-00aa00389b71"),Pcm=new("00000001-0000-0010-8000-00aa00389b71");
    static readonly Guid Bits=new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"),Rate=new("5faeeae7-0290-4c31-9e8a-c534f68d9dba"),Channels=new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    static nint Slot(nint obj,int slot)=>((nint*)(*(nint*)obj))[slot];
    static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    static void Release(nint obj){if(obj!=0)((delegate* unmanaged[Stdcall]<nint,uint>)Slot(obj,2))(obj);}
    static void SetGuid(nint obj,Guid key,Guid value)=>Check(((delegate* unmanaged[Stdcall]<nint,Guid*,Guid*,int>)Slot(obj,24))(obj,&key,&value));
    static uint GetUInt(nint obj,Guid key){uint value=0;Check(((delegate* unmanaged[Stdcall]<nint,Guid*,uint*,int>)Slot(obj,7))(obj,&key,&value));return value;}
    public sealed record Result(float[] Peaks,double Duration,int SampleRate,int Channels);
    public static Result Decode(string path,CancellationToken token,IProgress<double>? progress=null,string? wavPath=null)
    {
        Check(MFStartup(0x20070,0));nint reader=0,type=0,current=0;
        try
        {
            Check(MFCreateSourceReaderFromURL(Path.GetFullPath(path),0,out reader));
            Check(((delegate* unmanaged[Stdcall]<nint,uint,int,int>)Slot(reader,4))(reader,All,0));
            Check(((delegate* unmanaged[Stdcall]<nint,uint,int,int>)Slot(reader,4))(reader,Audio,1));
            Check(MFCreateMediaType(out type));SetGuid(type,Major,AudioType);SetGuid(type,Subtype,Pcm);
            var bitsKey=Bits;Check(((delegate* unmanaged[Stdcall]<nint,Guid*,uint,int>)Slot(type,21))(type,&bitsKey,16));
            Check(((delegate* unmanaged[Stdcall]<nint,uint,nint,nint,int>)Slot(reader,7))(reader,Audio,0,type));
            nint c=0;Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,int>)Slot(reader,6))(reader,Audio,&c));current=c;
            int rate=(int)GetUInt(current,Rate),channels=(int)GetUInt(current,Channels),bits=(int)GetUInt(current,Bits);
            if(bits!=16||rate<=0||channels<1)throw new NotSupportedException("系统未返回 16 位 PCM 音频。");
            using var wave=wavPath!=null?new PcmWaveWriter(wavPath,rate,channels):null;
            var peaks=new List<float>();double duration=0;var progressClock=System.Diagnostics.Stopwatch.StartNew();
            while(true)
            {
                token.ThrowIfCancellationRequested();uint actual=0,flags=0;long timestamp=0;nint sample=0;
                Check(((delegate* unmanaged[Stdcall]<nint,uint,uint,uint*,uint*,long*,nint*,int>)Slot(reader,9))(reader,Audio,0,&actual,&flags,&timestamp,&sample));
                if((flags&2)!=0){Release(sample);break;}
                if((flags&0x20)!=0){Release(sample);throw new NotSupportedException("音轨采样格式在途中改变，请转为 PCM WAV 后打开。");}
                if(sample==0)continue;
                nint buffer=0;bool locked=false;
                try
                {
                    nint b=0;Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)Slot(sample,41))(sample,&b));buffer=b;
                    byte* data=null;uint max=0,len=0;Check(((delegate* unmanaged[Stdcall]<nint,byte**,uint*,uint*,int>)Slot(buffer,3))(buffer,&data,&max,&len));locked=true;
                    var pcm=(short*)data;int frames=(int)len/2/channels;double start=Math.Max(0,timestamp/10000000.0);
                    wave?.Write(start,new ReadOnlySpan<byte>(data,(int)len));
                    for(int f=0;f<frames;f++)
                    {
                        int bin=(int)((start+f/(double)rate)*100);if(bin>100*60*60*24)throw new NotSupportedException("当前版本单个媒体限制 24 小时。");
                        while(peaks.Count<=bin)peaks.Add(0);float peak=0;for(int channel=0;channel<channels;channel++)peak=Math.Max(peak,Math.Abs(pcm[f*channels+channel]/32768f));if(peak>peaks[bin])peaks[bin]=peak;
                    }
                    duration=Math.Max(duration,start+frames/(double)rate);if(progressClock.ElapsedMilliseconds>=100){progress?.Report(duration);progressClock.Restart();}
                }
                finally{if(locked)((delegate* unmanaged[Stdcall]<nint,int>)Slot(buffer,4))(buffer);Release(buffer);Release(sample);}
            }
            token.ThrowIfCancellationRequested();wave?.Complete();return new(peaks.ToArray(),duration,rate,channels);
        }
        finally{Release(current);Release(type);Release(reader);MFShutdown();}
    }
}
