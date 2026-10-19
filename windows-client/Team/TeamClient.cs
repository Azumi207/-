using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Frameflow.Core;

namespace Frameflow.Team;

internal sealed class TeamException(string message,HttpStatusCode status):IOException(message)
{internal HttpStatusCode Status {get;}=status;}

internal sealed class TeamProject(JsonObject data)
{
    internal JsonObject Data {get;}=data;
    public string Id=>Data["id"]!.GetValue<string>();
    public string Title=>Data["title"]!.GetValue<string>();
    public string Stage=>Data["stage"]!.GetValue<string>();
    public string StageLabel=>Stage switch {"source_pending"=>"待上传片源","translation_ready"=>"初翻待领取","translating"=>"初翻 / 打轴中","review_ready"=>"校对待领取","reviewing"=>"校对中","encoding_ready"=>"校对通过 · 待压制",_=>Stage};
    public string Owner=>Data["owner_name"]?.GetValue<string>()??"";
    public string Role=>Data["my_role"]?.GetValue<string>()??"";
    public string RoleLabel=>Role switch{"admin"=>"管理员","source"=>"片源","translator"=>"初翻 / 打轴","reviewer"=>"校对","encoder"=>"压制",_=>Role};
    public long Revision=>Data["revision"]!.GetValue<long>();
    public long? Latest=>Data["latest_id"]?.GetValue<long>();
    public string? OwnerId=>Data["owner_id"]?.GetValue<string>();
    internal JsonObject? Media=>Data["media"] as JsonObject;
}

internal sealed record TeamWork(string Server,string User,string Project,string Subtitle,string Video,long? BaseVersion)
{
    internal static TeamWork? Read(string? subtitle)
    {
        if(subtitle==null||!File.Exists(subtitle+".team.json"))return null;
        var result=JsonSerializer.Deserialize<TeamWork>(File.ReadAllText(subtitle+".team.json"));
        if(result==null||!Path.GetFullPath(subtitle).Equals(Path.GetFullPath(result.Subtitle),StringComparison.OrdinalIgnoreCase))throw new IOException("团队关联文件与当前字幕不匹配，未上传。");
        return result;
    }
    internal void Save()=>SubtitleDocument.AtomicSave(Subtitle+".team.json",JsonSerializer.SerializeToUtf8Bytes(this),false);
}

