using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Controls;
using Frameflow.Core;
using Frameflow.Team;

namespace Frameflow;
public partial class MainWindow
{
    public async Task RunTeamOnlyAsync(string dir)
    {
        var lines=new List<string>();await RunTeamTestsAsync((value,label)=>{if(!value)throw new Exception("FAIL: "+label);lines.Add("PASS: "+label);File.WriteAllLines(Path.Combine(dir,"team-progress.txt"),lines);},dir);
        File.WriteAllLines(Path.Combine(dir,"team-tests.txt"),lines);Console.WriteLine(string.Join(Environment.NewLine,lines));
    }
    async Task RunTeamTestsAsync(Action<bool,string> check,string dir)
    {
        check(TeamClient.ValidateServer("http://127.0.0.1:8765").IsLoopback,"team accepts local-only HTTP development server");
        foreach(string url in new[]{"http://example.org","https://u:pw@example.org","https://example.org/path","file:///tmp/x","https://example.org?token=secret"})
        {bool rejected=false;try{TeamClient.ValidateServer(url);}catch(ArgumentException){rejected=true;}check(rejected,"team rejects unsafe endpoint "+url.Split('?')[0]);}
        string? fixture=Environment.GetEnvironmentVariable("FRAMEFLOW_TEAM_TEST_CONFIG");
        if(fixture==null){File.WriteAllText(Path.Combine(dir,"team-tests-not-run.txt"),"Real team server integration requires FRAMEFLOW_TEAM_TEST_CONFIG; local endpoint validation only.");return;}
        using var config=JsonDocument.Parse(File.ReadAllText(fixture));string urlBase=config.RootElement.GetProperty("url").GetString()!,password=config.RootElement.GetProperty("password").GetString()!;
        check(new Uri(urlBase).IsLoopback,"team integration fixture is loopback-only, no user server");
        using var admin=new TeamClient{CacheOverride=Path.Combine(dir,"team-cache")};await admin.Login(urlBase,"admin",password);
        var p=new TeamProject((await admin.Request("POST","projects",new{title="桌面交接验证 · "+Guid.NewGuid().ToString("N")[..6],description="生成的测试片源；并非用户素材"})).AsObject());
        foreach(var pair in new[]{("translator","translator"),("reviewer","reviewer")})p=await admin.Change(p,"members",new{revision=p.Revision,username=pair.Item1,role=pair.Item2});
        string source=Path.Combine(dir,"sample.avi"),sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        p=await admin.Upload(p,source);check(p.Stage=="translation_ready","native streaming upload fixes source and enables translation claim");
        using var translator=new TeamClient{CacheOverride=Path.Combine(dir,"team-cache")};using var reviewer=new TeamClient{CacheOverride=Path.Combine(dir,"team-cache")};
        var w=new MainWindow{SmokeMode=true,TestRecentPath=Path.Combine(dir,"team-recent.json")};w.Show();
        var openDialogs=new List<TeamHubDialog>();
        async Task<TeamHubDialog> Hub(TeamClient c)
        {
            var h=w.CreateTeamDialog(c,p.Id);openDialogs.Add(h);h.Show();await Task.Delay(50);await h.Operation;return h;
        }
        async Task Click(TeamHubDialog h,string key)
        {
            check(h.Buttons[key].IsEnabled,"team native button enabled: "+key);h.Buttons[key].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await h.Operation;
            check(h.Message.Foreground!=Brushes.DarkRed,"team native command completed: "+key+" "+h.Message.Text);
        }
        async Task Login(TeamHubDialog h,string name)
        {h.Address.Text=urlBase;h.Username.Text=name;h.Password.Password=password;await Click(h,"login");check(h.Password.Password.Length==0,"team clears password UI after login");}
        async Task WaitVideo(){var end=DateTime.UtcNow.AddSeconds(15);while(!w.mediaReady&&DateTime.UtcNow<end)await Task.Delay(100);check(w.mediaReady&&w.Player.NaturalVideoWidth==160,"team download opens valid video in actual Windows editor");w.Player.IsMuted=true;}
        try
        {
            var h=await Hub(translator);await Login(h,"translator");check(h.Current?.Id==p.Id,"native project hall lists assigned project");check(!h.Buttons["user"].IsEnabled,"non-admin account creation control disabled");
            await Click(h,"claim");check(h.Current!.Stage=="translating","native claim changes stage");await Click(h,"download");await WaitVideo();
            string first=w.subtitlePath!;var work=TeamWork.Read(first)!;check(work.Project==p.Id&&File.Exists(first+".team.json"),"team creates native ASS plus durable offline association");
            check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(work.Video)))==sourceHash,"native download checks source SHA256");
            // Exercise the same editor/model that normal subtitle editing uses; preserve style/actor/overrides.
            w.document=SubtitleDocument.Load(Path.Combine(dir,"sample.ass"));w.RefreshAll();w.SelectRow(w.document.Rows.First());w.TextEditor.Text=@"{\pos(960,900)}团队初翻";w.ActorBox.Text="翻译演员";w.ApplyEditor();
            h=await Hub(translator);h.Note.Text="初翻草稿";await Click(h,"draft");check(h.Current!.Stage=="translating"&&h.Current.Latest!=null,"draft versions do not hand off task");
            h.Note.Text="初翻完成，请校对";await Click(h,"submit");check(h.Current!.Stage=="review_ready"&&!h.Buttons["submit"].IsEnabled,"submit hands task to review, disables duplicate submit");h.Close();
            string original=File.ReadAllText(first);check(original.Contains(@"\pos(960,900)")&&original.Contains("翻译演员"),"native upload keeps ASS positioning and speaker fields");
            h=await Hub(reviewer);await Login(h,"reviewer");await Click(h,"claim");await Click(h,"download");await WaitVideo();
            check(w.document.Rows.First().Actor=="翻译演员"&&w.document.Rows.First().Text.Contains(@"\pos(960,900)"),"reviewer receives translator ASS style/actor exactly");
            w.SelectRow(w.document.Rows.First());w.TextEditor.Text=@"{\pos(960,900)}校对修改";w.ApplyEditor();
            h=await Hub(reviewer);h.Note.Text="";h.Buttons["return"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await h.Operation;check(h.Message.Text.Contains("退回前")&&h.Current!.Stage=="reviewing","native return requires reason before uploading");
            h.Note.Text="第一句术语请再确认";await Click(h,"return");check(h.Current!.Stage=="translation_ready","review return includes modified ASS and reason");h.Close();
            h=await Hub(translator);await Click(h,"claim");
            // Reopen old translator draft; its stale base must not overwrite reviewer changes.
            w.OpenSubtitle(first);h.Buttons["submit"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await h.Operation;
            check(h.Message.Text.Contains("新版本")&&File.ReadAllText(first)==original,"stale local draft rejected without overwriting local/server versions");
            await Click(h,"download");await WaitVideo();check(w.subtitlePath!=first&&File.ReadAllText(first)==original,"download latest always creates new copy, preserving offline draft");
            w.SelectRow(w.document.Rows.First());w.TextEditor.Text=@"{\pos(960,900)}术语已确认";w.ApplyEditor();h=await Hub(translator);h.Note.Text="术语确认完成";await Click(h,"submit");h.Close();
            h=await Hub(reviewer);await Click(h,"claim");await Click(h,"download");await WaitVideo();h=await Hub(reviewer);h.Note.Text="校对通过";await Click(h,"approve");
            check(h.Current!.Stage=="encoding_ready"&&h.Current.Data["approved_id"]!.GetValue<long>()==h.Current.Latest,"native approve pins exact accepted version for later encoding");
            check(h.Details.Text.Contains("第一句术语请再确认")&&h.Details.Text.Contains("校对通过"),"hall displays version notes and handoff decisions");
            h.UpdateLayout();var image=new RenderTargetBitmap((int)h.ActualWidth,(int)h.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(h);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(dir,"team-hall.png")))encoder.Save(output);
            File.WriteAllText(Path.Combine(dir,"team-result.json"),h.Current.Data.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
            await Click(h,"logout");check(!reviewer.LoggedIn&&File.Exists(w.subtitlePath!),"logout clears token but keeps offline subtitles");
            check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))==sourceHash,"team workflow never modifies original media fixture");
        }
        finally{foreach(var d in openDialogs)if(d.IsLoaded)d.Close();w.dirty=w.draftDirty=false;w.Close();}
    }
}
