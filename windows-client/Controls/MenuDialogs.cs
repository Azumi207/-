using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Frameflow.Controls;
public sealed class SelectivePasteDialog:Window
{
    readonly Dictionary<string,CheckBox> choices=[];
    public IReadOnlySet<string> Fields=>choices.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToHashSet();
    public SelectivePasteDialog(int copied,int targets)
    {
        Title="选择性粘贴";Width=460;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(16)};Content=panel;
        panel.Children.Add(new TextBlock{Text=$"已复制 {copied} 行 → 目标 {targets} 行",FontSize=16,Margin=new Thickness(0,0,0,10)});
        panel.Children.Add(new TextBlock{Text="只覆盖勾选字段，不插入新行。复制 1 行可应用到多行；否则行数须相同。",TextWrapping=TextWrapping.Wrap});
        var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Margin=new Thickness(0,10,0,10)};panel.Children.Add(grid);
        foreach(var (key,label) in new[]{("Text","字幕正文"),("Start","开始时间"),("End","结束时间"),("Style","样式"),("Actor","说话人"),("Layer","层"),("MarginL","左边距"),("MarginR","右边距"),("MarginV","垂直边距"),("Effect","特效字段"),("Comment","注释状态")}){var c=new CheckBox{Content=label,IsChecked=key=="Text"};choices.Add(key,c);grid.Children.Add(c);}
        panel.Children.Add(new TextBlock{Text="正文包含 ASS 行内样式/位置标签。批注不会覆盖；同名共享样式沿用目标项目。",TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.DimGray});
        var message=new TextBlock{Foreground=System.Windows.Media.Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};panel.Children.Add(message);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};panel.Children.Add(buttons);
        var cancel=new Button{Content="取消",IsCancel=true};var ok=new Button{Content="粘贴所选字段",IsDefault=true};ok.Click+=(_,_)=>{if(Fields.Count==0){message.Text="请至少勾选一个字段。";return;}if(copied!=1&&copied!=targets){message.Text="行数不匹配：请重新选择目标行，或只复制一行。";return;}DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(ok);
    }
}
public sealed class FindDialog:Window
{
    readonly MainWindow host;readonly TextBox query=new(){MinWidth=270};readonly ComboBox scope=new(){ItemsSource=new[]{"字幕正文","说话人","样式","特效","校对批注"},SelectedIndex=0};
    readonly CheckBox matchCase=new(){Content="区分大小写"},wrap=new(){Content="循环查找",IsChecked=true};readonly TextBlock result=new(){Margin=new Thickness(3,10,3,0),TextWrapping=TextWrapping.Wrap};
    public FindDialog(MainWindow owner)
    {
        host=owner;Owner=owner;Title="查找字幕";Width=470;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var p=new StackPanel{Margin=new Thickness(14)};Content=p;p.Children.Add(new TextBlock{Text="查找内容（普通文本）"});p.Children.Add(query);p.Children.Add(new TextBlock{Text="查找范围"});p.Children.Add(scope);
        var options=new StackPanel{Orientation=Orientation.Horizontal};options.Children.Add(matchCase);options.Children.Add(wrap);p.Children.Add(options);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};p.Children.Add(buttons);
        var prev=new Button{Content="上一个 · Shift+F3"};prev.Click+=(_,_)=>Search(true);var next=new Button{Content="下一个 · F3",IsDefault=true};next.Click+=(_,_)=>Search(false);var close=new Button{Content="关闭"};close.Click+=(_,_)=>Close();buttons.Children.Add(prev);buttons.Children.Add(next);buttons.Children.Add(close);p.Children.Add(result);
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}else if(e.Key==Key.F3){Search(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));e.Handled=true;}};
        Loaded+=(_,_)=>FocusQuery();
    }
    public void FocusQuery(){query.Focus();query.SelectAll();}
    public void Search(bool backwards)=>result.Text=host.FindText(query.Text,scope.SelectedItem?.ToString()??"字幕正文",matchCase.IsChecked==true,wrap.IsChecked==true,backwards);
}
