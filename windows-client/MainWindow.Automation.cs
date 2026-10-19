using System.Windows;
using System.Windows.Controls;
using Frameflow.Controls;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    List<AutomationScript> automationScripts=[AutomationScript.Builtin()];bool automationBusy;
    void AutomationManagerClick(object sender,RoutedEventArgs e)=>Guard(()=>{var dialog=new AutomationManagerDialog(automationScripts){Owner=this};if(dialog.ShowDialog()==true){automationScripts=dialog.Scripts.ToList();Status.Text=$"当前窗口加载 {automationScripts.Count} 个脚本。";}});
    async void ReloadAutomationClick(object sender,RoutedEventArgs e)
    {
        if(automationBusy)return;automationBusy=true;var errors=new List<string>();try{for(int i=0;i<automationScripts.Count;i++){var old=automationScripts[i];if(old.Path.StartsWith('<'))continue;try{if(new FileInfo(old.Path).Length>262144)throw new IOException("脚本过大");string source=File.ReadAllText(old.Path);var result=await AutomationProcess.RunAsync(new(source,null,SubtitleDocument.New().Serialize(),[],0),CancellationToken.None);if(result.Macros.Length==0)throw new InvalidOperationException("没有宏");automationScripts[i]=new(old.Path,source,result.Macros);}catch(Exception ex){errors.Add(old.Name+": "+ex.Message);}}Status.Text=errors.Count==0?"本窗口脚本已重新加载。":"部分脚本重载失败，保留旧版本："+string.Join("；",errors);}finally{automationBusy=false;}
    }
    void AutomationMenuOpened(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(sender,e.Source))return;while(AutomationMenu.Items.Count>3)AutomationMenu.Items.RemoveAt(3);foreach(var script in automationScripts){var group=new MenuItem{Header=script.Name,ToolTip=script.Path+" · "+script.Hash};foreach(var macro in script.Macros){var item=new MenuItem{Header=macro.Name,ToolTip=macro.Description,IsEnabled=!automationBusy&&SelectedRows().Length>0};item.Click+=async(_,_)=>{await RunAutomationMacroAsync(script,macro.Name);};group.Items.Add(item);}AutomationMenu.Items.Add(group);}
    }
    async Task RunAutomationMacroAsync(AutomationScript script,string name)
    {
        if(automationBusy||!ApplyEditor())return;Pause();automationBusy=true;string baseline=document.Serialize();var selection=SelectedRows().Select(r=>document.Records.FindIndex(x=>x.Row==r)+1).ToArray();int active=document.Records.FindIndex(x=>x.Row==selected)+1;var before=SubtitleTools.Copy(document);using var cancellation=new CancellationTokenSource();
        var completion=new TaskCompletionSource();var progress=new AutomationProgressDialog(cancellation){Owner=this};AutomationResult? result=null;Exception? failure=null;_ =Dispatcher.BeginInvoke(new Action(async()=>{try{result=await AutomationProcess.RunAsync(new(script.Source,name,baseline,selection,active),cancellation.Token);}catch(Exception ex){failure=ex;}finally{completion.TrySetResult();if(progress.IsVisible)progress.Close();}}));
        try{progress.ShowDialog();await completion.Task;if(result==null){Status.Text=failure is OperationCanceledException?"宏已取消，字幕未修改。":"宏未提交："+failure?.Message;return;}if(document.Serialize()!=baseline)throw new InvalidOperationException("执行期间字幕已改变，结果未应用。");var copy=ValidateAutomationResult(before,result);var dialog=new AutomationPreviewDialog(before,copy,result.Log){Owner=this};if(dialog.ShowDialog()==true){var rows=result.Selected.Select(i=>copy.Records.ElementAtOrDefault(i-1)?.Row).Where(r=>r!=null).Cast<AssRow>().ToArray();CommitSubtitleCopy(copy,rows,"自动化宏已应用："+name);}}
        catch(Exception ex){Status.Text="宏未提交："+ex.Message;}finally{automationBusy=false;}
    }
    static SubtitleDocument ValidateAutomationResult(SubtitleDocument before,AutomationResult result)
    {
        if(result.Ass==null)throw new InvalidOperationException("宏未返回字幕。");var copy=SubtitleDocument.Parse(result.Ass);if(copy.Records.Count!=before.Records.Count||copy.Rows.Count()!=before.Rows.Count()||copy.Warnings.Count>0)throw new InvalidOperationException("宏结果结构变化或损坏，未应用。");for(int i=0;i<copy.Records.Count;i++){var old=before.Records[i];var record=copy.Records[i];if(old.Row==null){if(record.Serialize()!=old.Serialize())throw new InvalidOperationException("宏改动了非对白记录，未应用。");}else{if(record.Row==null||!old.Row.Format.SequenceEqual(record.Row.Format))throw new InvalidOperationException("宏改动了事件格式，未应用。");record.Row.Note=old.Row.Note;record.Row.Reviewed=old.Row.Serialize()==record.Row.Serialize()&&old.Row.Reviewed;foreach(string field in old.Row.Format.Where(f=>!new[]{"Layer","Start","End","Style","Name","Actor","MarginL","MarginR","MarginV","Effect","Text"}.Contains(f,StringComparer.OrdinalIgnoreCase)))if(old.Row.Get(field)!=record.Row.Get(field))throw new InvalidOperationException("宏改动了未知字段。");}}return copy;
    }
}
