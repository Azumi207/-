using System.Text;

namespace Frameflow;
// Deterministic six-second, uncompressed AVI fixture with an interleaved PCM track.
// Generated only by --self-test; never reads the user's media.
public static class VideoFixture
{
    static byte[] Bytes(Action<BinaryWriter> write){using var m=new MemoryStream();using(var w=new BinaryWriter(m,Encoding.ASCII,true))write(w);return m.ToArray();}
    static void Four(BinaryWriter w,string value)=>w.Write(Encoding.ASCII.GetBytes(value));
    static void Chunk(BinaryWriter w,string id,byte[] bytes){Four(w,id);w.Write(bytes.Length);w.Write(bytes);if((bytes.Length&1)!=0)w.Write((byte)0);}
    static byte[] List(string name,Action<BinaryWriter> write)=>Bytes(w=>{Four(w,name);write(w);});
    public static void Write(string path)
    {
        const int width=160,height=90,fps=24,rate=16000,frames=144,size=width*height*3;
        var header=List("hdrl",w=>{
            Chunk(w,"avih",Bytes(b=>{b.Write(1000000/fps);b.Write(size*fps+rate*2);b.Write(0);b.Write(0x10);b.Write(frames);b.Write(0);b.Write(2);b.Write(size);b.Write(width);b.Write(height);for(int i=0;i<4;i++)b.Write(0);}));
            Chunk(w,"LIST",List("strl",b=>{
                Chunk(b,"strh",Bytes(s=>{Four(s,"vids");Four(s,"DIB ");s.Write(0);s.Write((short)0);s.Write((short)0);s.Write(0);s.Write(1);s.Write(fps);s.Write(0);s.Write(frames);s.Write(size);s.Write(-1);s.Write(0);s.Write((short)0);s.Write((short)0);s.Write((short)width);s.Write((short)height);}));
                Chunk(b,"strf",Bytes(s=>{s.Write(40);s.Write(width);s.Write(height);s.Write((short)1);s.Write((short)24);s.Write(0);s.Write(size);s.Write(0);s.Write(0);s.Write(0);s.Write(0);}));
            }));
            Chunk(w,"LIST",List("strl",b=>{
                Chunk(b,"strh",Bytes(s=>{Four(s,"auds");s.Write(0);s.Write(0);s.Write((short)0);s.Write((short)0);s.Write(0);s.Write(2);s.Write(rate*2);s.Write(0);s.Write(rate*6);s.Write(rate*2/fps+2);s.Write(-1);s.Write(2);for(int i=0;i<4;i++)s.Write((short)0);}));
                Chunk(b,"strf",Bytes(s=>{s.Write((short)1);s.Write((short)1);s.Write(rate);s.Write(rate*2);s.Write((short)2);s.Write((short)16);}));
            }));
        });
        var index=new List<(string Id,int Offset,int Length)>();
        var movie=List("movi",w=>{for(int frame=0;frame<frames;frame++){
            var image=new byte[size];for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=(y*width+x)*3;bool bar=x>=frame%width&&x<frame%width+8;image[i]=(byte)(bar?230:75+y);image[i+1]=(byte)(bar?180:45+x/2);image[i+2]=(byte)(bar?55:30+y/2);}
            index.Add(("00db",(int)w.BaseStream.Position,image.Length));Chunk(w,"00db",image);
            int first=frame*rate/fps,last=(frame+1)*rate/fps;
            var audio=Bytes(b=>{for(int i=first;i<last;i++){double t=i/(double)rate;double volume=t>.5&&t<2.12||t>2.2&&t<4||t>4.2&&t<4.5?.2:0;b.Write((short)(Math.Sin(2*Math.PI*440*t)*volume*32767));}});
            index.Add(("01wb",(int)w.BaseStream.Position,audio.Length));Chunk(w,"01wb",audio);
        }});
        var body=Bytes(w=>{Four(w,"AVI ");Chunk(w,"LIST",header);Chunk(w,"LIST",movie);Chunk(w,"idx1",Bytes(b=>{foreach(var i in index){Four(b,i.Id);b.Write(0x10);b.Write(i.Offset);b.Write(i.Length);}}));});
        using var output=new BinaryWriter(File.Create(path));Chunk(output,"RIFF",body);
    }
}
