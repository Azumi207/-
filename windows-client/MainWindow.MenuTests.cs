using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frameflow.Core;
using Frameflow.Controls;

namespace Frameflow;
public partial class MainWindow
{
    sealed class MemoryRows:IRowClipboard
    {
        public string? Native,Text;public bool FailWrite;
        public void Write(string native,string text){if(FailWrite)throw new InvalidOperationException("Simulated clipboard busy");Native=native;Text=text;}
        public (string?,string?) Read()=>(Native,Text);
    }
    static IEnumerable<T> MenuVisuals<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T t)yield return t;foreach(var x in MenuVisuals<T>(child))yield return x;}
    }
    static void CaptureElement(FrameworkElement element,string file)
    {
        element.UpdateLayout();
        if(element.ActualWidth<=0||element.ActualHeight<=0){File.WriteAllText(file+".notcaptured.txt","NOT CAPTURED: native popup/layout unavailable in this automated desktop session. No screenshot or visual-pass claim. Functional command assertions still execute.");return;}
        var bmp=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var s=File.Create(file);encoder.Save(s);
    }
    async Task RunMenuTestsAsync(Action<bool,string> check,string dir)
    {
        Pause();var saved=Capture();string? originalPath=subtitlePath;bool oldDirty=dirty;var clip=new MemoryRows();var oldClipboard=RowClipboard;RowClipboard=clip;TestRecentPath=Path.Combine(dir,"ui-recent.json");RecentFiles.Clear(TestRecentPath);
        var rows=document.Rows.ToArray();SelectRows([rows[0],rows[2]]);check(SelectedRows().Length==2,"subtitle grid supports noncontiguous multi-row selection");
        check(HandleShortcut(Key.C,ModifierKeys.Control,false,false,false)&&RowTransfer.Decode(clip.Native!).Rows.Length==2,"Ctrl+C copies selected rows through clipboard adapter");
        check(document.Rows.Count()==3&&!string.IsNullOrEmpty(clip.Text)&&clip.Text.StartsWith("Dialogue:"),"copy retains document and publishes compatible ASS event text");
        string before=document.Serialize();clip.FailWrite=true;bool failed=false;try{CopySelectedRows(true);}catch(InvalidOperationException){failed=true;}clip.FailWrite=false;
        check(failed&&document.Serialize()==before,"failed clipboard write never deletes cut rows");
        check(HandleShortcut(Key.X,ModifierKeys.Control,false,false,false)&&document.Rows.Count()==1&&document.Rows.Single().Text==rows[1].Text,"Ctrl+X cuts only selected noncontiguous rows");
        UndoClick(this,new RoutedEventArgs());check(document.Rows.Count()==3&&document.Serialize()==before,"cut can be undone with original row order and values");
        SelectRow(document.Rows.ElementAt(1));check(HandleShortcut(Key.V,ModifierKeys.Control,false,false,false)&&document.Rows.Count()==5,"Ctrl+V inserts copied rows before current row");
        check(document.Rows.ElementAt(1).Text==rows[0].Text&&document.Rows.ElementAt(2).Text==rows[2].Text&&SelectedRows().Length==2,"paste keeps copied order and selects inserted rows");
        UndoClick(this,new RoutedEventArgs());check(document.Serialize()==before,"paste undo restores exact subtitle document");RedoClick(this,new RoutedEventArgs());check(document.Rows.Count()==5,"paste redo restores inserted rows");UndoClick(this,new RoutedEventArgs());
        foreach(var key in new[]{Key.C,Key.X,Key.V})check(!HandleShortcut(key,ModifierKeys.Control,true,true,false),$"Ctrl+{key} remains native text editing in input fields");
        check(!HandleShortcut(Key.V,ModifierKeys.Control|ModifierKeys.Shift,true,true,false),"Ctrl+Shift+V does not steal text-input paste");

        SelectRow(document.Rows.First());CopySelectedRows(false);SelectRows(document.Rows.Skip(1).ToArray());string preserved=document.Rows.ElementAt(1).Text;double timing=document.Rows.ElementAt(1).Start;
        _ = Dispatcher.BeginInvoke(new Action(()=>{var dialog=Application.Current.Windows.OfType<SelectivePasteDialog>().Single();foreach(var box in MenuVisuals<CheckBox>(dialog))box.IsChecked=Equals(box.Content,"说话人");CaptureElement(dialog,Path.Combine(dir,"selective-paste.png"));MenuVisuals<Button>(dialog).Single(b=>Equals(b.Content,"粘贴所选字段")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}));
        SelectivePasteClick(this,new RoutedEventArgs());check(document.Rows.Skip(1).All(r=>r.Actor==document.Rows.First().Actor)&&document.Rows.ElementAt(1).Text==preserved&&document.Rows.ElementAt(1).Start==timing,"selective paste dialog applies actor only to multiple rows");
        UndoClick(this,new RoutedEventArgs());check(document.Serialize()==before,"selective paste is one undoable operation");
        _ = Dispatcher.BeginInvoke(new Action(()=>Application.Current.Windows.OfType<SelectivePasteDialog>().Single().Close()));SelectivePasteClick(this,new RoutedEventArgs());check(document.Serialize()==before,"canceling selective paste makes no changes");

        var oldFile=Path.Combine(dir,"original-menu.ass");var newFile=Path.Combine(dir,"save-as-menu.ass");SubtitleDocument.AtomicSave(oldFile,document.Bytes(),false);subtitlePath=oldFile;var originalBytes=File.ReadAllBytes(oldFile);
        TextEditor.Text="另存为测试";ApplyEditor();SaveTo(newFile);check(subtitlePath==newFile&&!dirty&&SubtitleDocument.Load(newFile).Rows.Any(r=>r.Text=="另存为测试")&&File.Exists(newFile+".frameflow.json"),"save-as writes new subtitle plus review sidecar and switches project path");
        check(File.ReadAllBytes(oldFile).SequenceEqual(originalBytes),"save-as leaves old subtitle file unchanged");
        TextEditor.Text="待保存的导出内容";ApplyEditor();bool pending=dirty;var export=Path.Combine(dir,"export-copy.ass");ExportTo(export);check(subtitlePath==newFile&&dirty==pending&&File.ReadAllBytes(export).SequenceEqual(document.Bytes()),"ASS export creates exact native copy without switching path or clearing dirty state");
        var srt=Path.Combine(dir,"export-copy.srt");ExportTo(srt);check(SubtitleDocument.Load(srt).Rows.Count()==document.Rows.Count(r=>!r.Comment)&&subtitlePath==newFile&&dirty,"SRT export is readable and retains current project state");
        failed=false;try{ExportTo(newFile);}catch(InvalidOperationException){failed=true;}check(failed,"export refuses overwriting the active project path");
        SaveTo(newFile);check(Directory.GetFiles(dir,"save-as-menu.ass.bak-*").Length>0,"subsequent save keeps previous subtitle backup");
        Remember(oldFile,"subtitle");Remember(newFile,"subtitle");Remember(Path.Combine(dir,"sample.avi"),"media");Remember(oldFile,"subtitle");PopulateRecent(RecentMenu,new RoutedEventArgs());
        check(RecentFiles.Load(TestRecentPath).Count==3&&RecentFiles.Load(TestRecentPath)[0].Path==Path.GetFullPath(oldFile),"recent menu records successful subtitle/media operations without duplicates");
        var recentItem=(MenuItem)RecentMenu.Items[0];recentItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));check(subtitlePath==oldFile&&document.Rows.Count()==3,"recent subtitle item reopens real file through normal open path");
        var missing=Path.Combine(dir,"missing-menu.ass");RecentFiles.Add(TestRecentPath,missing,"subtitle");PopulateRecent(RecentMenu,new RoutedEventArgs());check(!((MenuItem)RecentMenu.Items[0]).IsEnabled,"missing recent path is visibly disabled rather than silently failing");
        ((MenuItem)RecentMenu.Items[RecentMenu.Items.Count-1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));check(RecentFiles.Load(TestRecentPath).Count==0&&File.Exists(oldFile),"clear recent action does not remove subtitle files");

        var child=CreateIndependentWindow();await Task.Delay(100);check(!ReferenceEquals(child.document,document)&&child.subtitlePath==null&&child.mediaPath==null,"new window owns an independent blank project and media state");
        child.TextEditor.Text="仅新窗口";child.ApplyEditor();check(document.Rows.All(r=>r.Text!="仅新窗口")&&child.undo.Count>0&&child.sessionId!=sessionId,"new window edits undo and recovery identity stay isolated");child.Close();check(IsVisible,"closing new window leaves existing project window open");

        SelectRow(document.Rows.First());TextEditor.Text="Hello hello 中文";ApplyEditor();document.Rows.ElementAt(1).Text="中文";LoadEditor();
        var found=FindText("hello","字幕正文",false,true,false);check(found.Contains("1 / 2")&&TextEditor.SelectedText=="Hello","find locates and highlights first case-insensitive text match");
        found=FindText("hello","字幕正文",false,true,false);check(found.Contains("2 / 2")&&TextEditor.SelectionStart==6,"find next locates another occurrence in same subtitle row");
        check(FindText("hello","字幕正文",false,false,false).Contains("边界"),"find can stop at boundary without wrapping");check(FindText("hello","字幕正文",false,true,false).Contains("已循环"),"find wraps when explicitly enabled");
        check(FindText("HELLO","字幕正文",true,true,false)=="未找到匹配项。","case-sensitive find excludes different case");
        found=FindText("中文","字幕正文",true,true,false);found=FindText("中文","字幕正文",true,true,false);check(selected?.Number==2&&found.Contains("2 / 2"),"find next navigates to matching subtitle row");
        found=FindText("中文","字幕正文",true,true,true);check(selected?.Number==1&&found.Contains("1 / 2"),"find previous returns to earlier row");
        check(FindText("Default","样式",true,true,false).Contains("处"),"find supports style field scope");
        SelectRow(document.Rows.First());TextEditor.Text="甲\r\n乙";ApplyEditor();FindText("乙","字幕正文",true,true,false);check(TextEditor.SelectedText=="乙","find highlight maps correctly after a subtitle line break");
        check(HandleShortcut(Key.F,ModifierKeys.Control,true,true,false)&&findDialog!=null,"Ctrl+F opens modeless find while editing text");await Task.Delay(100);MenuVisuals<TextBox>(findDialog!).Single().Text="中文";findDialog!.Search(false);
        // SizeToContent can schedule a second native-window layout after result text changes.
        await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var layoutDeadline=DateTime.UtcNow.AddSeconds(3);while((findDialog.ActualWidth<=0||findDialog.ActualHeight<=0)&&DateTime.UtcNow<layoutDeadline)await Task.Delay(50);
        CaptureElement(findDialog,Path.Combine(dir,"find-dialog.png"));findDialog.Close();

        var top=MenuVisuals<MenuItem>(this).Where(m=>m.Parent is Menu).Take(2).ToArray();
        Activate();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);await Task.Delay(150);
        foreach(var (menu,name) in top.Zip(new[]{"file-menu","edit-menu"}))
        {
            var peer=new System.Windows.Automation.Peers.MenuItemAutomationPeer(menu);
            ((System.Windows.Automation.Provider.IExpandCollapseProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.ExpandCollapse)).Expand();
            await Task.Delay(100);var popup=(Popup)menu.Template.FindName("PART_Popup",menu);
            var popupDeadline=DateTime.UtcNow.AddSeconds(2);var content=(FrameworkElement)popup.Child;
            while(popup.IsOpen&&(content.ActualWidth<=0||content.ActualHeight<=0)&&DateTime.UtcNow<popupDeadline)await Task.Delay(30);
            if(!popup.IsOpen||content.ActualWidth<=0)File.WriteAllText(Path.Combine(dir,name+".png.notcaptured.txt"),$"NOT CAPTURED: native menu popup closed before capture; requested={menu.IsSubmenuOpen}, popup={popup.IsOpen}, active={IsActive}, focus={menu.IsKeyboardFocusWithin}. Not a visual-pass result.");
            else CaptureElement(content,Path.Combine(dir,name+".png"));menu.IsSubmenuOpen=false;await Task.Delay(80);
        }
        check(top[0].Items.OfType<MenuItem>().Any(m=>Equals(m.Header,"导出字幕…"))&&top[1].Items.OfType<MenuItem>().Any(m=>Equals(m.Header,"选择性粘贴…")),"requested file and edit command entries retained in native menu structure (popup screenshot is a separate diagnostic)");
        RowClipboard=oldClipboard;Restore(saved);subtitlePath=originalPath;dirty=oldDirty;draftDirty=false;TestRecentPath=null;UpdateTitle();Seek(selected!.Start);
    }
}
