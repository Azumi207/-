using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Frameflow.Core;
using Microsoft.Win32;

namespace Frameflow.Controls;

internal class SubtitleToolDialog:Window
{
    protected readonly StackPanel Body=new(){Margin=new Thickness(16)};
    internal readonly TextBlock Message=new(){TextWrapping=TextWrapping.Wrap,Foreground=Frameflow.Core.Appearance.Brush("ErrorBrush"),Margin=new Thickness(3,8,3,4)};
    protected readonly StackPanel Actions=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(12)};
    protected SubtitleToolDialog(string title,double width=640,double height=600)
    {
        Title=title;Width=width;Height=height;MinWidth=460;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var dock=new DockPanel();DockPanel.SetDock(Actions,Dock.Bottom);dock.Children.Add(Actions);dock.Children.Add(new ScrollViewer{Content=Body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=dock;
    }
    protected void Hint(string text)=>Body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Frameflow.Core.Appearance.Brush("MutedBrush"),Margin=new Thickness(3,5,3,10)});
    internal static TextBox Input(Panel panel,string label,string value="",bool multiline=false)
    {
        panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(3,6,3,1)});var text=new TextBox{Text=value,MinHeight=multiline?76:28,AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};panel.Children.Add(text);return text;
    }
    internal static ComboBox Choice(Panel panel,string label,IEnumerable<string> values)
    {
        panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(3,6,3,1)});var combo=new ComboBox{ItemsSource=values.ToArray(),SelectedIndex=0,MinWidth=120};panel.Children.Add(combo);return combo;
    }
    internal Button Action(string label,Action action,Panel? parent=null)
    {
        var button=new Button{Content=label,MinWidth=80};button.Click+=(_,_)=>Attempt(action);(parent??Actions).Children.Add(button);return button;
    }
    protected void Attempt(Action action){try{Message.Text="";action();}catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or FormatException or OverflowException or System.IO.IOException or UnauthorizedAccessException or RegexMatchTimeoutException){Message.Foreground=Frameflow.Core.Appearance.Brush("ErrorBrush");Message.Text=ex.Message;}}
    protected void Finish(){Body.Children.Add(Message);Action("取消",()=>DialogResult=false);}
}

