using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Frameflow.Core;
namespace Frameflow.Controls;
internal sealed class MediaInfoDialog:SubtitleToolDialog
{
    internal MediaInfoDialog(string text):base("视频详情",620,440){var field=Input(Body,"当前媒体",text,true);field.IsReadOnly=true;field.MinHeight=290;Action("关闭",()=>DialogResult=false);}
}
internal sealed class DummyVideoDialog:SubtitleToolDialog
{
    internal readonly TextBox VideoWidth,VideoHeight,Fps,Seconds,Color;
    internal readonly CheckBox Checker=new(){Content="棋盘背景",IsChecked=true};
    internal DummyVideoDialog():base("空白视频 · 虚拟画布",510,490){Hint("不生成巨大视频文件。用于排版与字幕预览；虚拟播放时钟，不含音轨。");VideoWidth=Input(Body,"宽度","1920");VideoHeight=Input(Body,"高度","1080");Fps=Input(Body,"帧率","24");Seconds=Input(Body,"时长（秒）","60");Color=Input(Body,"背景颜色 #RRGGBB","#334155");Body.Children.Add(Checker);Action("创建",()=>{_=Values();DialogResult=true;});Finish();}
    internal (int Width,int Height,double Fps,double Duration,Color Color,bool Checker) Values(){int w=int.Parse(VideoWidth.Text),h=int.Parse(VideoHeight.Text);double f=double.Parse(Fps.Text,CultureInfo.InvariantCulture),d=Timecode.Parse(Seconds.Text);if(w<16||h<16||w>7680||h>4320)throw new FormatException("画布尺寸范围 16～7680 × 16～4320。");_=FrameTimeline.Cfr(f,d);return(w,h,f,d,(Color)ColorConverter.ConvertFromString(Color.Text),Checker.IsChecked==true);}
}
internal sealed class JumpVideoDialog:SubtitleToolDialog
{
    internal readonly TextBox Value;internal readonly ComboBox Unit;readonly FrameTimeline? frames;
    internal double Position=>Unit.SelectedIndex==0?Timecode.Parse(Value.Text):frames==null?throw new InvalidOperationException("跳到帧号需要加载帧时间表。"):frames.Starts[CheckedFrame()];
    int CheckedFrame(){int i=int.Parse(Value.Text,CultureInfo.InvariantCulture);if(i<0||i>=frames!.Count)throw new FormatException("帧号超出范围（从 0 开始）。");return i;}
    internal JumpVideoDialog(double current,FrameTimeline? table):base("跳转到视频位置",490,330){frames=table;Hint("按时间或帧号定位。真实视频定位仍受 Windows 解码器限制，不保证显示帧精确。");Unit=Choice(Body,"单位",["时间（秒 / h:mm:ss.cc）","帧号（0 起始）"]);Value=Input(Body,"位置",Timecode.Ass(current));Action("跳转",()=>{_=Position;DialogResult=true;});Finish();}
}
internal sealed class PreferencesDialog:SubtitleToolDialog
{
    internal readonly TextBox Volume,Gain,Autosave,RowHeight;internal readonly CheckBox Crosshair,Follow,Linked,Toolbar;internal readonly ComboBox Tags,MenuLanguage;
    readonly string layout;
    internal DisplayOptions Options=>new(layout,Tags.SelectedIndex,Toolbar.IsChecked==true,Crosshair.IsChecked==true,Follow.IsChecked==true,Linked.IsChecked==true,double.Parse(Volume.Text,CultureInfo.InvariantCulture)/100,double.Parse(Gain.Text,CultureInfo.InvariantCulture),int.Parse(Autosave.Text),double.Parse(RowHeight.Text,CultureInfo.InvariantCulture),MenuLanguage.SelectedIndex==1?"en":"zh");
    internal PreferencesDialog(DisplayOptions current):base("选项 · 显示与播放",590,700){layout=current.Layout;Hint("应用到当前窗口，并保存为后续新窗口的默认设置。仅一级菜单提供中英文；正文、工具对话框仍为中文。不会更改 Aegisub 配置。");Volume=Input(Body,"播放音量 %（0～100）",(current.Volume*100).ToString(CultureInfo.InvariantCulture));Gain=Input(Body,"波形显示增益（0.1～10，不改变音量）",current.WaveGain.ToString(CultureInfo.InvariantCulture));Autosave=Input(Body,"恢复副本间隔（15～600 秒）",current.AutosaveSeconds.ToString());RowHeight=Input(Body,"字幕表行高（22～48）",current.RowHeight.ToString(CultureInfo.InvariantCulture));
        CheckBox Flag(string text,bool v){var c=new CheckBox{Content=text,IsChecked=v,Margin=new Thickness(3,7,3,3)};Body.Children.Add(c);return c;}Crosshair=Flag("显示悬停十字坐标",current.Crosshair);Follow=Flag("波形跟随播放",current.Follow);Linked=Flag("视频 / 波形联动",current.Linked);Toolbar=Flag("显示工具栏",current.Toolbar);Tags=Choice(Body,"字幕表 ASS 标签显示",["完整标签","简化为 {…}","隐藏标签"]);Tags.SelectedIndex=current.Tags;MenuLanguage=Choice(Body,"一级菜单语言",["简体中文","English (top-level menus only)"]);MenuLanguage.SelectedIndex=current.MenuLanguage=="en"?1:0;Action("应用并保存",()=>{Options.Validate();DialogResult=true;});Finish();}
}
