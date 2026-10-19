using System.Numerics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Core;

namespace Frameflow.Media;
internal sealed record PcmInfo(int Rate,int Channels,long Offset,long Length){public int Align=>Channels*2;public double Duration=>Length/(double)(Rate*Align);}
internal static class PcmTools
{
    internal static PcmInfo Inspect(string path)
    {
        using var b=new BinaryReader(File.OpenRead(path));if(Encoding.ASCII.GetString(b.ReadBytes(4))!="RIFF")throw new FormatException("需要 RIFF PCM WAV。");b.ReadUInt32();if(Encoding.ASCII.GetString(b.ReadBytes(4))!="WAVE")throw new FormatException("不是 WAV。");int rate=0,channels=0,align=0;long data=-1,len=0;
        while(b.BaseStream.Position+8<=b.BaseStream.Length){string tag=Encoding.ASCII.GetString(b.ReadBytes(4));uint n=b.ReadUInt32();long end=b.BaseStream.Position+n;if(end>b.BaseStream.Length)throw new FormatException("WAV 数据不完整。");if(tag=="fmt "){if(n<16||b.ReadUInt16()!=1)throw new FormatException("需要未压缩 16 位 PCM。");channels=b.ReadUInt16();rate=b.ReadInt32();b.ReadInt32();align=b.ReadUInt16();if(b.ReadUInt16()!=16)throw new FormatException("仅支持 16 位 PCM。");}if(tag=="data"){data=b.BaseStream.Position;len=n;}b.BaseStream.Position=end+(n%2);}
        if(rate<1||rate>384000||channels<1||channels>32||align!=channels*2||data<0||len%align!=0)throw new FormatException("无效 PCM 布局。");return new(rate,channels,data,len);
    }
    internal static void Generate(string path,double seconds,bool noise)
    {
        if(!double.IsFinite(seconds)||seconds<.1||seconds>3600)throw new FormatException("测试音频时长为 0.1～3600 秒。");const int rate=16000;using var wave=new PcmWaveWriter(path,rate,1);var random=new Random(417);long total=(long)Math.Round(seconds*rate);byte[] block=new byte[32000];
        for(long at=0;at<total;at+=rate){int count=(int)Math.Min(rate,total-at);Array.Clear(block);if(noise)for(int i=0;i<count;i++){short s=(short)random.Next(-3277,3278);block[i*2]=(byte)s;block[i*2+1]=(byte)(s>>8);}wave.Write(at/(double)rate,block.AsSpan(0,count*2));}wave.Complete();
    }
    internal static void Clip(string source,string output,double start,double end)
    {
        var info=Inspect(source);if(!double.IsFinite(start)||!double.IsFinite(end)||start<0||end<=start||end>info.Duration+.000001)throw new FormatException("选段必须位于音轨范围内。");if(Path.GetFullPath(source).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("不能覆盖音频源。");
        string temp=output+".tmp-"+Guid.NewGuid().ToString("N");try{using(var input=File.OpenRead(source)){long first=(long)Math.Round(start*info.Rate),last=Math.Min(info.Length/info.Align,(long)Math.Round(end*info.Rate));input.Position=info.Offset+first*info.Align;using var wave=new PcmWaveWriter(temp,info.Rate,info.Channels);byte[] buffer=new byte[info.Align*16384];long remaining=(last-first)*info.Align,written=0;while(remaining>0){int count=input.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(count==0)throw new EndOfStreamException();wave.Write(written/(double)(info.Rate*info.Align),buffer.AsSpan(0,count));written+=count;remaining-=count;}wave.Complete();}if(File.Exists(output))File.Copy(output,output+".bak-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));File.Move(temp,output,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static BitmapSource Spectrum(string path,double start,double span,CancellationToken token,int columns=700)
    {
        if(!double.IsFinite(start)||!double.IsFinite(span)||start<0||span<=0||span>120||columns<1||columns>1600)throw new ArgumentException("无效频谱视窗。");var info=Inspect(path);const int n=512,bins=128;var pixels=new byte[columns*bins*4];var fft=new Complex[n];byte[] bytes=new byte[n*info.Align];using var input=File.OpenRead(path);
        for(int x=0;x<columns;x++){token.ThrowIfCancellationRequested();long frame=(long)Math.Round((start+x/(double)columns*span)*info.Rate);Array.Clear(bytes);long offset=frame*info.Align;if(offset<info.Length){input.Position=info.Offset+offset;input.ReadExactly(bytes.AsSpan(0,(int)Math.Min(bytes.Length,info.Length-offset)));}for(int i=0;i<n;i++){double v=0;for(int c=0;c<info.Channels;c++){int p=(i*info.Channels+c)*2;v+=(short)(bytes[p]|bytes[p+1]<<8)/32768d;}fft[i]=v/info.Channels*(.5-.5*Math.Cos(2*Math.PI*i/(n-1)));}Transform(fft);for(int y=0;y<bins;y++){int bin=(bins-1-y)*2;double power=Math.Max(fft[bin].Magnitude,fft[Math.Min(n/2,bin+1)].Magnitude)/n;double a=Math.Clamp((20*Math.Log10(Math.Max(power,1e-7))+80)/70,0,1);int p=(y*columns+x)*4;pixels[p]=(byte)(40+70*a);pixels[p+1]=(byte)(15+220*a*a);pixels[p+2]=(byte)(5+245*a);pixels[p+3]=255;}}
        var bitmap=BitmapSource.Create(columns,bins,96,96,PixelFormats.Bgra32,null,pixels,columns*4);bitmap.Freeze();return bitmap;
    }
    static void Transform(Complex[] a){int n=a.Length;for(int i=1,j=0;i<n;i++){int bit=n>>1;for(; (j&bit)!=0;bit>>=1)j^=bit;j^=bit;if(i<j)(a[i],a[j])=(a[j],a[i]);}for(int len=2;len<=n;len<<=1){var root=Complex.FromPolarCoordinates(1,-2*Math.PI/len);for(int i=0;i<n;i+=len){Complex w=1;for(int j=0;j<len/2;j++){var u=a[i+j];var v=a[i+j+len/2]*w;a[i+j]=u+v;a[i+j+len/2]=u-v;w*=root;}}}}
}