internal sealed class StyleManagerDialog:SubtitleToolDialog
{
    internal SubtitleDocument Working {get;}
    internal readonly ListBox Styles=new(){Height=110};
    internal readonly Dictionary<string,TextBox> Fields=new(StringComparer.OrdinalIgnoreCase);
    readonly WrapPanel fields=new();bool refreshing;
    internal StyleManagerDialog(SubtitleDocument source):base("样式管理器 · 当前字幕",720,740)
    {
        Working=SubtitleTools.Copy(source);Hint("编辑全部样式字段；改名同步更新引用。先「应用字段」，最后「完成」统一提交，可撤销。被引用的样式不能删除。");
        Body.Children.Add(Styles);Styles.SelectionChanged+=(_,_)=>{if(!refreshing)LoadFields();};
        var tools=new WrapPanel();Body.Children.Add(tools);
        Action("新建",()=>{string name=Unique("新样式");Working.AddStyle(name);Refresh(name);},tools);
        Action("复制样式",()=>{var s=Current();string name=Unique(s.Name+" 副本");Working.AddStyle(name);var added=Working.Styles.Last();added.Format=s.Format.ToArray();added.Values=s.Values.ToArray();added.Set("Name",name);FixStyleFormat(added);Refresh(name);},tools);
        Action("删除",()=>{SubtitleTools.DeleteStyle(Working,Current());Refresh();},tools);
        Action("导入 ASS 样式…",()=>{var picker=new OpenFileDialog{Filter="ASS 字幕|*.ass"};if(picker.ShowDialog(this)==true){Import(SubtitleDocument.Load(picker.FileName));Refresh();}},tools);
        Action("导出样式库…",()=>{var picker=new SaveFileDialog{Filter="ASS 样式库|*.ass",FileName="样式库.ass"};if(picker.ShowDialog(this)==true){Export(picker.FileName);Message.Text="已导出不含字幕正文的样式库。";}},tools);
        Body.Children.Add(fields);Action("应用字段",SaveFields);Action("完成",()=>{SaveFields();DialogResult=true;});Finish();Refresh();
    }
    string Unique(string prefix){string name=prefix;int n=2;while(Working.Styles.Any(s=>s.Name==name))name=prefix+" "+n++;return name;}
    AssStyle Current()=>Working.Styles.FirstOrDefault(s=>s.Name==(string?)Styles.SelectedItem)??throw new InvalidOperationException("请先选择样式。");
    internal void Refresh(string? name=null){refreshing=true;Styles.ItemsSource=Working.Styles.Select(s=>s.Name).ToArray();Styles.SelectedItem=name; if(Styles.SelectedIndex<0)Styles.SelectedIndex=0;refreshing=false;LoadFields();}
    void LoadFields()
    {
        fields.Children.Clear();Fields.Clear();if(Styles.SelectedItem==null)return;var style=Current();
        var labels=new Dictionary<string,string>{{"Name","名称"},{"Fontname","字体"},{"Fontsize","字号"},{"PrimaryColour","主颜色 ASS"},{"SecondaryColour","次颜色 ASS"},{"OutlineColour","描边颜色 ASS"},{"BackColour","阴影颜色 ASS"},{"Bold","粗体 -1/0"},{"Italic","斜体 -1/0"},{"Underline","下划线 -1/0"},{"StrikeOut","删除线 -1/0"},{"ScaleX","横向缩放 %"},{"ScaleY","纵向缩放 %"},{"Spacing","字间距"},{"Angle","旋转角度"},{"BorderStyle","边框模式 1/3"},{"Outline","描边"},{"Shadow","阴影"},{"Alignment","对齐 1～9"},{"MarginL","左边距"},{"MarginR","右边距"},{"MarginV","垂直边距"},{"Encoding","字符集"}};
        foreach(string field in style.Format){var panel=new StackPanel{Width=210,Margin=new Thickness(0,0,8,0)};fields.Children.Add(panel);Fields[field]=Input(panel,labels.GetValueOrDefault(field,field),style.Get(field));}
    }
    internal void SaveFields()
    {
        var style=Current();var numeric=new HashSet<string>(new[]{"Fontsize","ScaleX","ScaleY","Spacing","Angle","Outline","Shadow","MarginL","MarginR","MarginV","Alignment","Encoding","Bold","Italic","Underline","StrikeOut","BorderStyle","AlphaLevel"},StringComparer.OrdinalIgnoreCase);
        foreach(var (field,input) in Fields)
        {
            if(input.Text.IndexOfAny([',','\r','\n'])>=0)throw new FormatException(field+" 不能含逗号或换行。");
            if(numeric.Contains(field)&&(!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number)))throw new FormatException(field+" 必须是有限数字。");
            if(field.Contains("Colour",StringComparison.OrdinalIgnoreCase)&&!Regex.IsMatch(input.Text,@"^(?:&H[0-9a-fA-F]{1,8}&?|-?\d+)$"))throw new FormatException(field+" 请使用 ASS 颜色（如 &H00FFFFFF）。");
        }
        double Num(string name,double fallback)=>Fields.TryGetValue(name,out var box)?double.Parse(box.Text,CultureInfo.InvariantCulture):fallback;
        if(Num("Fontsize",48)<=0||Num("ScaleX",100)<=0||Num("ScaleY",100)<=0)throw new FormatException("字号与缩放必须大于零。");
        foreach(string field in new[]{"Bold","Italic","Underline","StrikeOut"})if(Fields.ContainsKey(field)&&Num(field,0)!=0&&Num(field,0)!=-1&&Num(field,0)!=1)throw new FormatException(field+" 仅接受 -1、0、1。");
        foreach(string field in new[]{"MarginL","MarginR","MarginV","Encoding","Alignment","BorderStyle"})if(Fields.ContainsKey(field)&&(Num(field,0)<0||Num(field,0)!=Math.Truncate(Num(field,0))))throw new FormatException(field+" 必须为非负整数。");
        if(!Working.LegacySsa&&(Num("Alignment",2)<1||Num("Alignment",2)>9))throw new FormatException("ASS 对齐值为 1～9。");
        SubtitleTools.RenameStyle(Working,style,Fields["Name"].Text);foreach(var (field,input) in Fields)if(!field.Equals("Name",StringComparison.OrdinalIgnoreCase))style.Set(field,input.Text);foreach(var row in Working.Rows)row.Reviewed=false;Refresh(style.Name);Message.Text="字段已应用到暂存副本；点击完成提交。";
    }
    void FixStyleFormat(AssStyle style)
    {
        int index=Working.Records.FindIndex(r=>r.Style==style);Working.Records.Insert(index,new(){Raw="Format: "+string.Join(", ",style.Format)});
    }
    internal void Import(SubtitleDocument source)
    {
        if(source.LegacySsa||Working.LegacySsa)throw new InvalidOperationException("样式库导入暂仅支持 ASS。");
        foreach(var style in source.Styles){string name=Unique(style.Name);Working.AddStyle(name);var added=Working.Styles.Last();added.Format=style.Format.ToArray();added.Values=style.Values.ToArray();added.Set("Name",name);FixStyleFormat(added);}
    }
    internal void Export(string path)
    {
        // Export only native style definitions, never unparsed dialogue or attachments.
        var library=SubtitleDocument.New();library.Records.RemoveAll(r=>r.Style!=null);
        int index=library.Records.FindIndex(r=>r.Raw.Trim()=="[Events]");
        if(Working.LegacySsa)throw new InvalidOperationException("旧 SSA 样式库暂不转换导出为 ASS。");
        foreach(var style in Working.Styles){library.Records.Insert(index++,new(){Raw="Format: "+string.Join(", ",style.Format)});library.Records.Insert(index++,new(){Raw=style.Serialize()});}
        SubtitleDocument.AtomicSave(path,library.Bytes());
    }
}

