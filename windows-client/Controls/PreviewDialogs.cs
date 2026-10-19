using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Core;
using Frameflow.Media;
using Microsoft.Win32;
namespace Frameflow.Controls;

internal sealed class PreviewFontsDialog:SubtitleToolDialog
{
    internal readonly TextBox DirectoryBox;
    internal PreviewFontsDialog(string ass,string directory,string actual):base("字体检查 · 预览与压制共用",760,620)
    {
        Hint("不自动下载字体、不替换字幕中的字体名称。名称预检可能受别名影响，实际选用字体以 libass 日志为准。视频字体附件需要先自行导出到字体文件夹。");
        DirectoryBox=Input(Body,"附加字体文件夹（也会传给压制与对照预览）",directory);
        Action("选择文件夹…",()=>{var p=new OpenFolderDialog{Title="选择附加字体文件夹"};if(p.ShowDialog(this)==true)DirectoryBox.Text=p.FolderName;},Body);
        var names=BurnInEncoder.ReferencedFonts(ass).ToArray();
        var installed=new HashSet<string>(Fonts.SystemFontFamilies.SelectMany(f=>f.FamilyNames.Values.Append(f.Source)),StringComparer.OrdinalIgnoreCase);
        Hint("引用字体\n"+string.Join('\n',names.Select(n=>n+" — "+(installed.Contains(n.TrimStart('@'))?"本机名称匹配":"本机名称未匹配；可能使用附加字体或替代字体"))));
        var log=new TextBox{Text=string.Join('\n',actual.Split('\n').Where(l=>l.Contains("fontselect",StringComparison.OrdinalIgnoreCase)||l.Contains("Glyph",StringComparison.OrdinalIgnoreCase)||l.StartsWith("FONT:"))),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Hint("实际渲染记录 · fontselect 的箭头右侧是引擎实际选用字体；Glyph 表示缺字 / 回退信息。没有记录时先播放相应字幕。不会把“名称匹配”当作无缺字保证。");Body.Children.Add(log);
        Action("应用并重新预览",()=>{if(DirectoryBox.Text.Trim().Length>0&&!Directory.Exists(DirectoryBox.Text.Trim())){Message.Text="文件夹不存在。";return;}DialogResult=true;});Action("取消",()=>Close());
    }
}
internal sealed class PreviewCompareDialog:SubtitleToolDialog
{
    internal readonly Image ReferenceImage=new(){Stretch=Stretch.Uniform},EncodedImage=new(){Stretch=Stretch.Uniform};
    internal readonly Button GenerateButton,PlayResultButton;internal readonly TextBlock Details=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)};
    internal Task? Running;internal EncodeResult? Result;internal string? LastError;internal string? ReferencePath,EncodedPath;
    readonly string ass,video,fonts;readonly double time;readonly AssPreviewWorkspace work;CancellationTokenSource? cancel;bool busy,closing,closed;
    internal PreviewCompareDialog(string script,string media,string fontDirectory,double position):base("成片对照 · 同一字幕与字体快照",1140,750)
    {
        ass=script;video=media;fonts=fontDirectory;time=position;work=new AssPreviewWorkspace(script,"",CancellationToken.None);
        Hint("左侧：在源视频帧上使用编辑器同款 libass 渲染；右侧：真正压制 10 秒 MP4 后重新解码的首帧。两侧共用当前字幕、字体目录与绝对时间。H.264 压缩和颜色转换可能带来细微像素差异，不承诺逐像素相同。");
        Hint("对照起点 "+Timecode.Ass(position)+" · 使用默认均衡画质 / 自动显卡（失败回退 CPU） · 当前字幕快照不会写回原文件。");
        var grid=new Grid{Height=365};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());
        for(int i=0;i<2;i++){var panel=new DockPanel{Margin=new Thickness(6)};var label=new TextBlock{Text=i==0?"编辑渲染 · libass":"实际压制 · H.264",Margin=new Thickness(4,0,4,8)};DockPanel.SetDock(label,Dock.Top);panel.Children.Add(label);panel.Children.Add(i==0?ReferenceImage:EncodedImage);Grid.SetColumn(panel,i);grid.Children.Add(panel);}Body.Children.Add(grid);Body.Children.Add(Details);Body.Children.Add(Message);
        GenerateButton=Action("生成对照（10 秒）",()=>Running=GenerateAsync());PlayResultButton=Action("播放实际成片",()=>{if(Result!=null)Process.Start(new ProcessStartInfo(Result.Output){UseShellExecute=true});});PlayResultButton.IsEnabled=false;
        Action("关闭 / 取消",()=>{if(busy){closing=true;cancel?.Cancel();}else Close();});
        Closing+=(_,e)=>{if(busy){e.Cancel=true;closing=true;cancel?.Cancel();}};Closed+=(_,_)=>{closed=true;work.Dispose();};
    }
    internal static BitmapSource ReadImage(string path){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(Path.GetFullPath(path));b.EndInit();b.Freeze();return b;}
    internal async Task GenerateAsync()
    {
        if(busy)return;busy=true;GenerateButton.IsEnabled=PlayResultButton.IsEnabled=false;LastError=null;cancel=new();
        try
        {
            string id=Guid.NewGuid().ToString("N");ReferencePath=Path.Combine(work.DirectoryPath,id+"-edit.png");EncodedPath=Path.Combine(work.DirectoryPath,id+"-encoded.png");
            Details.Text="正在生成实际压制片段…原文件保持不变。";
            Result=await BurnInEncoder.EncodeAsync(new(video,Path.Combine(work.DirectoryPath,id+".mp4"),ass,FontDirectory:fonts,PreviewStart:time),new Progress<EncodeProgress>(p=>{if(!closed)Details.Text=p.Text;}),cancel.Token);
            await Task.Run(async()=>{using var reference=new AssPreviewWorkspace(ass,fonts,cancel.Token);await reference.ReferenceFrameAsync(video,time,ReferencePath,cancel.Token);await AssPreviewWorkspace.DecodeFrameAsync(Result.Output,EncodedPath,cancel.Token);},cancel.Token);
            ReferenceImage.Source=ReadImage(ReferencePath);EncodedImage.Source=ReadImage(EncodedPath);Details.Text="同一首帧已就绪 · 实际编码器："+Result.Encoder.Label+"\n可播放实际成片检查动画 / 卡拉 OK / 遮罩。";
            Message.Foreground=Appearance.Brush("MutedBrush");Message.Text="实际字体选择：\n"+(Result.FontLog.Length==0?"此片段没有字体选择记录（例如只有绘图或没有字幕）。":Result.FontLog);PlayResultButton.IsEnabled=true;
        }
        catch(Exception e){LastError=e.Message;Message.Foreground=Appearance.Brush("ErrorBrush");Message.Text=e.Message;Details.Text="对照未完成；原视频和字幕未修改。";}
        finally{busy=false;GenerateButton.IsEnabled=true;cancel.Dispose();cancel=null;if(closing)Close();}
    }
}
