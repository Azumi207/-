using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Frameflow.Media;
using Frameflow.Core;
using Microsoft.Win32;

namespace Frameflow.Controls;
internal sealed class BurnInDialog:SubtitleToolDialog
{
    internal readonly TextBox InputVideo,OutputVideo,FontDirectory,PreviewTime;
    internal readonly ComboBox Quality,Acceleration;
    internal readonly TextBlock EncoderLabel=new(){Text="自动模式：开始时实际试编码，无需选择显卡或填写参数。",TextWrapping=TextWrapping.Wrap,Foreground=Frameflow.Core.Appearance.Brush("MutedBrush"),Margin=new Thickness(3,5,3,5)};
    internal readonly CheckBox KeepAudio=new(){Content="保留原视频的第一条音轨（AAC，不使用编辑器独立音轨）",IsChecked=true,Margin=new Thickness(3,8,3,8)};
    internal readonly ProgressBar Progress=new(){Minimum=0,Maximum=100,Height=17,Margin=new Thickness(3,8,3,8)};
    internal readonly TextBlock StatusLabel=new(){Text="就绪 · 先预览 10 秒，可检查实际字体与排版。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,7,3,7)};
    internal readonly Button StartButton,PreviewButton,CancelButton,OpenButton,FolderButton;
    readonly List<Control> inputs=[];readonly string ass;CancellationTokenSource? cancellation;bool closed,closeAfterCancel;
    internal bool Busy {get;private set;}
    internal EncodeResult? LastResult {get;private set;}
    internal string? LastError {get;private set;}
    internal Task? Running {get;private set;}
    internal BurnInDialog(string snapshot,string? video,double current,string initialFontDirectory=""):base("压制字幕到视频 · 保留 ASS 样式",810,820)
    {
        ass=snapshot;Hint("把当前字幕烧录到画面，任何播放器都能看到。采用 FFmpeg + libass，与精确 ASS 预览共用渲染引擎与字体规则。输出 MP4（H.264 / AAC），原视频、字幕不覆盖。字幕是打开此窗口时的快照，包含已提交的未保存编辑。");
        InputVideo=Input(Body,"① 视频文件",video??"");OutputVideo=Input(Body,"② 输出 MP4",video!=null&&File.Exists(video)?BurnInEncoder.SuggestOutput(video):"");
        var paths=new WrapPanel();Body.Children.Add(paths);inputs.Add(Action("选择视频…",()=>{var p=new OpenFileDialog{Filter="视频|*.mp4;*.mkv;*.avi;*.mov;*.m4v;*.wmv;*.webm|所有文件|*.*"};if(p.ShowDialog(this)==true){InputVideo.Text=p.FileName;OutputVideo.Text=BurnInEncoder.SuggestOutput(p.FileName);}},paths));inputs.Add(Action("选择输出位置…",()=>{var p=new SaveFileDialog{Filter="MP4 视频|*.mp4",FileName=OutputVideo.Text.Length>0?Path.GetFileName(OutputVideo.Text):"字幕视频.mp4",OverwritePrompt=false};if(p.ShowDialog(this)==true)OutputVideo.Text=p.FileName;},paths));
        Quality=Choice(Body,"③ 画质",["推荐 · 清晰与体积平衡","更高画质","较小文件"]);
        Acceleration=Choice(Body,"加速方式",["自动（优先显卡，失败回退 CPU）","仅 CPU（兼容优先）"]);Body.Children.Add(EncoderLabel);Hint("仅加速视频编码；字幕仍由 CPU 上的 libass 渲染。不同编码器的体积/画质不完全相同，建议先预览。");Body.Children.Add(KeepAudio);
        var advanced=new StackPanel();var expander=new Expander{Header="字体与预览设置（可选）",Content=advanced,Margin=new Thickness(3,6,3,8)};Body.Children.Add(expander);
        FontDirectory=Input(advanced,"附加字体文件夹（留空使用本机字体；TTF / OTF / TTC）",initialFontDirectory);inputs.Add(Action("选择字体文件夹…",()=>{var p=new OpenFolderDialog{Title="选择字幕使用的字体目录"};if(p.ShowDialog(this)==true)FontDirectory.Text=p.FolderName;},advanced));PreviewTime=Input(advanced,"10 秒预览起点（秒或 h:mm:ss.cc）",Timecode.Ass(Math.Max(0,current)));
        string[] fonts=BurnInEncoder.ReferencedFonts(ass).ToArray();var installed=new HashSet<string>(Fonts.SystemFontFamilies.SelectMany(f=>f.FamilyNames.Values.Append(f.Source)),StringComparer.OrdinalIgnoreCase);var missing=fonts.Where(f=>!installed.Contains(f.TrimStart('@'))).ToArray();
        var fontHint=new TextBlock{Text="字幕字体："+string.Join("、",fonts)+(missing.Length>0?"\n本机名称未匹配："+string.Join("、",missing)+"。可提供字体目录，务必先预览；缺字/替代字体可能改变排版。":"\n字体版本或复杂标签仍可能存在渲染差异，建议先预览。"),TextWrapping=TextWrapping.Wrap,Foreground=missing.Length>0?Frameflow.Core.Appearance.Brush("WarningBrush"):Frameflow.Core.Appearance.Brush("MutedBrush"),Margin=new Thickness(3,6,3,8)};Body.Children.Add(fontHint);
        Hint("保留原尺寸与时间戳节奏；奇数宽高最多补 1 像素。当前仅 SDR，HDR 会拒绝而不是自动偏色转换。音轨取源视频第一条；不复制软字幕、附件和其他音轨。视频中的字体附件请先导出到附加字体目录。");Body.Children.Add(Progress);Body.Children.Add(StatusLabel);Body.Children.Add(Message);
        var resultActions=new WrapPanel();Body.Children.Add(resultActions);OpenButton=Action("播放结果",()=>{if(LastResult!=null&&File.Exists(LastResult.Output))Process.Start(new ProcessStartInfo(LastResult.Output){UseShellExecute=true});},resultActions);FolderButton=Action("打开输出文件夹",()=>{if(LastResult!=null)Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+LastResult.Output+"\""){UseShellExecute=true});},resultActions);OpenButton.IsEnabled=FolderButton.IsEnabled=false;
        PreviewButton=Action("预览 10 秒",()=>Running=StartAsync(true));StartButton=Action("开始压制",()=>Running=StartAsync(false));CancelButton=Action("关闭",()=>{if(Busy){cancellation?.Cancel();StatusLabel.Text="正在停止编码并清理临时文件…";}else Close();});
        inputs.AddRange([InputVideo,OutputVideo,FontDirectory,PreviewTime,Quality,Acceleration,KeepAudio]);Closing+=(_,e)=>{if(Busy){e.Cancel=true;closeAfterCancel=true;cancellation?.Cancel();StatusLabel.Text="正在安全取消，完成清理后关闭…";}};Closed+=(_,_)=>{closed=true;cancellation?.Cancel();};
    }
    internal async Task StartAsync(bool preview)
    {
        if(Busy)return;Busy=true;LastError=null;LastResult=null;OpenButton.IsEnabled=FolderButton.IsEnabled=false;StartButton.IsEnabled=PreviewButton.IsEnabled=false;foreach(var c in inputs)c.IsEnabled=false;CancelButton.Content="取消压制";Progress.Value=0;Message.Text="";Message.Foreground=Frameflow.Core.Appearance.Brush("MutedBrush");EncoderLabel.Text=Acceleration.SelectedIndex==0?"等待试编码检测…":"已选择仅 CPU。";cancellation=new CancellationTokenSource();
        try
        {
            var request=new EncodeRequest(InputVideo.Text.Trim(),OutputVideo.Text.Trim(),ass,Quality.SelectedIndex,KeepAudio.IsChecked==true,FontDirectory.Text.Trim(),preview?Timecode.Parse(PreviewTime.Text):null,(EncodingAcceleration)Acceleration.SelectedIndex);
            if(preview){string full=Path.GetFullPath(request.Output);request=request with{Output=BurnInEncoder.SuggestOutput(full,true)};}
            StatusLabel.Text="正在检查视频与压制引擎…";LastResult=await BurnInEncoder.EncodeAsync(request,new Progress<EncodeProgress>(p=>{if(!closed&&Busy){Progress.Value=p.Fraction*100;StatusLabel.Text=p.Text;if(p.EncoderStatus!=null)EncoderLabel.Text=p.EncoderStatus;}}),cancellation.Token);
            Progress.Value=100;EncoderLabel.Text=LastResult.AccelerationNotice+"\n实际编码器："+LastResult.Encoder.Label;StatusLabel.Text=(preview?"预览片段已生成，请点「播放结果」检查。":"压制完成，已验证输出编码、时长与音轨。")+"\n"+LastResult.Output;Message.Text="字体选择与详细命令见日志：\n"+LastResult.LogPath;OpenButton.IsEnabled=FolderButton.IsEnabled=true;
        }
        catch(Exception e){Message.Foreground=Frameflow.Core.Appearance.Brush("ErrorBrush");LastError=e.Message;StatusLabel.Text=e is OperationCanceledException||e is EncodeFailure{Canceled:true}?"已取消，未生成正式输出。":"未完成压制，原文件未改动。";Message.Text=e.Message;}
        finally{Busy=false;StartButton.IsEnabled=PreviewButton.IsEnabled=true;foreach(var c in inputs)c.IsEnabled=true;CancelButton.Content="关闭";cancellation.Dispose();cancellation=null;if(closeAfterCancel)Close();}
    }
}