internal sealed class LineAssistantDialog:SubtitleToolDialog
{
    internal SubtitleDocument Working {get;}
    internal int RowIndex {get;private set;}
    internal readonly TextBox Original,Editor;
    internal readonly ComboBox StyleChoice;
    internal readonly TextBlock Progress=new(){FontWeight=FontWeights.SemiBold};
    internal readonly ListBox Suggestions=new(){Height=72};
    readonly bool styling,spelling;readonly string[] originals;readonly Action<int,bool> preview;int part;
    internal LineAssistantDialog(SubtitleDocument source,int index,string mode,Action<int,bool> show):base(mode=="style"?"样式助手":mode=="spell"?"拼写检查 · 英语（Windows 词典）":"翻译助手 · 保留 ASS 标签",680,690)
    {
        Working=SubtitleTools.Copy(source);originals=source.Rows.Select(r=>r.Text).ToArray();styling=mode=="style";spelling=mode=="spell";preview=show;RowIndex=Math.Clamp(index,0,Math.Max(0,originals.Length-1));
        if(originals.Length==0)throw new InvalidOperationException("请先添加字幕行。");
        Hint(styling?"逐句分配共享样式。切换行会应用当前选择；试听不更改时间。完成后统一提交，取消放弃本次助手修改。":"一次编辑一个正文片段，花括号内的 ASS 标签不动。原文保留为本次会话快照；不是机器翻译。切换会暂存编辑，完成后统一提交。绘图行需在主编辑器处理。");
        if(spelling)Hint("使用本机英语词典，不检查中文/日文语义。无建议不代表译文正确；取决于 Windows 拼写服务是否可用。");
        Body.Children.Add(Progress);Original=Input(Body,"原文（本次会话）","",true);Original.IsReadOnly=true;StyleChoice=Choice(Body,"应用样式",Working.Styles.Select(s=>s.Name));StyleChoice.Visibility=styling?Visibility.Visible:Visibility.Collapsed;if(!styling)((FrameworkElement)Body.Children[Body.Children.Count-2]).Visibility=Visibility.Collapsed;
        Editor=Input(Body,styling?"正文（只读）":"当前正文片段","",true);Editor.IsReadOnly=styling;
        if(spelling){Editor.Language=XmlLanguage.GetLanguage("en-US");SpellCheck.SetIsEnabled(Editor,true);Body.Children.Add(Suggestions);var spellTools=new WrapPanel();Body.Children.Add(spellTools);Action("下一个拼写问题",FindSpelling,spellTools);Action("采用所选建议",CorrectSpelling,spellTools);}
        var navigation=new WrapPanel();Body.Children.Add(navigation);Action("上一句",()=>MoveRow(-1),navigation);Action("下一句",()=>MoveRow(1),navigation);
        if(!styling){Action("上个片段",()=>MovePart(-1),navigation);Action("下个片段",()=>MovePart(1),navigation);}
        Action("试听本句",()=>preview(RowIndex,true),navigation);Action("应用并下一句",()=>MoveRow(1));Action("完成",()=>{ApplyCurrent();DialogResult=true;});Finish();part=Math.Max(0,FirstPart(Row.Text));LoadCurrent();
    }
    AssRow Row=>Working.Rows.ElementAt(RowIndex);
    static int FirstPart(string text)=>Array.FindIndex(SubtitleTools.TextParts(text),s=>s.Length>0&&!s.StartsWith('{'));
    internal void LoadCurrent()
    {
        var parts=SubtitleTools.TextParts(Row.Text);if(part<0||part>=parts.Length||parts[part].StartsWith('{'))part=Math.Max(0,FirstPart(Row.Text));
        Progress.Text=$"第 {RowIndex+1} / {originals.Length} 句 · {Row.StartText} → {Row.EndText} · 片段 {part/2+1}";
        Original.Text=originals[RowIndex].Replace(@"\N","\n");StyleChoice.SelectedItem=Row.Style;Editor.Text=(styling?Row.Text:parts[part]).Replace(@"\N","\n");Editor.IsEnabled=!Regex.IsMatch(Row.Text,@"\\p[1-9]");Suggestions.ItemsSource=null;preview(RowIndex,false);
    }
    internal void ApplyCurrent()
    {
        if(styling){if(StyleChoice.SelectedItem is not string name)throw new InvalidOperationException("请选择已存在的样式。");if(Row.Style!=name){Row.Style=name;Row.Reviewed=false;}}
        else if(Editor.IsEnabled){var text=SubtitleTools.ReplacePart(Row.Text,part,Editor.Text);if(text!=Row.Text){Row.Text=text;Row.Reviewed=false;}}
    }
    internal void MoveRow(int delta){ApplyCurrent();RowIndex=Math.Clamp(RowIndex+delta,0,originals.Length-1);part=Math.Max(0,FirstPart(Row.Text));LoadCurrent();}
    internal void MovePart(int delta)
    {
        ApplyCurrent();var parts=SubtitleTools.TextParts(Row.Text);for(int i=part+delta;i>=0&&i<parts.Length;i+=delta)if(parts[i].Length>0&&!parts[i].StartsWith('{')){part=i;LoadCurrent();return;}Message.Text="已到当前句的正文片段边界。";
    }
    internal SpellingIssue? CurrentSpelling;string spellingSnapshot="";
    internal void FindSpelling()
    {
        CurrentSpelling=null;Suggestions.ItemsSource=null;var found=WindowsSpelling.Check(Editor.Text);
        int start=Editor.SelectionStart+Editor.SelectionLength;var error=found.FirstOrDefault(e=>e.Start>=start)??found.FirstOrDefault();
        if(error==null){Message.Text="已检查当前片段：未检出英语拼写问题（不代表语义正确）。";return;}
        CurrentSpelling=error;spellingSnapshot=Editor.Text;Editor.Select(error.Start,error.Length);Editor.Focus();Suggestions.ItemsSource=error.Suggestions.Select(s=>new SpellingSuggestion(s)).ToArray();Suggestions.SelectedIndex=0;Message.Text="选中建议后点击采用，或直接修改正文。";
    }
    internal void CorrectSpelling()
    {
        if(CurrentSpelling is not SpellingIssue error||Suggestions.SelectedItem is not SpellingSuggestion suggestion)return;
        if(Editor.Text!=spellingSnapshot)throw new InvalidOperationException("正文已改变，请重新检查后采用建议。");
        Editor.Select(error.Start,error.Length);Editor.SelectedText=suggestion.Replacement;CurrentSpelling=null;Suggestions.ItemsSource=null;
    }

}

