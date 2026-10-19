using System.Text;

namespace Frameflow.Media;
// A seekable, audio-only PCM cache. Timestamp gaps become silence, not lost time.
internal sealed class PcmWaveWriter:IDisposable
{
    readonly FileStream stream;readonly int rate,channels;bool completed;
    public PcmWaveWriter(string path,int rate,int channels)
    {
        this.rate=rate;this.channels=channels;stream=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
        stream.Write(new byte[44]);
    }
    public void Write(double timestamp,ReadOnlySpan<byte> pcm)
    {
        long offset=44+(long)Math.Round(timestamp*rate)*channels*2;
        // RIFF is limited to 32-bit lengths. Never silently wrap or truncate.
        if(offset+pcm.Length>uint.MaxValue-8)throw new NotSupportedException("纯音频缓存超过 WAV 的 4 GB 限制，请使用较短媒体。");
        if(offset>stream.Length)stream.SetLength(offset); // Sparse/gap bytes read as silence.
        stream.Position=offset;stream.Write(pcm);
    }
    public void Complete()
    {
        stream.Position=0;using var w=new BinaryWriter(stream,Encoding.ASCII,true);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write((uint)(stream.Length-8));w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);w.Write((ushort)1);w.Write((ushort)channels);w.Write(rate);w.Write(rate*channels*2);w.Write((ushort)(channels*2));w.Write((ushort)16);
        w.Write(Encoding.ASCII.GetBytes("data"));w.Write((uint)(stream.Length-44));w.Flush();stream.Flush();completed=true;
    }
    public void Dispose(){string path=stream.Name;stream.Dispose();if(!completed)try{File.Delete(path);}catch(IOException){}}
}