internal sealed class TeamClient:IDisposable
{
    readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(3),MaxResponseContentBufferSize=16*1024*1024};
    internal string Server {get;private set;}="";
    internal string UserId {get;private set;}="";
    internal string Username {get;private set;}="";
    internal bool Admin {get;private set;}
    internal bool LoggedIn=>http.DefaultRequestHeaders.Authorization!=null;
    internal string? CacheOverride;
    internal string CacheRoot=>CacheOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Frameflow","Team");
    static string E(string id)=>Uri.EscapeDataString(id);
    static string SafeId(string value)=>Guid.TryParseExact(value,"N",out _)?value:throw new IOException("服务器返回了无效标识，未访问本地缓存路径。");
    static string Hex(byte[] bytes)=>Convert.ToHexString(bytes).ToLowerInvariant();
    static async Task<string> Hash(string path,CancellationToken ct){using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,true);return Hex(await SHA256.HashDataAsync(f,ct));}
    internal static Uri ValidateServer(string value,bool allowHttp=false)
    {
        if(!Uri.TryCreate(value.Trim().TrimEnd('/')+"/",UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||uri.AbsolutePath!="/")throw new ArgumentException("请输入服务器根地址，例如 https://team.example.org；不要包含账号、路径或查询参数。");
        if(uri.Scheme=="http"&&!uri.IsLoopback&&!allowHttp)throw new ArgumentException("非本机服务器必须使用 HTTPS。仅在可信测试网络中明确勾选 HTTP 风险确认。");
        return uri;
    }
    async Task Check(HttpResponseMessage response,CancellationToken ct)
    {
        if(response.IsSuccessStatusCode)return;
        string message=$"服务器返回 {(int)response.StatusCode}。";
        // Errors are bounded; do not display raw HTML, tokens or arbitrary huge proxy responses.
        try{using var stream=await response.Content.ReadAsStreamAsync(ct);var buffer=new byte[16384];int n=await stream.ReadAsync(buffer,ct);var error=JsonNode.Parse(buffer.AsSpan(0,n));message=error?["detail"]?.GetValue<string>()??message;}catch(Exception ex) when(ex is JsonException or InvalidOperationException){}
        if(response.StatusCode==HttpStatusCode.Unauthorized)Forget();
        throw new TeamException(message,response.StatusCode);
    }
    internal async Task<JsonNode> Request(string method,string route,object? data=null,CancellationToken ct=default)
    {
        using var req=new HttpRequestMessage(new HttpMethod(method),new Uri(new Uri(Server),"api/"+route));
        if(data!=null)req.Content=new StringContent(JsonSerializer.Serialize(data,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(req,ct);await Check(response,ct);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))??throw new IOException("服务器未返回有效数据。");
    }
    internal async Task Login(string server,string name,string password,bool allowHttp=false,CancellationToken ct=default)
    {
        var uri=ValidateServer(server,allowHttp);Forget();Server=uri.AbsoluteUri;
        var login=await Request("POST","login",new{username=name,password},ct);
        UserId=SafeId(login["user"]!["id"]!.GetValue<string>());Username=login["user"]!["username"]!.GetValue<string>();Admin=login["user"]!["admin"]!.GetValue<bool>();
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login["token"]!.GetValue<string>());
    }
    void Forget(){http.DefaultRequestHeaders.Authorization=null;UserId=Username="";Admin=false;}
    internal async Task Logout(CancellationToken ct=default){try{if(LoggedIn)await Request("POST","logout",ct:ct);}finally{Forget();}}
    internal async Task<TeamProject[]> Projects(CancellationToken ct=default)=>(await Request("GET","projects",ct:ct)).AsArray().Select(n=>new TeamProject(n!.AsObject())).ToArray();
    internal async Task<TeamProject> Detail(string id,CancellationToken ct=default)=>new((await Request("GET","projects/"+E(id),ct:ct)).AsObject());
    internal async Task<TeamProject> Change(TeamProject p,string action,object data,CancellationToken ct=default)=>new((await Request("POST","projects/"+E(p.Id)+"/"+action,data,ct)).AsObject());
    internal async Task<TeamProject> Claim(TeamProject p,CancellationToken ct=default)=>await Change(p,"claim",new{revision=p.Revision},ct);
    internal async Task<TeamProject> Release(TeamProject p,CancellationToken ct=default)=>await Change(p,"release",new{revision=p.Revision},ct);
    internal void ValidateWork(TeamWork w,TeamProject p)
    {
        if(w.Server!=Server||w.User!=UserId||w.Project!=p.Id)throw new InvalidOperationException("当前编辑的不是此账号领取的项目。请先下载打开该项目，或打开它的本地工作字幕。");
        if(w.BaseVersion!=p.Latest)throw new InvalidOperationException("服务器已有新版本；本地草稿未覆盖。请下载最新版本为新副本，比较并合并后提交。");
    }
    internal async Task<(TeamProject Project,TeamWork Work)> Submit(TeamProject p,TeamWork w,string ass,string kind,string note,CancellationToken ct=default)
    {
        ValidateWork(w,p);
        var next=await Change(p,"versions",new{revision=p.Revision,base_version=w.BaseVersion,ass,kind,note},ct);
        var updated=w with{BaseVersion=next.Latest};updated.Save();Remember(updated);return(next,updated);
    }
    string ProjectFolder(string project)=>Path.Combine(CacheRoot,Hex(SHA256.HashData(Encoding.UTF8.GetBytes(Server)))[..20],SafeId(UserId),SafeId(project));
    void Remember(TeamWork w){string folder=ProjectFolder(w.Project);Directory.CreateDirectory(folder);SubtitleDocument.AtomicSave(Path.Combine(folder,"latest-work.json"),JsonSerializer.SerializeToUtf8Bytes(w),false);}
    internal TeamWork? LocalWork(string project)
    {
        string path=Path.Combine(ProjectFolder(project),"latest-work.json");if(!File.Exists(path))return null;
        var w=JsonSerializer.Deserialize<TeamWork>(File.ReadAllText(path));return w!=null&&w.Server==Server&&w.User==UserId&&w.Project==project&&File.Exists(w.Subtitle)?TeamWork.Read(w.Subtitle):null;
    }
    internal async Task<TeamWork> Download(TeamProject p,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        if(p.Media==null)throw new InvalidOperationException("片源还未上传。");
        string folder=ProjectFolder(p.Id);Directory.CreateDirectory(folder);
        string video=await DownloadMedia(p,folder,progress,ct);
        string ass;
        if(p.Latest is long version)
        {
            using var response=await http.GetAsync(new Uri(new Uri(Server),$"api/projects/{E(p.Id)}/versions/{version}"),ct);await Check(response,ct);
            var bytes=await response.Content.ReadAsByteArrayAsync(ct);
            if(bytes.Length>4*1024*1024||!response.Headers.TryGetValues("X-Content-SHA256",out var hashes)||Hex(SHA256.HashData(bytes))!=hashes.Single())throw new IOException("字幕版本校验失败，未打开。");
            ass=new UTF8Encoding(false,true).GetString(bytes);SubtitleDocument.Parse(ass);
        }
        else {var doc=SubtitleDocument.New();doc.Add(0,2,"");ass=doc.Serialize();}
        // Always a new working copy. Download/retry must never overwrite offline edits.
        string working=Path.Combine(folder,$"work-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"+(ass.Contains("[V4 Styles]",StringComparison.OrdinalIgnoreCase)?".ssa":".ass"));
        SubtitleDocument.AtomicSave(working,Encoding.UTF8.GetBytes(ass),false);
        var work=new TeamWork(Server,UserId,p.Id,working,video,p.Latest);work.Save();Remember(work);return work;
    }
    async Task<string> DownloadMedia(TeamProject p,string folder,IProgress<string>? progress,CancellationToken ct)
    {
        var m=p.Media!;string sha=m["sha256"]!.GetValue<string>(),fid=m["id"]!.GetValue<string>();long size=m["size"]!.GetValue<long>();
        if(sha.Length!=64||!sha.All(Uri.IsHexDigit)||size<=0||size>1000L*1024*1024*1024)throw new IOException("素材元数据无效。");
        string ext=Path.GetExtension(m["name"]!.GetValue<string>()).ToLowerInvariant();if(!new[]{".mp4",".mkv",".avi",".mov",".webm",".wmv",".m4v"}.Contains(ext))throw new IOException("素材扩展名无效。");
        string path=Path.Combine(folder,sha+ext),part=path+".part";
        if(File.Exists(path)&&new FileInfo(path).Length==size&&await Hash(path,ct)==sha){progress?.Report("使用已校验的本地片源");return path;}
        long offset=File.Exists(part)?new FileInfo(part).Length:0;
        if(offset>size){File.Delete(part);offset=0;}
        if(offset<size)
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,new Uri(new Uri(Server),"api/files/"+E(fid)));
            if(offset>0){req.Headers.Range=new RangeHeaderValue(offset,null);req.Headers.IfRange=new RangeConditionHeaderValue(new EntityTagHeaderValue('"'+sha+'"'));}
            using var response=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);await Check(response,ct);
            if(response.StatusCode==HttpStatusCode.OK)offset=0;
            else if(response.StatusCode!=HttpStatusCode.PartialContent||response.Content.Headers.ContentRange?.From!=offset||response.Content.Headers.ContentRange?.Length!=size)throw new IOException("断点下载响应不一致，保留本地文件等待重试。");
            if(response.Headers.ETag?.Tag!='"'+sha+'"')throw new IOException("片源校验标识已变化。");
            using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new FileStream(part,offset==0?FileMode.Create:FileMode.Append,FileAccess.Write,FileShare.None,128*1024,true);
            byte[] buffer=new byte[256*1024];int n;while((n=await input.ReadAsync(buffer,ct))>0){if(offset+n>size)throw new IOException("下载数据超过声明大小。");await output.WriteAsync(buffer.AsMemory(0,n),ct);offset+=n;progress?.Report($"下载片源 {offset*100/size}% · {offset/1024/1024}/{size/1024/1024} MB");}
        }
        if(new FileInfo(part).Length!=size)throw new IOException("下载未完整，可再次点击续传。");
        progress?.Report("校验片源 SHA256…");if(await Hash(part,ct)!=sha){File.Delete(part);throw new IOException("片源校验不一致，已丢弃损坏的下载临时文件；请重试。");}
        File.Move(part,path,true);return path;
    }
    internal async Task<TeamProject> Upload(TeamProject p,string path,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        progress?.Report("计算片源 SHA256…");string sha=await Hash(path,ct);long size=new FileInfo(path).Length;
        var upload=await Request("POST","projects/"+E(p.Id)+"/uploads",new{name=Path.GetFileName(path),size,sha256=sha},ct);
        string id=upload["id"]!.GetValue<string>();long offset=upload["offset"]!.GetValue<long>();
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,128*1024,true);input.Position=offset;byte[] buffer=new byte[4*1024*1024];
        while(offset<size)
        {
            int n=await input.ReadAsync(buffer,ct);if(n==0)throw new IOException("片源在上传期间变更，请重新上传。");
            using var req=new HttpRequestMessage(HttpMethod.Put,new Uri(new Uri(Server),"api/uploads/"+E(id)));req.Headers.Add("Upload-Offset",offset.ToString(System.Globalization.CultureInfo.InvariantCulture));req.Content=new ByteArrayContent(buffer,0,n);
            using var response=await http.SendAsync(req,ct);await Check(response,ct);var result=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));long next=result!["offset"]!.GetValue<long>();if(next!=offset+n)throw new IOException("服务器上传偏移不一致。");offset=next;progress?.Report($"上传片源 {offset*100/size}% · 可中断后续传");
        }
        progress?.Report("服务端正在校验片源；完成后才能领取任务…");return new((await Request("POST","uploads/"+E(id)+"/finish",ct:ct)).AsObject());
    }
    public void Dispose()=>http.Dispose();
}