internal sealed class ResampleDialog:SubtitleToolDialog
{
    internal readonly TextBox WidthInput,HeightInput;
    internal int TargetWidth=>int.Parse(WidthInput.Text,CultureInfo.InvariantCulture);
    internal int TargetHeight=>int.Parse(HeightInput.Text,CultureInfo.InvariantCulture);
    internal ResampleDialog(SubtitleDocument doc):base("分辨率重采样 · 等比例",520,400)
    {
        Hint($"当前脚本：{doc.PlayResX} × {doc.PlayResY}。同步缩放字号、边距、描边、阴影与静态 pos 坐标；不是修改视频分辨率。复杂动画/绘图/裁剪会整体拒绝，不会只改一半。");WidthInput=Input(Body,"目标宽度",doc.PlayResX.ToString());HeightInput=Input(Body,"目标高度",doc.PlayResY.ToString());
        Action("应用重采样",()=>{SubtitleTools.Resample(SubtitleTools.Copy(doc),TargetWidth,TargetHeight);DialogResult=true;});Finish();
    }
}

internal sealed class SelectLinesDialog:SubtitleToolDialog
{
    internal readonly TextBox Query;
    internal readonly ComboBox Field,Match,Mode;
    internal readonly CheckBox Case=new(){Content="区分大小写"},Dialogue=new(){Content="对白",IsChecked=true},Comments=new(){Content="注释",IsChecked=true},Negate=new(){Content="反向匹配（不符合条件）"};
    internal SelectLinesDialog(SubtitleDocument doc):base("选择字幕行",560,570)
    {
        Hint("按正文、说话人、样式或特效筛选，不改动字幕内容。正则表达式有超时保护。");Query=Input(Body,"查找内容");Field=Choice(Body,"字段",["字幕正文","样式","说话人","特效"]);Match=Choice(Body,"匹配方式",["包含","完全相同","正则表达式"]);Mode=Choice(Body,"选择方式",["替换当前选择","加入当前选择","从当前选择移除","与当前选择取交集"]);
        var flags=new WrapPanel();foreach(var check in new[]{Case,Dialogue,Comments,Negate})flags.Children.Add(check);Body.Children.Add(flags);Action("选择",()=>{_=Matches(doc);DialogResult=true;});Finish();
    }
    internal AssRow[] Matches(SubtitleDocument doc)=>SubtitleTools.Select(doc,Query.Text,(string)Field.SelectedItem,(string)Match.SelectedItem,Case.IsChecked==true,Dialogue.IsChecked==true,Comments.IsChecked==true,Negate.IsChecked==true);
}
