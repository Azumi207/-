using System.Text.Json;

namespace Frameflow.Core;
public sealed record RecentFile(string Path,string Kind);
public static class RecentFiles
{
    static readonly object Gate=new();
    public static List<RecentFile> Load(string path)
    {
        lock(Gate){if(!File.Exists(path))return [];try{if(new FileInfo(path).Length>1000000)return [];return (JsonSerializer.Deserialize<List<RecentFile>>(File.ReadAllText(path))??[]).Where(x=>x!=null&&!string.IsNullOrWhiteSpace(x.Path)&&new[]{"subtitle","media","video","audio","timecode","keyframe"}.Contains(x.Kind)).Take(20).ToList();}catch(JsonException){return [];}}
    }
    public static void Add(string store,string path,string kind)
    {
        path=System.IO.Path.GetFullPath(path);lock(Gate){var list=Load(store);list.RemoveAll(x=>x.Path.Equals(path,StringComparison.OrdinalIgnoreCase));list.Insert(0,new(path,kind));Write(store,list.Take(20).ToArray());}
    }
    public static void Clear(string store){lock(Gate)Write(store,[]);}
    static void Write(string path,IEnumerable<RecentFile> list){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);SubtitleDocument.AtomicSave(path,JsonSerializer.SerializeToUtf8Bytes(list),false);}
}
