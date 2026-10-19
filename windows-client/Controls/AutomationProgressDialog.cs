using System.Windows;
using System.Windows.Controls;
namespace Frameflow.Controls;
internal sealed class AutomationProgressDialog:Window
{
    internal AutomationProgressDialog(CancellationTokenSource cancellation){Title="正在运行字幕宏";Width=420;Height=165;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;var panel=new StackPanel{Margin=new Thickness(18)};panel.Children.Add(new TextBlock{Text="脚本在独立进程运行，结束后先预览，不自动改字幕。",TextWrapping=TextWrapping.Wrap});var cancel=new Button{Content="取消运行",Margin=new Thickness(0,15,0,0)};cancel.Click+=(_,_)=>cancellation.Cancel();panel.Children.Add(cancel);Content=panel;Closing+=(_,_)=>cancellation.Cancel();}
}
