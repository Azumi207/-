using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Frameflow.Core;
using Frameflow.Controls;
using Microsoft.Win32;

namespace Frameflow;
public partial class MainWindow
{
    internal IRowClipboard RowClipboard=new SystemRowClipboard();
    internal string? TestRecentPath;
    FindDialog? findDialog;string lastFindKey="";int lastFindRow=-1,lastFindOffset=-1;
    string? RecentPath=>SmokeMode?TestRecentPath:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Frameflow","recent.json");
    void Remember(string path,string kind){try{if(RecentPath is string store)RecentFiles.Add(store,path,kind);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Status.Text="文件已打开/保存，但最近记录无法更新："+ex.Message;}}
    void PopulateRecent(object sender,RoutedEventArgs e)
    {
        RecentMenu.Items.Clear();try
        {
            var entries=RecentPath is string store?RecentFiles.Load(store):[];
            foreach(var entry in entries)
            {
                bool exists=File.Exists(entry.Path);var item=new MenuItem{Header=$"{(entry.Kind=="subtitle"?"字幕":"媒体")} · {Path.GetFileName(entry.Path)}{(exists?"":"（已移动或删除）")}",ToolTip=entry.Path,IsEnabled=exists};
                item.Click+=(_,_)=>Guard(()=>{if(entry.Kind=="subtitle")OpenSubtitle(entry.Path);else OpenMedia(entry.Path);});RecentMenu.Items.Add(item);
            }
            if(entries.Count==0)RecentMenu.Items.Add(new MenuItem{Header="暂无最近打开的文件",IsEnabled=false});
            else {RecentMenu.Items.Add(new Separator());var clear=new MenuItem{Header="清空最近记录（不删除文件）"};clear.Click+=(_,_)=>Guard(()=>{if(RecentPath is string path)RecentFiles.Clear(path);});RecentMenu.Items.Add(clear);}
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){RecentMenu.Items.Add(new MenuItem{Header="无法读取最近记录",IsEnabled=false,ToolTip=ex.Message});}
    }
    MainWindow CreateIndependentWindow()
    {
        var window=new MainWindow{SmokeMode=SmokeMode,TestRecentPath=TestRecentPath,ShowActivated=!SmokeMode};window.Show();return window;
    }
    void NewWindowClick(object sender,RoutedEventArgs e)=>Guard(()=>{CreateIndependentWindow();});
    void SaveTo(string path)
    {
        string extension=document.LegacySsa?".ssa":".ass";
        if(!Path.GetExtension(path).Equals(extension,StringComparison.OrdinalIgnoreCase))throw new FormatException("请使用 "+extension+" 扩展名；其他格式请使用「导出字幕」。");
        SubtitleDocument.AtomicSave(path,document.Bytes());ReviewStore.Save(path,document,mediaPath);subtitlePath=path;dirty=draftDirty=false;UpdateTitle();Status.Text="已保存字幕及独立校对记录；覆盖前已备份旧文件。";Remember(path,"subtitle");
    }
    void ExportTo(string path)
    {
        string ext=Path.GetExtension(path).ToLowerInvariant(),native=document.LegacySsa?".ssa":".ass";
        if(ext!=native&&ext!=".srt")throw new FormatException("当前项目可导出 "+native+" 或 .srt；不伪装转换 ASS/SSA 样式。");
        if(subtitlePath!=null&&Path.GetFullPath(path).Equals(Path.GetFullPath(subtitlePath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("导出是创建副本，请选用与当前字幕不同的文件名；覆盖当前项目请用保存。");
        SubtitleDocument.AtomicSave(path,ext==".srt"?new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(document.ExportSrt())).ToArray():document.Bytes());
        Status.Text="已导出字幕副本；当前项目路径、未保存状态及校对记录保持不变。";
    }
    void ExportSubtitleClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEditor())return;string native=document.LegacySsa?"SSA":"ASS";var dlg=new SaveFileDialog{Title="导出字幕副本",Filter=$"{native} 字幕（保留样式和说话人）|*.{native.ToLowerInvariant()}|SRT 字幕（仅正文和时间）|*.srt",DefaultExt="."+native.ToLowerInvariant(),FileName=Path.GetFileNameWithoutExtension(subtitlePath??"未命名")+"-导出"};
        if(dlg.ShowDialog(this)!=true)return;
        if(Path.GetExtension(dlg.FileName).Equals(".srt",StringComparison.OrdinalIgnoreCase)&&MessageBox.Show(this,"SRT 会丢失样式、说话人、层、ASS 特效及注释行。只导出副本，不改变当前项目。继续？","导出 SRT",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        Guard(()=>ExportTo(dlg.FileName));
    }
    AssRow[] SelectedRows()=>SubtitleGrid.SelectedItems.Cast<AssRow>().OrderBy(r=>r.Number).ToArray();
    void SelectRows(AssRow[] rows)
    {
        loading=true;SubtitleGrid.SelectedItems.Clear();foreach(var row in rows)SubtitleGrid.SelectedItems.Add(row);selected=rows.FirstOrDefault();SubtitleGrid.SelectedItem=selected;loading=false;LoadEditor();if(selected!=null)SubtitleGrid.ScrollIntoView(selected);
    }
    bool CopySelectedRows(bool cut)
    {
        if(!ApplyEditor())return false;var rows=SelectedRows();if(rows.Length==0){Status.Text="请先在字幕列表选择行。";return false;}
        var packet=RowTransfer.Capture(document,rows);RowClipboard.Write(RowTransfer.Encode(packet),RowTransfer.AssText(packet));
        // Never remove rows until the clipboard write has succeeded.
        if(cut){PushUndo();int index=rows[0].Number-1;foreach(var row in rows)document.Remove(row);MarkDirty();RefreshAll(index);}
        Status.Text=$"已{(cut?"剪切":"复制")} {rows.Length} 行（时间、说话人、样式和特效均保留）。";return true;
    }
    void CopyRowsClick(object sender,RoutedEventArgs e)=>Guard(()=>CopySelectedRows(false));
    void CutRowsClick(object sender,RoutedEventArgs e)=>Guard(()=>CopySelectedRows(true));
    RowPacket ReadRows()
    {
        var (native,text)=RowClipboard.Read();return native!=null?RowTransfer.Decode(native):RowTransfer.FromText(text??"",selected?.Start??0,selected==null?2:selected.End-selected.Start);
    }
    void PasteRows(RowPacket packet)
    {
        int index=SelectedRows().FirstOrDefault()?.Number-1??document.Rows.Count();
        // Stage validation on a copy: malformed clipboard cannot partially modify a document.
        var staged=SubtitleDocument.Parse(document.Serialize());RowTransfer.Insert(staged,packet,index);
        PushUndo();var inserted=RowTransfer.Insert(document,packet,index);MarkDirty();RefreshAll(inserted[0].Number-1);SelectRows(inserted);Status.Text=$"已在选中行之前插入 {inserted.Length} 行；保留原时间，同名样式沿用当前项目。可撤销。";
    }
    void PasteRowsClick(object sender,RoutedEventArgs e){if(!ApplyEditor())return;Guard(()=>PasteRows(ReadRows()));}
    void PasteSelectedFields(RowPacket packet,AssRow[] targets,IReadOnlySet<string> fields)
    {
        if(fields.Count==0)throw new FormatException("请至少勾选一个字段。");
        var staged=SubtitleDocument.Parse(document.Serialize());RowTransfer.PasteFields(staged,packet,targets.Select(r=>staged.Rows.ElementAt(r.Number-1)).ToArray(),fields);
        PushUndo();RowTransfer.PasteFields(document,packet,targets,fields);MarkDirty();RefreshAll(targets.First().Number-1);SelectRows(targets);Status.Text=$"已选择性粘贴 {targets.Length} 行；未勾选字段和批注保持不变。可撤销。";
    }
    void SelectivePasteClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEditor())return;Guard(()=>{var packet=ReadRows();var targets=SelectedRows();if(targets.Length==0)throw new FormatException("请先选择目标字幕行。");
            var dialog=new SelectivePasteDialog(packet.Rows.Length,targets.Length){Owner=this};if(dialog.ShowDialog()==true)PasteSelectedFields(packet,targets,dialog.Fields);});
    }
    void FindClick(object sender,RoutedEventArgs e)
    {
        if(findDialog==null){findDialog=new FindDialog(this);findDialog.Closed+=(_,_)=>findDialog=null;findDialog.Show();}else findDialog.Activate();findDialog.FocusQuery();
    }
    internal string FindText(string query,string scope,bool matchCase,bool wrap,bool backwards)
    {
        if(query.Length==0)return "请输入要查找的内容。";if(!ApplyEditor())return "请先修正当前字幕的时间或字段。";
        var hits=new List<(AssRow Row,int Offset)>();var comparison=matchCase?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
        foreach(var row in document.Rows)
        {
            string value=scope switch{"说话人"=>row.Actor,"样式"=>row.Style,"特效"=>row.Effect,"校对批注"=>row.Note,_=>row.Text.Replace(@"\N","\n")};
            for(int pos=0;pos<=value.Length-query.Length;){int found=value.IndexOf(query,pos,comparison);if(found<0)break;hits.Add((row,found));pos=found+Math.Max(1,query.Length);}
        }
        if(hits.Count==0)return "未找到匹配项。";
        string key=scope+"\0"+matchCase+"\0"+query;int previous=key==lastFindKey&&selected?.Number==lastFindRow?hits.FindIndex(h=>h.Row.Number==lastFindRow&&h.Offset==lastFindOffset):-1;
        int index=previous>=0?previous+(backwards?-1:1):backwards?hits.FindLastIndex(h=>h.Row.Number<=(selected?.Number??int.MaxValue)):hits.FindIndex(h=>h.Row.Number>=(selected?.Number??1));
        bool wrapped=false;if(index<0||index>=hits.Count){if(!wrap)return "已到达查找边界（未开启循环）。";index=backwards?hits.Count-1:0;wrapped=true;}
        var hit=hits[index];Pause();SelectRow(hit.Row);EditorTabs.SelectedIndex=0;if(scope=="字幕正文"){TextEditor.Select(hit.Offset,query.Length);TextEditor.ScrollToLine(TextEditor.GetLineIndexFromCharacterIndex(hit.Offset));}
        lastFindKey=key;lastFindRow=hit.Row.Number;lastFindOffset=hit.Offset;return $"第 {index+1} / {hits.Count} 处 · 第 {hit.Row.Number} 句{(wrapped?" · 已循环":"")}";
    }
    void RepeatFind(bool backwards){if(findDialog==null)FindClick(this,new RoutedEventArgs());else findDialog.Search(backwards);}
}

internal interface IRowClipboard { void Write(string native,string text);(string? Native,string? Text) Read(); }
internal sealed class SystemRowClipboard:IRowClipboard
{
    public void Write(string native,string text){var data=new DataObject();data.SetData(RowTransfer.ClipboardFormat,native);data.SetText(text,TextDataFormat.UnicodeText);Clipboard.SetDataObject(data,true);}
    public (string?,string?) Read(){var data=Clipboard.GetDataObject();return (data?.GetDataPresent(RowTransfer.ClipboardFormat)==true?data.GetData(RowTransfer.ClipboardFormat) as string:null,data?.GetData(DataFormats.UnicodeText) as string);}
}
