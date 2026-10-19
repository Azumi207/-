using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Frameflow.Core;
using Microsoft.Win32;

namespace Frameflow.Controls;
internal sealed record AutomationScript(string Path,string Source,MacroInfo[] Macros)
{
    public string Name=>Path.StartsWith('<')?"内置基础清理":System.IO.Path.GetFileName(Path);
    public int Count=>Macros.Length;
    public string Hash=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source)))[..16];
    public override string ToString()=>Name+" · "+Count+" 个宏 · "+Hash;
    internal static AutomationScript Builtin()=>new("<builtin>",AutomationEngine.Example,[new("去除所选行首尾空白","保留时间、样式与 ASS 标签"),new("切换所选行注释","批量切换对白与注释，可统一撤销")]);
}
internal sealed class AutomationManagerDialog:SubtitleToolDialog
{
    internal readonly ObservableCollection<AutomationScript> Scripts;
    internal readonly ListBox ScriptList=new(){Height=190};internal readonly TextBox Details;
    readonly CancellationTokenSource cancellation=new();bool busy,closed;
    internal AutomationManagerDialog(IEnumerable<AutomationScript> scripts):base("自动化管理器 · Lua 基础兼容",820,720)
    {
        Scripts=new(scripts);Hint("支持 Lua 宏注册、读取 ASS 行、修改已有对白字段、验证与返回选择。运行前预览、统一撤销。不是完整 Automation 4：暂不支持增删记录、样式写入、require/include、karaskel、交互式 Lua 对话框或导出过滤器。脚本没有文件、网络、系统命令接口，执行于受限独立进程（512 MB / 10 秒）。仍只应加载可信脚本。");
        ScriptList.ItemsSource=Scripts;Body.Children.Add(ScriptList);Details=Input(Body,"所选脚本 / 宏（当前窗口，关闭软件不自动加载外部脚本）","",true);Details.IsReadOnly=true;Details.Height=155;ScriptList.SelectionChanged+=(_,_)=>Describe();ScriptList.SelectedIndex=0;
        var actions=new WrapPanel();Body.Children.Add(actions);AsyncButton("添加 Lua 脚本…",async()=>{var p=new OpenFileDialog{Filter="Lua 脚本|*.lua",Multiselect=true};if(p.ShowDialog(this)==true)foreach(string path in p.FileNames)await LoadScriptAsync(path);},actions);
        AsyncButton("重新加载所选",async()=>{if(ScriptList.SelectedItem is AutomationScript item){if(item.Path.StartsWith('<'))return;await LoadScriptAsync(item.Path);}},actions);
        Action("移除所选（不删文件）",()=>{if(busy)throw new InvalidOperationException("请先等待当前操作。");if(ScriptList.SelectedItem is AutomationScript item)Scripts.Remove(item);},actions);
        AsyncButton("扫描脚本目录…",async()=>{var p=new OpenFolderDialog{Title="选择本窗口要加载的 Lua 目录"};if(p.ShowDialog(this)==true)await ScanAsync(p.FolderName);},actions);
        Action("导出示例脚本…",()=>{var p=new SaveFileDialog{Filter="Lua 脚本|*.lua",FileName="frameflow-example.lua"};if(p.ShowDialog(this)==true)ExportExample(p.FileName);},actions);
        Hint("重载成功才替换已加载版本；不会默默执行磁盘上新改的脚本。这里只管理加载列表，运行宏请返回自动化菜单。");Action("完成",()=>{if(busy)throw new InvalidOperationException("请等待加载完成，或取消对话框。");DialogResult=true;});Finish();Closed+=(_,_)=>{closed=true;cancellation.Cancel();};
    }
    void AsyncButton(string label,Func<Task> run,Panel panel){var b=new Button{Content=label};b.Click+=async(_,_)=>{if(busy)return;busy=true;try{Message.Text="正在隔离检查脚本…";await run();if(!closed)Message.Text="已加载到本次会话；点击完成生效。";}catch(Exception ex){if(!closed)Message.Text=ex.Message;}finally{busy=false;}};panel.Children.Add(b);}
    internal async Task LoadScriptAsync(string path)
    {
        path=System.IO.Path.GetFullPath(path);if(new FileInfo(path).Length>262144)throw new IOException("脚本文件超过 256 KB。");string source=File.ReadAllText(path);var result=await AutomationProcess.RunAsync(new(source,null,SubtitleDocument.New().Serialize(),[],0),cancellation.Token);if(closed)return;if(result.Macros.Length==0)throw new InvalidOperationException("没有注册可运行的宏。");var existing=Scripts.FirstOrDefault(s=>s.Path.Equals(path,StringComparison.OrdinalIgnoreCase));if(existing==null&&Scripts.Count>=32)throw new InvalidOperationException("当前窗口最多加载 32 个脚本。");var item=new AutomationScript(path,source,result.Macros);if(existing!=null)Scripts[Scripts.IndexOf(existing)]=item;else Scripts.Add(item);ScriptList.SelectedItem=item;Describe();
    }
    internal async Task ScanAsync(string folder){var paths=Directory.GetFiles(folder,"*.lua").Order(StringComparer.OrdinalIgnoreCase).ToArray();if(paths.Length>32)throw new InvalidOperationException("目录 Lua 文件超过 32 个，请逐个添加。");var errors=new List<string>();foreach(string path in paths)try{await LoadScriptAsync(path);}catch(Exception ex){errors.Add(System.IO.Path.GetFileName(path)+": "+ex.Message);}if(errors.Count>0)throw new InvalidOperationException(string.Join('\n',errors));}
    internal void ExportExample(string path)=>SubtitleDocument.AtomicSave(path,Encoding.UTF8.GetBytes(AutomationEngine.Example));
    void Describe(){if(ScriptList.SelectedItem is AutomationScript item)Details.Text=item.Path+"\nSHA256 前 16 位："+item.Hash+"\n"+string.Join('\n',item.Macros.Select(m=>m.Name+" — "+m.Description));else Details.Text="";}
}
internal sealed record MacroChange(int Number,string Before,string After,string TimeBefore,string TimeAfter,string Fields);
internal sealed class AutomationPreviewDialog:SubtitleToolDialog
{
    internal AutomationPreviewDialog(SubtitleDocument before,SubtitleDocument after,string log):base("自动化结果预览 · 尚未提交",940,660)
    {
        Hint("检查正文、时间及字段变化后应用。取消不会改动字幕，应用后可一次撤销。仅显示发生变化的行。");var changes=before.Rows.Zip(after.Rows).Where(p=>p.First.Serialize()!=p.Second.Serialize()).Select(p=>new MacroChange(p.First.Number,p.First.Text,p.Second.Text,p.First.StartText+" → "+p.First.EndText,p.Second.StartText+" → "+p.Second.EndText,string.Join("; ",p.First.Format.Where(f=>f is not "Text" and not "Start" and not "End" && p.First.Get(f)!=p.Second.Get(f)).Select(f=>f+": "+p.First.Get(f)+" → "+p.Second.Get(f)))+(p.First.Comment!=p.Second.Comment?$"; 注释: {p.First.Comment} → {p.Second.Comment}":""))).ToArray();var grid=new DataGrid{ItemsSource=changes,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,Height=300};foreach(var (label,path,width) in new[]{("行","Number",40),("原文","Before",210),("新文","After",210),("原时间","TimeBefore",170),("新时间","TimeAfter",170),("其他字段变化","Fields",200)})grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(path),Width=width});Body.Children.Add(grid);Hint($"共 {changes.Length} 行变化；横向滚动可查看所有字段变化；未知字段与非对白记录保留。");var logs=Input(Body,"脚本日志",log,true);logs.IsReadOnly=true;Action("应用宏结果",()=>DialogResult=true);Finish();
    }
}
