using System.Windows;
using System.Windows.Controls;
using Frameflow.Controls;
using Frameflow.Core;

namespace Frameflow;
public partial class MainWindow
{
    void CommitSubtitleCopy(SubtitleDocument copy,AssRow[] selection,string description)
    {
        if(copy.Serialize()==document.Serialize()){Status.Text="未更改字幕内容。";return;}
        PushUndo();document=copy;draftDirty=false;MarkDirty();RefreshAll(selection.FirstOrDefault()?.Number-1??0);SelectRows(selection);Status.Text=description+"；可撤销。";
    }
    void RunSubtitleOperation(string op)
    {
        if(!ApplyEditor())return;
        if((op.EndsWith("-video")||op=="split-before"||op=="split-after")&&(!VideoAvailable))throw new InvalidOperationException("请先打开视频；按播放点操作需要视频时钟。");
        Pause();var copy=SubtitleTools.Copy(document);var rows=copy.Rows.ToArray();var targets=SelectedRows().Select(r=>rows[r.Number-1]).ToArray();
        var result=SubtitleTools.Execute(copy,targets,op,PlaybackPosition);CommitSubtitleCopy(copy,result,"已完成字幕行操作");
    }
    void SubtitleOperationClick(object sender,RoutedEventArgs e)=>Guard(()=>RunSubtitleOperation((string)((MenuItem)sender).Tag));
    void StyleManagerClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEditor())return;Guard(()=>{int index=selected?.Number-1??0;var dialog=new StyleManagerDialog(document){Owner=this};if(dialog.ShowDialog()==true)CommitSubtitleCopy(dialog.Working,dialog.Working.Rows.Skip(index).Take(1).ToArray(),"样式管理器更改已提交");});
    }
    void ShowLineAssistant(string mode)
    {
        if(!ApplyEditor())return;int index=selected?.Number-1??0;var selection=SelectedRows();Pause();
        var dialog=new LineAssistantDialog(document,index,mode,(i,play)=>{var row=document.Rows.ElementAt(i);SelectRow(row);if(play)PlayRange(row.Start,row.End);}){Owner=this};
        try{if(dialog.ShowDialog()==true)CommitSubtitleCopy(dialog.Working,dialog.Working.Rows.Skip(dialog.RowIndex).Take(1).ToArray(),"助手更改已提交");else SelectRows(selection);}finally{Pause();}
    }
    void StylingAssistantClick(object sender,RoutedEventArgs e)=>Guard(()=>ShowLineAssistant("style"));
    void TranslationAssistantClick(object sender,RoutedEventArgs e)=>Guard(()=>ShowLineAssistant("translate"));
    void SpellcheckClick(object sender,RoutedEventArgs e)=>Guard(()=>ShowLineAssistant("spell"));
    void ResampleClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEditor())return;Guard(()=>{var dialog=new ResampleDialog(document){Owner=this};if(dialog.ShowDialog()!=true)return;var copy=SubtitleTools.Copy(document);SubtitleTools.Resample(copy,dialog.TargetWidth,dialog.TargetHeight);CommitSubtitleCopy(copy,copy.Rows.Skip(selected?.Number-1??0).Take(1).ToArray(),"脚本与静态排版已等比例重采样");});
    }
    void InsertDrawing(string text)
    {
        var copy=SubtitleTools.Copy(document);var row=copy.Add(selected?.Start??0,selected?.End??5,text,copy.Rows.ElementAtOrDefault(selected?.Number-1??-1));CommitSubtitleCopy(copy,[row],"已插入独立 ASS 多边形行");Seek(row.Start);
    }
    void DrawingClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEditor())return;Guard(()=>{var dialog=new DrawingDialog(document.PlayResX,document.PlayResY){Owner=this};if(dialog.ShowDialog()==true)InsertDrawing(dialog.DrawingText);});
    }
    void SelectLinesClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEditor())return;Guard(()=>{var dialog=new SelectLinesDialog(document){Owner=this};if(dialog.ShowDialog()!=true)return;ApplyLineSelection(dialog.Matches(document),dialog.Mode.SelectedIndex);});
    }
    void ApplyLineSelection(AssRow[] matches,int mode)
    {
        var current=SelectedRows();var result=mode switch{1=>current.Union(matches),2=>current.Except(matches),3=>current.Intersect(matches),_=>matches.AsEnumerable()};SelectRows(result.OrderBy(r=>r.Number).ToArray());Status.Text=$"已选择 {SubtitleGrid.SelectedItems.Count} 行。";
    }
    void SelectAllLinesClick(object sender,RoutedEventArgs e){if(ApplyEditor())ApplyLineSelection(document.Rows.ToArray(),0);}
    void SubtitleMenuOpened(object sender,RoutedEventArgs e)
    {
        if(!ReferenceEquals(e.Source,sender))return;int count=SelectedRows().Length;bool video=VideoAvailable;
        void Update(MenuItem item)
        {
            if(item.Tag is string tag)item.IsEnabled=tag.StartsWith("insert-")?(!tag.EndsWith("-video")||video):tag=="split-before"||tag=="split-after"?count>0&&video:tag=="swap"?count==2:tag.StartsWith("join-")||tag=="recombine"?count>=2:tag.StartsWith("sort-all")?document.Rows.Any():count>0;
            foreach(var child in item.Items.OfType<MenuItem>())Update(child);
        }
        Update((MenuItem)sender);
    }
}
