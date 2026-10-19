using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Frameflow.Core;
using Frameflow.Controls;

namespace Frameflow;
public partial class MainWindow
{
    async Task RunSubtitleTestsAsync(Action<bool,string> check,string dir)
    {
        Pause();var saved=Capture();bool wasDirty=dirty;
        void Fixture(string first="甲")
        {
            draftDirty=false;document=SubtitleDocument.New();document.Add(1,2,first);document.Add(2,3,"乙");document.Add(4,5,"丙");document.AddStyle("Accent");undo.Clear();redo.Clear();RefreshAll();
        }
        void Click(SubtitleToolDialog dialog,string name)=>MenuVisuals<Button>(dialog).Single(b=>Equals(b.Content,name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Modal<T>(Action launch,Action<T> action,int delay=100) where T:SubtitleToolDialog
        {
            Exception? error=null;
            _=Dispatcher.BeginInvoke(new Action(async()=>{
                T? dialog=null;try{await Task.Delay(delay);dialog=Application.Current.Windows.OfType<T>().Single();dialog.UpdateLayout();action(dialog);}catch(Exception ex){error=ex;}finally{if(dialog?.IsVisible==true)dialog.Close();}
            }));launch();if(error!=null)throw new Exception("Modal subtitle test failed",error);
        }
        Fixture();SubtitleMenu.IsSubmenuOpen=true;await Task.Delay(100);var popup=(Popup)SubtitleMenu.Template.FindName("PART_Popup",SubtitleMenu);CaptureElement((FrameworkElement)popup.Child,Path.Combine(dir,"subtitle-menu.png"));SubtitleMenu.IsSubmenuOpen=false;
        check(SubtitleMenu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"样式与位置"))&&SubtitleMenu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"校对模式")),"subtitle menu retains original style positioning and review actions");
        IEnumerable<MenuItem> Items(MenuItem parent){foreach(var child in parent.Items.OfType<MenuItem>()){yield return child;foreach(var nested in Items(child))yield return nested;}}
        var operations=Items(SubtitleMenu).Where(i=>i.Tag is string).ToArray();check(operations.Length==26,"subtitle menu exposes all 26 line/insert/join/sort/swap actions");
        foreach(var item in operations)
        {
            string op=(string)item.Tag;Fixture(op=="split-karaoke"?@"{\k50}甲{\k50}乙":"甲");
            if(op=="recombine"){document.Rows.ElementAt(1).Text=@"甲\N乙";document.Rows.Last().Text="乙";document.Rows.Last().Start=3;LoadEditor();}
            if(op.StartsWith("join-")||op=="recombine"||op=="swap")SelectRows([document.Rows.First(),document.Rows.ElementAt(1)]);
            if(op=="recombine")SelectRows(document.Rows.ToArray());
            if(op.StartsWith("sort-")){document.AddStyle("Zulu");document.Rows.First().Start=5;document.Rows.First().End=6;document.Rows.First().Style="Zulu";document.Rows.First().Actor="Z";document.Rows.First().Effect="Z";document.Rows.First().Layer="5";LoadEditor();SelectRows([document.Rows.First(),document.Rows.Last()]);}
            Seek(1.5);string before=document.Serialize();int count=document.Rows.Count();item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(document.Serialize()!=before&&dirty,"desktop menu route executes "+op);
            UndoClick(this,new RoutedEventArgs());if(document.Serialize()!=before){File.WriteAllText(Path.Combine(dir,"failed-"+op+"-before.ass"),before);File.WriteAllText(Path.Combine(dir,"failed-"+op+"-after.ass"),document.Serialize());}
            check(document.Serialize()==before&&document.Rows.Count()==count,"single undo restores "+op);
        }
        Fixture();string initial=document.Serialize();
        document.Rows.First().Style="MissingStyle";LoadEditor();check(ApplyEditor()&&document.Rows.First().Style=="MissingStyle","loading an undefined style reference does not silently replace it with Default");Fixture();
        Modal<StyleManagerDialog>(()=>StyleManagerClick(this,new RoutedEventArgs()),dialog=>{dialog.Fields["Name"].Text="对白";dialog.Fields["Fontsize"].Text="36";CaptureElement(dialog,Path.Combine(dir,"style-manager.png"));Click(dialog,"完成");});
        check(document.Styles.First().Name=="对白"&&document.Styles.First().Get("Fontsize")=="36"&&document.Rows.All(r=>r.Style=="对白"),"style manager commits edited fields and synchronized row references");UndoClick(this,new RoutedEventArgs());check(document.Serialize()==initial,"style manager is one undoable transaction");
        Modal<StyleManagerDialog>(()=>StyleManagerClick(this,new RoutedEventArgs()),dialog=>{dialog.Fields["Fontsize"].Text="999";Click(dialog,"应用字段");Click(dialog,"取消");});check(document.Serialize()==initial,"style manager cancel discards even applied staged fields");
        Modal<StyleManagerDialog>(()=>StyleManagerClick(this,new RoutedEventArgs()),dialog=>{
            var library=SubtitleDocument.Parse("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, Extra\nStyle: Imported,Arial,24,retain-me\n");dialog.Import(library);dialog.Refresh("Imported");Click(dialog,"复制样式");
            var path=Path.Combine(dir,"exported-style-library.ass");dialog.Export(path);var reopened=SubtitleDocument.Load(path);check(!reopened.Rows.Any()&&reopened.Styles.Count(s=>s.Get("Extra")=="retain-me")==2,"style library export writes readable ASS without dialogue and keeps unknown style fields");Click(dialog,"完成");
        });var styleRoundtrip=SubtitleDocument.Parse(document.Serialize());check(styleRoundtrip.Warnings.Count==0&&styleRoundtrip.Styles.Count(s=>s.Get("Extra")=="retain-me")==2,"style import and duplication preserve custom schema through ASS roundtrip");
        Fixture();Modal<LineAssistantDialog>(()=>StylingAssistantClick(this,new RoutedEventArgs()),dialog=>{dialog.StyleChoice.SelectedItem="Accent";dialog.MoveRow(1);CaptureElement(dialog,Path.Combine(dir,"styling-assistant.png"));Click(dialog,"完成");});check(document.Rows.First().Style=="Accent"&&document.Rows.ElementAt(1).Style=="Default","styling assistant assigns style then advances without leaking to other lines");
        Fixture(@"{\i1}Hello{\i0}\Nworld");initial=document.Serialize();Modal<LineAssistantDialog>(()=>TranslationAssistantClick(this,new RoutedEventArgs()),dialog=>{check(dialog.Editor.Text=="Hello","translation assistant starts at first actual text block, not empty tag prefix");dialog.Editor.Text="你好";dialog.MovePart(1);check(dialog.Editor.Text.Contains("world"),"translation assistant navigates body blocks around ASS overrides");CaptureElement(dialog,Path.Combine(dir,"translation-assistant.png"));Click(dialog,"完成");});
        check(document.Rows.First().Text==@"{\i1}你好{\i0}\Nworld","translation assistant preserves inline tags and unedited blocks");UndoClick(this,new RoutedEventArgs());check(document.Serialize()==initial,"translation session has one-step undo");
        Modal<LineAssistantDialog>(()=>TranslationAssistantClick(this,new RoutedEventArgs()),dialog=>{dialog.Editor.Text="丢弃";dialog.MoveRow(1);Click(dialog,"取消");});check(document.Serialize()==initial,"translation cancel discards changes from earlier rows too");
        Fixture("heloo world");Modal<LineAssistantDialog>(()=>SpellcheckClick(this,new RoutedEventArgs()),dialog=>{
            dialog.FindSpelling();bool detected=dialog.CurrentSpelling!=null;File.WriteAllText(Path.Combine(dir,"spelling-observed.txt"),"SpellCheckEnabled: "+SpellCheck.GetIsEnabled(dialog.Editor)+" Language: "+dialog.Editor.Language.IetfLanguageTag+"\nWindows English spelling error detected: "+detected+"\nSuggestions: "+string.Join(",",dialog.Suggestions.Items.Cast<object>()));
            check(SpellCheck.GetIsEnabled(dialog.Editor)&&dialog.Editor.Language.IetfLanguageTag.Equals("en-US",StringComparison.OrdinalIgnoreCase),"spelling tool enables local Windows English dictionary on body text");
            check(detected&&dialog.Suggestions.Items.Count>0,"Windows spelling service actually detects misspelling and supplies suggestions");
            CaptureElement(dialog,Path.Combine(dir,"spelling-dialog.png"));dialog.CorrectSpelling();Click(dialog,"完成");
        },700);check(document.Rows.First().Text!="heloo world","accepting a dictionary suggestion changes the subtitle body");
        Fixture(@"{\pos(960,900)}甲");Modal<ResampleDialog>(()=>ResampleClick(this,new RoutedEventArgs()),dialog=>{dialog.WidthInput.Text="1280";dialog.HeightInput.Text="720";CaptureElement(dialog,Path.Combine(dir,"resample-dialog.png"));Click(dialog,"应用重采样");});check(document.PlayResX==1280&&document.Rows.First().Text.Contains(@"\pos(640,600)"),"resample dialog commits proportional script and coordinate changes");UndoClick(this,new RoutedEventArgs());check(document.PlayResX==1920,"resample is undoable");
        Fixture(@"{\move(0,0,100,100)}甲");initial=document.Serialize();Modal<ResampleDialog>(()=>ResampleClick(this,new RoutedEventArgs()),dialog=>{dialog.WidthInput.Text="1280";dialog.HeightInput.Text="720";Click(dialog,"应用重采样");check(dialog.IsVisible&&dialog.Message.Text.Length>0,"unsupported resampling remains in dialog with explicit explanation");Click(dialog,"取消");});check(document.Serialize()==initial,"failed resampling leaves every row and field intact");
        Fixture();Modal<DrawingDialog>(()=>DrawingClick(this,new RoutedEventArgs()),dialog=>{dialog.AddPoint(new(400,300));dialog.AddPoint(new(900,300));dialog.AddPoint(new(650,600));CaptureElement(dialog,Path.Combine(dir,"drawing-dialog.png"));Click(dialog,"插入绘图行");});
        check(document.Rows.Count()==4&&selected!.Text.Contains(@"\p1")&&document.Rows.First().Text=="甲","drawing tool inserts independent ASS vector row without replacing dialogue");UpdateLayout();CapturePng(Path.Combine(dir,"drawing-preview.png"));check(BasicDrawing.Geometry(selected!.Text)!=null,"inserted polygon has real video-overlay geometry");UndoClick(this,new RoutedEventArgs());check(document.Rows.Count()==3,"drawing insert undo restores original row count");
        Fixture();Modal<SelectLinesDialog>(()=>SelectLinesClick(this,new RoutedEventArgs()),dialog=>{dialog.Query.Text="[甲丙]";dialog.Match.SelectedItem="正则表达式";CaptureElement(dialog,Path.Combine(dir,"select-lines.png"));Click(dialog,"选择");});check(SelectedRows().Select(r=>r.Number).SequenceEqual(new[]{1,3}),"conditional selection dialog selects matching noncontiguous rows");
        ApplyLineSelection([document.Rows.ElementAt(1)],1);check(SelectedRows().Length==3,"conditional selection union works");ApplyLineSelection([document.Rows.First()],2);check(SelectedRows().Length==2,"conditional selection subtraction works");ApplyLineSelection([document.Rows.Last()],3);check(SelectedRows().Single().Number==3,"conditional selection intersection works");SelectAllLinesClick(this,new RoutedEventArgs());check(SelectedRows().Length==3,"select all subtitle menu action selects every row");
        draftDirty=false;Restore(saved);dirty=wasDirty;UpdateTitle();Seek(selected!.Start);
    }
}
