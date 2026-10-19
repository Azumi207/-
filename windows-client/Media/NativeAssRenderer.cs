using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Frameflow.Media;

// One worker owns all handles. Keep the library, renderer, font and glyph caches
// between position edits; never call into the same renderer concurrently.
internal sealed unsafe class NativeAssRenderer : IDisposable
{
    IntPtr library, renderer, track;
    string snapshot="";
    bool limitedRgb;
    readonly Native.LogCallback callback;
    readonly ConcurrentQueue<string> log=new();
    int logChars;
    internal string Fonts {get;}
    internal string Diagnostics=>string.Join('\n',log);
    internal int RenderCount {get;private set;}
    internal NativeAssRenderer(string fonts)
    {
        Fonts=fonts;callback=Log;
        try
        {
            library=Native.ass_library_init();if(library==IntPtr.Zero)throw new IOException("libass 初始化失败。");
            Native.ass_set_message_cb(library,callback,IntPtr.Zero);
            // Same read-only supplemental font policy as the export engine.
            if(fonts.Length>0)
            {
                if(!Directory.Exists(fonts))throw new DirectoryNotFoundException("附加字体目录不存在。");
                var files=Directory.GetFiles(fonts).Where(p=>new[]{".ttf",".otf",".ttc",".otc"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Order(StringComparer.Ordinal).ToArray();
                if(files.Length>200||files.Sum(p=>new FileInfo(p).Length)>300_000_000)throw new IOException("字体目录超过 200 个文件 / 300 MB。");
                foreach(string file in files){byte[] b=File.ReadAllBytes(file);Native.ass_add_font(library,Path.GetFileName(file),b,b.Length);}
            }
            renderer=Native.ass_renderer_init(library);if(renderer==IntPtr.Zero)throw new IOException("libass 渲染器初始化失败。");
            Native.ass_set_cache_limits(renderer,10000,64);
            Native.ass_set_fonts(renderer,null,"Arial",4,IntPtr.Zero,1); // Windows DirectWrite, not fontconfig.
        }
        catch{Dispose();throw;}
    }
    void Log(int level,IntPtr format,IntPtr args,IntPtr data)
    {
        if(level>6||logChars>=100_000)return;
        try
        {
            byte[] bytes=new byte[4096];int n=Native._vsnprintf(bytes,(nuint)(bytes.Length-1),format,args);
            string line=Encoding.UTF8.GetString(bytes,0,n<0?bytes.Length-1:Math.Min(n,bytes.Length-1)).TrimEnd('\0');
            logChars+=line.Length;log.Enqueue(line);
        }
        catch{/* A diagnostic callback must never unwind into unmanaged code. */}
    }
    internal AssFrame Render(string ass,int sourceWidth,int sourceHeight,double time,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(renderer==IntPtr.Zero)throw new ObjectDisposedException(nameof(NativeAssRenderer));
        if(sourceWidth<16||sourceHeight<16||sourceWidth>7680||sourceHeight>4320||!double.IsFinite(time)||time<0||time>86400)throw new ArgumentException("预览尺寸或时间无效。");
        if(ass!=snapshot)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(ass+'\0');if(bytes.Length>64*1024*1024)throw new FormatException("字幕快照超过 64 MB。");
            var next=Native.ass_read_memory(library,bytes,(nuint)(bytes.Length-1),"UTF-8");if(next==IntPtr.Zero)throw new FormatException("libass 无法解析字幕。");
            if(track!=IntPtr.Zero)Native.ass_free_track(track);track=next;snapshot=ass;
            // Match the pinned FFmpeg 9 BGRA overlay's ASS color-range policy.
            // Read libass's parsed header (including default/unknown values),
            // rather than duplicating ASS parsing. TV range compresses RGB to
            // 16..235; None/PC keeps full range on our transparent RGB canvas.
            int matrix=Marshal.PtrToStructure<Native.TrackHeader>(track).matrix;
            limitedRgb=matrix is not (2 or 4 or 6 or 8 or 10);
        }
        // Interactive positioning is rendered at display resolution. Releasing
        // the drag retains this frame until the original-resolution FFmpeg
        // reference replaces it, so final antialiasing remains authoritative.
        double scale=Math.Min(1,Math.Min(1280d/sourceWidth,720d/sourceHeight));
        int w=Math.Max(1,(int)Math.Round(sourceWidth*scale)),h=Math.Max(1,(int)Math.Round(sourceHeight*scale));
        Native.ass_set_storage_size(renderer,sourceWidth,sourceHeight);Native.ass_set_frame_size(renderer,w,h);
        var images=Native.ass_render_frame(renderer,track,(long)Math.Round(time*1000),out _);
        var frame=new AssFrame(w,h,time);
        try
        {
            Array.Clear(frame.Pixels,0,w*h*4);
            fixed(byte* dst=frame.Pixels)
            for(var p=images;p!=IntPtr.Zero;)
            {
                token.ThrowIfCancellationRequested();var image=Marshal.PtrToStructure<Native.AssImage>(p);p=image.next;
                if(image.w<=0||image.h<=0||image.bitmap==IntPtr.Zero)continue;
                int x0=Math.Max(0,-image.x),y0=Math.Max(0,-image.y),x1=Math.Min(image.w,w-image.x),y1=Math.Min(image.h,h-image.y);
                int opacity=255-(int)(image.color&255),r=(int)(image.color>>24),g=(int)(image.color>>16)&255,b=(int)(image.color>>8)&255;
                if(limitedRgb){r=16+(r*219+127)/255;g=16+(g*219+127)/255;b=16+(b*219+127)/255;}
                for(int y=y0;y<y1;y++)
                {
                    byte* src=(byte*)image.bitmap+y*image.stride;byte* row=dst+((y+image.y)*w+image.x+x0)*4;
                    for(int x=x0;x<x1;x++,row+=4)
                    {
                        int a=(src[x]*opacity+127)/255,inv=255-a;
                        row[0]=(byte)((b*a+row[0]*inv+127)/255);row[1]=(byte)((g*a+row[1]*inv+127)/255);row[2]=(byte)((r*a+row[2]*inv+127)/255);row[3]=(byte)(a+(row[3]*inv+127)/255);
                    }
                }
            }
            RenderCount++;return frame;
        }
        catch{frame.Dispose();throw;}
    }
    public void Dispose()
    {
        if(track!=IntPtr.Zero){Native.ass_free_track(track);track=IntPtr.Zero;}
        if(renderer!=IntPtr.Zero){Native.ass_renderer_done(renderer);renderer=IntPtr.Zero;}
        if(library!=IntPtr.Zero){Native.ass_library_done(library);library=IntPtr.Zero;}
        GC.KeepAlive(callback);
    }
    static class Native
    {
        const string Dll="Frameflow.NativeAss";
        static readonly IntPtr module;
        static Native()
        {
            string path=Path.Combine(Path.GetDirectoryName(typeof(Native).Assembly.Location)!,"NativeAss","libass-9.dll");
            // Never resolve a rendering DLL from the working directory or PATH.
            module=LoadLibraryExW(path,IntPtr.Zero,0x1100);
            if(module==IntPtr.Zero)throw new DllNotFoundException("缺少实时字幕渲染组件："+path+" ("+Marshal.GetLastWin32Error()+")");
            NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,(name,_,_)=>name==Dll?module:IntPtr.Zero);
            if(ass_library_version()!=0x01705000)throw new NotSupportedException("实时渲染组件版本不匹配；需要 libass 0.17.5。");
        }
        [StructLayout(LayoutKind.Sequential)] internal struct AssImage
        {internal int w,h,stride;internal IntPtr bitmap;internal uint color;internal int x,y;internal IntPtr next;internal int type;}
        // Public, read-only prefix of ASS_Track from the pinned libass 0.17.5 ABI.
        [StructLayout(LayoutKind.Sequential)] internal struct TrackHeader
        {internal int stylesCount,stylesMax,eventsCount,eventsMax;internal IntPtr styles,events,styleFormat,eventFormat;internal int type,x,y;internal double timer;internal int wrap,border,kerning;internal IntPtr language;internal int matrix;}
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void LogCallback(int level,IntPtr format,IntPtr args,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryExW(string path,IntPtr file,uint flags);
        [DllImport("msvcrt.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int _vsnprintf(byte[] buffer,nuint count,IntPtr format,IntPtr args);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ass_library_init();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int ass_library_version();
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_library_done(IntPtr library);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ass_renderer_init(IntPtr library);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_renderer_done(IntPtr renderer);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_set_message_cb(IntPtr library,LogCallback callback,IntPtr data);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_set_fonts(IntPtr renderer,[MarshalAs(UnmanagedType.LPUTF8Str)] string? font,[MarshalAs(UnmanagedType.LPUTF8Str)] string family,int provider,IntPtr config,int update);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_set_cache_limits(IntPtr renderer,int glyphs,int megabytes);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_set_storage_size(IntPtr renderer,int w,int h);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_set_frame_size(IntPtr renderer,int w,int h);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ass_read_memory(IntPtr library,byte[] text,nuint length,[MarshalAs(UnmanagedType.LPUTF8Str)] string codepage);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_free_track(IntPtr track);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ass_render_frame(IntPtr renderer,IntPtr track,long time,out int changed);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void ass_add_font(IntPtr library,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,byte[] data,int length);
    }
}
