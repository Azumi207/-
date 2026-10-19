using System.IO;
using System.Windows;
using Frameflow.Controls;
using Frameflow.Team;

namespace Frameflow;
public partial class MainWindow
{
    TeamClient? teamClient;
    void TeamHubClick(object sender,RoutedEventArgs e)=>Guard(()=>
    {
        if(!ApplyEditor())return;Pause();
        if(teamClient==null){teamClient=new();Closed+=(_,_)=>teamClient.Dispose();}
        var work=TeamWork.Read(subtitlePath);
        var dialog=CreateTeamDialog(teamClient,work?.Project);dialog.ShowDialog();
    });
    TeamHubDialog CreateTeamDialog(TeamClient client,string? preferred=null)=>new(client,
        p=>
        {
            var work=TeamWork.Read(subtitlePath)??throw new InvalidOperationException("请先从项目大厅下载并打开团队工作字幕，不能把未关联的当前字幕上传到此项目。");
            client.ValidateWork(work,p);if(!ApplyEditor())throw new InvalidOperationException("请先修正当前字幕输入。");SaveTo(work.Subtitle);return(work,document.Serialize());
        },
        work=>
        {
            OpenSubtitle(work.Subtitle);
            if(!string.Equals(subtitlePath,work.Subtitle,StringComparison.OrdinalIgnoreCase))return false;
            OpenMedia(work.Video);Status.Text="团队工作副本已打开；可离线编辑，完成后从「团队」菜单提交。";return true;
        },
        work=>Status.Text="已同步团队字幕版本；本地字幕与关联记录已保留。",preferred){Owner=this};
}
