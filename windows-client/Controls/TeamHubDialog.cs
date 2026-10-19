using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Frameflow.Team;
using Microsoft.Win32;

namespace Frameflow.Controls;

internal sealed class TeamHubDialog:SubtitleToolDialog
{
    readonly TeamClient client;
    readonly Func<TeamProject,(TeamWork Work,string Ass)> capture;
    readonly Func<TeamWork,bool> open;
    readonly Action<TeamWork> saved;
    readonly string? preferred;
    internal readonly TextBox Address,Username,Note,ProjectTitle,ProjectDescription,MemberName,NewName;
    internal readonly PasswordBox Password=new(){MinHeight=28},NewPassword=new(){MinHeight=28};
    internal readonly CheckBox Insecure=new(){Content="仅限可信测试网络：允许非本机 HTTP（密码和素材未加密）",Margin=new Thickness(3,5,3,5)};
    internal readonly ComboBox Role;
    internal readonly DataGrid ProjectsGrid=new(){Height=150,AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,CanUserAddRows=false};
    internal readonly TextBlock Details=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,6,4,6)};
    internal readonly Dictionary<string,Button> Buttons=new();
    readonly Expander admin;
    readonly TextBlock session=new(){Margin=new Thickness(4),Foreground=Brushes.SlateGray};
    readonly List<FrameworkElement> busyControls=new();
    CancellationTokenSource? cancellation;
    bool closeWhenDone;
    internal Task Operation {get;private set;}=Task.CompletedTask;
    internal bool Busy=>cancellation!=null;
    internal TeamProject? Current {get;private set;}
    internal TeamHubDialog(TeamClient client,Func<TeamProject,(TeamWork Work,string Ass)> capture,Func<TeamWork,bool> open,Action<TeamWork> saved,string? preferred=null):base("团队 · 项目大厅",980,850)
    {
        this.client=client;this.capture=capture;this.open=open;this.saved=saved;this.preferred=preferred;
        Hint("登录服务器 → 领取任务 → 下载到本地编辑 → 同步/提交版本 → 校对退回或通过。原有单机编辑不受影响；密码与登录令牌不落盘。一个服务端对应一个字幕组。");
        var login=new Grid();login.ColumnDefinitions.Add(new(){Width=new GridLength(3,GridUnitType.Star)});login.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});login.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});Body.Children.Add(login);
        StackPanel Column(int i){var p=new StackPanel{Margin=new Thickness(4)};Grid.SetColumn(p,i);login.Children.Add(p);return p;}
        Address=Input(Column(0),"服务器地址",client.Server==""?"http://127.0.0.1:8765":client.Server);
        Username=Input(Column(1),"用户名",client.Username);var pass=Column(2);pass.Children.Add(new TextBlock{Text="密码",Margin=new Thickness(3,6,3,1)});pass.Children.Add(Password);
        Body.Children.Add(Insecure);var loginButtons=new WrapPanel();Body.Children.Add(loginButtons);
        Add("login","登录",async()=>{string password=Password.Password;Password.Clear();await client.Login(Address.Text,Username.Text,password,Insecure.IsChecked==true,Token);await Refresh();},loginButtons);
        Add("logout","退出登录",async()=>{await client.Logout(Token);ProjectsGrid.ItemsSource=null;Current=null;Details.Text="已退出；本地工作文件保留。";},loginButtons);
        Add("refresh","刷新大厅",()=>Refresh(),loginButtons);Body.Children.Add(session);
        foreach(var pair in new[]{("项目","Title",3.0),("阶段","StageLabel",2.0),("领取人","Owner",1.3),("我的角色","RoleLabel",1.0)})ProjectsGrid.Columns.Add(new DataGridTextColumn{Header=pair.Item1,Binding=new Binding(pair.Item2),Width=new DataGridLength(pair.Item3,DataGridLengthUnitType.Star)});
        Body.Children.Add(ProjectsGrid);ProjectsGrid.SelectionChanged+=(_,_)=>{if(!Busy&&ProjectsGrid.SelectedItem is TeamProject p)Start(async()=>{await Load(p.Id);});};
        Body.Children.Add(new Expander{Header="项目详情 / 版本记录 / 校对意见（可滚动）",IsExpanded=true,Content=new ScrollViewer{Content=Details,MaxHeight=145,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var tasks=new WrapPanel();Body.Children.Add(tasks);
        Add("claim","领取当前阶段",async()=>{var p=Require();await client.Claim(p,Token);await Refresh(p.Id);},tasks);
        Add("release","释放任务",async()=>{var p=Require();await client.Release(p,Token);await Refresh(p.Id);},tasks);
        Add("download","下载最新为新副本并编辑",async()=>{var p=Require();var work=await client.Download(p,Progress(),Token);if(open(work))closeWhenDone=true;},tasks);
        Add("local","继续本地草稿",()=>{var work=client.LocalWork(Require().Id)??throw new InvalidOperationException("此账号没有本地工作副本，请先下载。");if(open(work))closeWhenDone=true;return Task.CompletedTask;},tasks);
        Note=Input(Body,"提交说明 / 校对意见（退回必填；本版为项目级意见）","",true);Note.MinHeight=55;
        var submit=new WrapPanel();Body.Children.Add(submit);
        Add("draft","同步当前字幕草稿",async()=>{await Sync("draft");await Refresh(Current!.Id);},submit);
        Add("submit","完成初翻 · 交给校对",async()=>{await Sync("submit");await Refresh(Current!.Id);},submit);
        Add("return","校对退回",()=>Review("return"),submit);
        Add("approve","校对通过",()=>Review("approve"),submit);
        Hint("同步/提交只针对当前编辑器打开的团队工作字幕。校对退回/通过会先同步当前修改；通过绑定该版本，不会自动压制或发布。下载总是创建新副本，绝不覆盖离线草稿。");
        var manage=new StackPanel();admin=new Expander{Header="项目管理（管理员建立账号、项目、分配成员）/ 片源上传",Content=manage,IsExpanded=false};Body.Children.Add(admin);
        var account=new WrapPanel();manage.Children.Add(account);var userPanel=new StackPanel{Width=200,Margin=new Thickness(4)};account.Children.Add(userPanel);NewName=Input(userPanel,"新成员用户名");var secretPanel=new StackPanel{Width=240,Margin=new Thickness(4)};account.Children.Add(secretPanel);secretPanel.Children.Add(new TextBlock{Text="初始密码（12–128 字符）"});secretPanel.Children.Add(NewPassword);
        Add("user","创建账号",async()=>{string password=NewPassword.Password;NewPassword.Clear();await client.Request("POST","users",new{username=NewName.Text,password},Token);Message.Text="账号已创建；请安全地交付初始密码，再分配项目角色。";},account);
        ProjectTitle=Input(manage,"新项目名称");ProjectDescription=Input(manage,"项目说明");Add("project","新建项目",async()=>{var p=await client.Request("POST","projects",new{title=ProjectTitle.Text,description=ProjectDescription.Text},Token);await Refresh(p["id"]!.GetValue<string>());},manage);
        var membership=new WrapPanel();manage.Children.Add(membership);var names=new StackPanel{Width=200,Margin=new Thickness(4)};membership.Children.Add(names);MemberName=Input(names,"分配已有成员到所选项目");var roles=new StackPanel{Width=170,Margin=new Thickness(4)};membership.Children.Add(roles);Role=Choice(roles,"角色",["source · 片源","translator · 初翻/打轴","reviewer · 校对","encoder · 压制"]);
        Add("member","分配角色",async()=>{var p=Require();await client.Change(p,"members",new{revision=p.Revision,username=MemberName.Text,role=((string)Role.SelectedItem).Split(' ')[0]},Token);await Refresh(p.Id);},membership);
        Add("upload","上传片源 / 继续未完成上传…",async()=>{var p=Require();var dialog=new OpenFileDialog{Title="上传片源（将传到当前服务器）",Filter="视频|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.m4v"};if(dialog.ShowDialog(this)==true){await client.Upload(p,dialog.FileName,Progress(),Token);await Refresh(p.Id);}},manage);
        Add("discardUpload","放弃自己的未完成上传…",async()=>{var p=await client.Detail(Require().Id,Token);string id=p.Data["pending_upload"]?["id"]?.GetValue<string>()??throw new InvalidOperationException("没有自己的有效未完成上传。");if(MessageBox.Show(this,"只删除服务器上的本次上传临时块，不删除你的本地片源。确定放弃？","放弃上传",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK){await client.Request("DELETE","uploads/"+Uri.EscapeDataString(id),ct:Token);await Refresh(p.Id);}},manage);
        busyControls.AddRange(new FrameworkElement[]{login,Insecure,ProjectsGrid,Note,NewName,NewPassword,ProjectTitle,ProjectDescription,MemberName,Role});Body.Children.Add(Message);
        var cancel=new Button{Content="取消传输",MinWidth=95};cancel.Click+=(_,_)=>cancellation?.Cancel();Actions.Children.Add(cancel);
        var close=new Button{Content="关闭",MinWidth=80};close.Click+=(_,_)=>Close();Actions.Children.Add(close);
        Closing+=(_,e)=>{if(Busy){cancellation!.Cancel();e.Cancel=true;Message.Text="正在取消网络操作，请稍后再关闭；本地字幕保留。";}};
        Loaded+=(_,_)=>{if(client.LoggedIn)Start(()=>Refresh(preferred));};UpdateControls();
    }
    CancellationToken Token=>cancellation?.Token??CancellationToken.None;
    IProgress<string> Progress()=>new Progress<string>(text=>Message.Text=text);
    Button Add(string key,string label,Func<Task> action,Panel parent){var button=new Button{Content=label,MinHeight=28,Margin=new Thickness(3)};Buttons.Add(key,button);button.Click+=(_,_)=>Start(action);parent.Children.Add(button);return button;}
    internal void Start(Func<Task> action){if(Busy)return;Operation=Run(action);}
    async Task Run(Func<Task> action)
    {
        cancellation=new();Message.Text="";Message.Foreground=Brushes.SlateGray;UpdateControls();
        try{await action();}
        catch(OperationCanceledException){Message.Text="操作已取消或超时。本地字幕保留；上传/下载可重试续传。若刚提交版本，请先刷新确认结果。";}
        catch(Exception ex) when(ex is IOException or HttpRequestException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {Message.Foreground=Brushes.DarkRed;Message.Text=ex.Message+"\n本地文件未删除；网络中断或冲突后请先刷新再重试。";}
        finally{cancellation.Dispose();cancellation=null;UpdateControls();if(closeWhenDone)Close();}
    }
    internal async Task Refresh(string? selected=null)
    {
        string? id=selected??Current?.Id??preferred;var rows=await client.Projects(Token);ProjectsGrid.ItemsSource=rows;var p=rows.FirstOrDefault(x=>x.Id==id)??rows.FirstOrDefault();ProjectsGrid.SelectedItem=p;
        if(p!=null)await Load(p.Id);else{Current=null;Details.Text="暂无项目。请管理员建立项目并分配成员。";}
    }
    async Task Load(string id)
    {
        Current=await client.Detail(id,Token);var p=Current;
        string Label(string? value)=>value switch{"draft"=>"同步草稿","submit"=>"提交校对","return"=>"退回修改","approve"=>"校对通过","claim"=>"领取任务","release"=>"释放任务","created"=>"创建项目","member"=>"分配成员","source_uploaded"=>"片源已上传",_=>value??""};
        string Versions=string.Join("\n",(p.Data["versions"]?.AsArray()??new JsonArray()).Take(8).Select(n=>$"v{n!["number"]} · {n["author"]} · {Label(n["kind"]?.ToString())} · {n["note"]}"));
        string events=string.Join("\n",(p.Data["events"]?.AsArray()??new JsonArray()).Take(5).Select(n=>$"{n!["actor"]} · {Label(n["action"]?.ToString())} · {n["detail"]}"));
        string members=string.Join("、",(p.Data["members"]?.AsArray()??new JsonArray()).Select(n=>$"{n!["username"]} ({n["role"]})"));
        Details.Text=$"{p.Title}  |  {p.StageLabel}  |  修订 {p.Revision}\n{p.Data["description"]}\n成员：{members}\n片源：{p.Media?["name"]?.ToString()??"未上传"}  ·  当前字幕版本 ID：{p.Latest?.ToString()??"尚无"}\n版本记录（最近 8 个）：\n{Versions}\n最近交接记录：\n{events}";
    }
    TeamProject Require()=>Current??throw new InvalidOperationException("请先选择项目。");
    async Task Sync(string kind)
    {
        var p=Require();var (work,ass)=capture(p);var result=await client.Submit(p,work,ass,kind,Note.Text,Token);Current=result.Project;saved(result.Work);Message.Text="字幕版本已提交并校验；本地副本保留。";
    }
    async Task Review(string action)
    {
        if(action=="return"&&string.IsNullOrWhiteSpace(Note.Text))throw new InvalidOperationException("退回前请填写具体校对意见。");
        await Sync("draft");var p=Require();await client.Change(p,"review",new{revision=p.Revision,version_id=p.Latest,action,note=Note.Text},Token);await Refresh(p.Id);
    }
    void UpdateControls()
    {
        bool logged=client.LoggedIn;foreach(var control in busyControls)control.IsEnabled=!Busy;
        foreach(var button in Buttons.Values)button.IsEnabled=!Busy&&logged;
        Buttons["login"].IsEnabled=!Busy;Buttons["logout"].IsEnabled=!Busy&&logged;
        foreach(string key in new[]{"user","project","member"})Buttons[key].IsEnabled=!Busy&&logged&&client.Admin;
        session.Text=logged?$"已登录：{client.Username}{(client.Admin?"（管理员）":"")} · {client.Server}":"未登录 · 账号由服务端管理员创建";
        bool owner=Current?.OwnerId==client.UserId;
        Buttons["claim"].IsEnabled=!Busy&&logged&&Current!=null&&(Current.Stage=="translation_ready"&&(client.Admin||Current.Role=="translator")||Current.Stage=="review_ready"&&(client.Admin||Current.Role=="reviewer"));
        Buttons["release"].IsEnabled=!Busy&&logged&&Current!=null&&(Current.Stage is "translating" or "reviewing")&&(owner||client.Admin);
        Buttons["draft"].IsEnabled=!Busy&&logged&&owner&&(Current!.Stage is "translating" or "reviewing");
        Buttons["submit"].IsEnabled=!Busy&&logged&&owner&&Current!.Stage=="translating";
        Buttons["return"].IsEnabled=Buttons["approve"].IsEnabled=!Busy&&logged&&owner&&Current!.Stage=="reviewing";
        Buttons["upload"].IsEnabled=!Busy&&logged&&Current?.Stage=="source_pending"&&(client.Admin||Current.Role=="source");
        Buttons["discardUpload"].IsEnabled=Buttons["upload"].IsEnabled;
        Buttons["download"].IsEnabled=Buttons["local"].IsEnabled=!Busy&&logged&&Current?.Media!=null;
    }
}
