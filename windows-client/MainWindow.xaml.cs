using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Frameflow.Core;
using Frameflow.Media;

namespace Frameflow;
public partial class MainWindow:Window
{
    SubtitleDocument document=SubtitleDocument.New();AssRow? selected;
    string? subtitlePath,mediaPath;bool loading,dirty,draftDirty,playing,mediaReady,timerSeeking,scrubbing,composing;
    double duration=60,stopAt=double.PositiveInfinity;CancellationTokenSource? decodeCancel;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    readonly DispatcherTimer autosave=new(){Interval=TimeSpan.FromSeconds(30)};
    readonly Stack<Snapshot> undo=[],redo=[];
    sealed record Snapshot(string Text,(string Note,bool Reviewed)[] Notes,int Index);
    readonly string recoveryDir=Path.Combine(AppContext.BaseDirectory,"Recovery");
    readonly string sessionId=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N");
    bool ready,tableDirty;
    public MainWindow()
    {
        InitializeComponent();InitializeAppearance();ready=true;InitializeAssPreview();
        FontBox.ItemsSource=Fonts.SystemFontFamilies.Select(f=>f.Source).Order().ToArray();
        // IME completion can already be handled by a text control; observe it anyway.
        AddHandler(TextCompositionManager.PreviewTextInputStartEvent,new TextCompositionEventHandler((_,_)=>composing=true),true);
        AddHandler(TextCompositionManager.PreviewTextInputEvent,new TextCompositionEventHandler((_,_)=>composing=false),true);
        AddHandler(Keyboard.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((_,_)=>{composing=false;UpdateShortcutHint();}),true);
        Deactivated+=(_,_)=>composing=false;
        InitializeFineTiming();
        Wave.SeekRequested+=t=>{if(Linked.IsChecked==true)Seek(t);else{Wave.Position=t;Wave.InvalidateVisual();}};
        Wave.ZoomChanged+=()=>{ZoomLabel.Text=$"{Wave.Span:0.0} s · 滚轮缩放";ScheduleSpectrum();};
        Overlay.BeginMove+=()=>{if(selected==null||!ApplyEditor())return false;if(selected.Text.Contains(@"\move(")){Status.Text="此句含动态定位，拖动已禁用；请在正文中明确编辑 move 标签。";return false;}PushUndo();BeginAssSubtitleDrag();return true;};
        Overlay.Moved+=(x,y)=>{if(selected==null||assDragging&&selected!=assDragRow)return;try{AssTags.Set(selected,"pos",$"({x.ToString("0",CultureInfo.InvariantCulture)},{y.ToString("0",CultureInfo.InvariantCulture)})");selected.Reviewed=false;XBox.Text=x.ToString("0",CultureInfo.InvariantCulture);YBox.Text=y.ToString("0",CultureInfo.InvariantCulture);Overlay.InvalidateDocument();MoveAssSubtitlePreview();}catch(Exception ex){Status.Text=ex.Message;}};
        Overlay.EndMove+=EndAssSubtitleDrag;
        InitializePlayback();timer.Tick+=(_,_)=>Tick();autosave.Tick+=(_,_)=>AutoSave();autosave.Start();
        document.Add(0,2,"");RefreshAll(0);UpdateShortcutHint();Loaded+=(_,_)=>{if(!SmokeMode)ApplyDisplayOptions(DisplayOptions.Load(DisplayOptionsPath));UpdateVideoPresentation();};
    }
    void Guard(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"帧语",MessageBoxButton.OK,MessageBoxImage.Warning);Status.Text=ex.Message;}}
    void MarkDirty(){ScheduleAssPreview();dirty=true;tableDirty=true;Overlay.InvalidateDocument();UpdateTitle();}
    void UpdateTitle(){Title=$"{(dirty||draftDirty?"* ":"")}{Path.GetFileName(subtitlePath)??"未命名.ass"} — 帧语 Windows Alpha 0.2.0 · 新版界面";FileLabel.Text=Path.GetFileName(subtitlePath)??"未命名.ass";FileLabel.ToolTip=subtitlePath??"尚未保存的字幕";}
    Snapshot Capture()=>new(document.Serialize(),document.Rows.Select(r=>(r.Note,r.Reviewed)).ToArray(),selected?.Number-1??0);
    void PushUndo(){undo.Push(Capture());redo.Clear();if(undo.Count>150){var keep=undo.Take(100).Reverse().ToArray();undo.Clear();foreach(var s in keep)undo.Push(s);}}
    void Restore(Snapshot snapshot){document=SubtitleDocument.Parse(snapshot.Text);var rows=document.Rows.ToArray();for(int i=0;i<Math.Min(rows.Length,snapshot.Notes.Length);i++){rows[i].Note=snapshot.Notes[i].Note;rows[i].Reviewed=snapshot.Notes[i].Reviewed;}draftDirty=false;RefreshAll(snapshot.Index);MarkDirty();}
    void RefreshAll(int index=0){loading=true;document.Renumber();SubtitleGrid.ItemsSource=document.Rows.ToList();tableDirty=false;StyleBox.ItemsSource=document.Styles.Select(s=>s.Name).Distinct().ToArray();ActorBox.ItemsSource=document.Rows.Select(r=>r.Actor).Where(s=>s!="").Distinct().ToArray();selected=document.Rows.ElementAtOrDefault(Math.Clamp(index,0,Math.Max(0,document.Rows.Count()-1)));SubtitleGrid.SelectedItem=selected;loading=false;LoadEditor();RefreshIssues();UpdateTitle();}
    void RefreshRows(){bool wasLoading=loading;loading=true;SubtitleGrid.Items.Refresh();tableDirty=false;loading=wasLoading;CountLabel.Text=$"{document.Rows.Count()} 句 · 当前 #{selected?.Number??0}";}
    void LoadEditor()
    {
        CancelLiveTiming();CancelWaveEdit();StopFineAudition();
        loading=true;var row=selected;TextEditor.IsEnabled=row!=null;StartBox.IsEnabled=EndBox.IsEnabled=row!=null;
        if(row!=null){ActorBox.Text=row.Actor;if(!StyleBox.Items.Cast<string>().Contains(row.Style))StyleBox.ItemsSource=document.Styles.Select(s=>s.Name).Append(row.Style).Distinct().ToArray();StyleBox.SelectedItem=row.Style;LayerBox.Text=row.Layer;CommentBox.IsChecked=row.Comment;StartBox.Text=row.StartText;EndBox.Text=row.EndText;TextEditor.Text=row.Text.Replace(@"\N","\n");DurationLabel.Text=row.DurationText;NoteBox.Text=row.Note;
            var s=document.FindStyle(row);FontBox.Text=AssTags.Get(row,"fn",s?.Get("Fontname","Microsoft YaHei")??"Microsoft YaHei");SizeBox.Text=AssTags.Number(row,"fs",s?.Number("Fontsize",48)??48).ToString(CultureInfo.InvariantCulture);OutlineBox.Text=AssTags.Number(row,"bord",s?.Number("Outline",2)??2).ToString(CultureInfo.InvariantCulture);ColorBox.Text=AssTags.AssToHex(AssTags.Get(row,"c",s?.Get("PrimaryColour","&HFFFFFF")??"&HFFFFFF"));BoldBox.IsChecked=AssTags.Number(row,"b",s?.Number("Bold",0)??0)!=0;ItalicBox.IsChecked=AssTags.Number(row,"i",s?.Number("Italic",0)??0)!=0;Wave.Start=row.Start;Wave.End=row.End;
        }else{TextEditor.Text="";NoteBox.Text="";}
        Overlay.Document=document;Overlay.Selected=row;var p=row!=null?Overlay.Position(row):(0d,0d,2);XBox.Text=p.Item1.ToString("0",CultureInfo.InvariantCulture);YBox.Text=p.Item2.ToString("0",CultureInfo.InvariantCulture);loading=false;draftDirty=false;if(tableDirty)RefreshRows();CountLabel.Text=$"{SubtitleGrid.Items.Count} 句 · 当前 #{selected?.Number??0}";Wave.InvalidateVisual();Overlay.InvalidateDocument();ScheduleAssPreview();RefreshFineTiming(true);
    }
    bool ApplyEditor(bool report=true)
    {
        if(waveEditing)EndWaveEdit();
        if(loading||selected==null)return true;
        try
        {
            double start=Timecode.Parse(StartBox.Text),end=Timecode.Parse(EndBox.Text);if(end<=start)throw new FormatException("结束时间必须晚于开始时间。");
            if(!int.TryParse(LayerBox.Text,out var layer)||layer<0)throw new FormatException("层必须为非负整数。");string actor=ActorBox.Text,style=StyleBox.SelectedItem?.ToString()??selected.Style;
            if(actor.IndexOfAny([',','\r','\n'])>=0)throw new FormatException("说话人不能含逗号或换行。");string text=TextEditor.Text.Replace("\r\n",@"\N").Replace("\n",@"\N");
            bool changed=Math.Abs(selected.Start-start)>.004||Math.Abs(selected.End-end)>.004||selected.Text!=text||selected.Actor!=actor||selected.Style!=style||selected.Layer!=layer.ToString()||selected.Comment!=(CommentBox.IsChecked==true);
            if(changed){PushUndo();selected.Start=start;selected.End=end;selected.Text=text;selected.Actor=actor;selected.Style=style;selected.Layer=layer.ToString();selected.Comment=CommentBox.IsChecked==true;selected.Reviewed=false;MarkDirty();RefreshIssues();Overlay.InvalidateVisual();}
            Wave.Start=selected.Start;Wave.End=selected.End;Wave.InvalidateVisual();RefreshFineTiming(false);draftDirty=false;UpdateTitle();return true;
        }catch(Exception ex){if(report)MessageBox.Show(this,ex.Message,"不能提交",MessageBoxButton.OK,MessageBoxImage.Warning);return false;}
    }
    void DraftChanged(object sender,TextChangedEventArgs e){if(!ready||loading)return;draftDirty=true;UpdateTitle();ScheduleAssPreview();}
    void SelectRow(AssRow? row,bool seek=true){if(row==null)return;if(!ApplyEditor())return;loading=true;selected=row;SubtitleGrid.SelectedItems.Clear();SubtitleGrid.SelectedItem=row;SubtitleGrid.ScrollIntoView(row);loading=false;LoadEditor();EnsureWave(row.Start);if(seek)Seek(row.Start);}
    void RowSelected(object sender,SelectionChangedEventArgs e){if(loading||!ready)return;var next=SubtitleGrid.SelectedItem as AssRow;if(next==selected)return;if(!ApplyEditor()){loading=true;SubtitleGrid.SelectedItem=selected;loading=false;return;}selected=next;LoadEditor();if(next!=null){EnsureWave(next.Start);Seek(next.Start);}}
    void GridDoubleClick(object sender,MouseButtonEventArgs e){EditorTabs.SelectedIndex=0;TextEditor.Focus();}
    bool ConfirmDiscard(){if(!ApplyEditor())return false;if(!dirty&&!draftDirty)return true;var answer=MessageBox.Show(this,"当前字幕尚未保存。是否保存后继续？","帧语",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);return answer==MessageBoxResult.No || answer==MessageBoxResult.Yes&&Save(false);}
    public void OpenSubtitle(string path)
    {
        if(!ConfirmDiscard())return;var loaded=SubtitleDocument.Load(path);document=loaded;subtitlePath=Path.GetExtension(path).Equals(".srt",StringComparison.OrdinalIgnoreCase)?null:path;undo.Clear();redo.Clear();dirty=draftDirty=false;
        try{_ =ReviewStore.Load(path,document);}catch(Exception ex){Status.Text=ex.Message;MessageBox.Show(this,ex.Message,"校对记录",MessageBoxButton.OK,MessageBoxImage.Information);}
        RefreshAll();if(document.Warnings.Count>0)MessageBox.Show(this,string.Join('\n',document.Warnings.Take(12)),"部分内容仅原样保留");Status.Text=$"已打开 {Path.GetFileName(path)} · {document.Rows.Count()} 句";Remember(path,"subtitle");
    }
    void NewClick(object sender,RoutedEventArgs e){if(!ConfirmDiscard())return;document=SubtitleDocument.New();document.Add(0,2);subtitlePath=null;undo.Clear();redo.Clear();dirty=false;RefreshAll();}
    void OpenClick(object sender,RoutedEventArgs e){var dlg=new OpenFileDialog{Filter="字幕文件|*.ass;*.ssa;*.srt|所有文件|*.*"};if(dlg.ShowDialog(this)==true)Guard(()=>OpenSubtitle(dlg.FileName));}
    bool Save(bool saveAs)
    {
        if(!ApplyEditor())return false;string? path=subtitlePath;if(saveAs||path==null){var dialog=new SaveFileDialog{Filter=document.LegacySsa?"SSA 字幕（保留旧格式）|*.ssa":"ASS 字幕|*.ass",FileName=Path.GetFileName(path)??"未命名.ass",DefaultExt=document.LegacySsa?".ssa":".ass"};if(dialog.ShowDialog(this)!=true)return false;path=dialog.FileName;}
        try{SaveTo(path);return true;}catch(Exception ex){MessageBox.Show(this,"保存失败："+ex.Message);return false;}
    }
    void SaveClick(object sender,RoutedEventArgs e)=>Save(false);void SaveAsClick(object sender,RoutedEventArgs e)=>Save(true);
    void ExportClick(object sender,RoutedEventArgs e){if(!ApplyEditor())return;if(MessageBox.Show(this,"SRT 不保留样式、说话人、层和 ASS 特效。继续导出副本？","格式提示",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;var dlg=new SaveFileDialog{Filter="SRT 字幕|*.srt",DefaultExt=".srt"};if(dlg.ShowDialog(this)==true)Guard(()=>{SubtitleDocument.AtomicSave(dlg.FileName,Encoding.UTF8.GetBytes(document.ExportSrt()));Status.Text="已导出 SRT 副本，当前 ASS 项目未改变。";});}
    void UndoClick(object sender,RoutedEventArgs e){if(!ApplyEditor())return;if(undo.Count==0)return;redo.Push(Capture());Restore(undo.Pop());}
    void RedoClick(object sender,RoutedEventArgs e){if(!ApplyEditor())return;if(redo.Count==0)return;undo.Push(Capture());Restore(redo.Pop());}
    void AddClick(object sender,RoutedEventArgs e){if(!ApplyEditor())return;PushUndo();var start=selected?.End??PlaybackPosition;var row=document.Add(start,start+2,"",selected);MarkDirty();RefreshAll(row.Number-1);Seek(row.Start);TextEditor.Focus();}
    void DeleteClick(object sender,RoutedEventArgs e){if(!ApplyEditor())return;var rows=SelectedRows();if(rows.Length==0)return;PushUndo();int index=rows[0].Number-1;foreach(var row in rows)document.Remove(row);MarkDirty();RefreshAll(index);}
    void Next(bool create=false){if(!ApplyEditor())return;var list=document.Rows.ToList();int i=selected==null?-1:list.IndexOf(selected);if(i+1<list.Count)SelectRow(list[i+1]);else if(create)AddClick(this,new RoutedEventArgs());}
    void PreviousClick(object sender,RoutedEventArgs e){if(selected!=null)SelectRow(document.Rows.ElementAtOrDefault(selected.Number-2));}
    void NextClick(object sender,RoutedEventArgs e)=>Next();void CommitNextClick(object sender,RoutedEventArgs e){Next(true);TextEditor.Focus();}
    void SetStartClick(object sender,RoutedEventArgs e){if(selected==null)return;StartBox.Text=Timecode.Ass(PlaybackPosition);if(ApplyEditor())LoadEditor();}
    void SetEndClick(object sender,RoutedEventArgs e){if(selected==null)return;EndBox.Text=Timecode.Ass(PlaybackPosition);if(ApplyEditor())LoadEditor();}
    void StyleClick(object sender,RoutedEventArgs e){if(ApplyEditor()){LoadEditor();EditorTabs.SelectedIndex=1;}}
    void CreateClick(object sender,RoutedEventArgs e){EditorTabs.SelectedIndex=0;TextEditor.Focus();}
    void ReviewClick(object sender,RoutedEventArgs e){if(ApplyEditor()){RefreshIssues();EditorTabs.SelectedIndex=2;}}
    void NewStyleClick(object sender,RoutedEventArgs e){if(!ApplyEditor())return;var value=Prompt("新增样式","样式名称");if(value==null)return;Guard(()=>{PushUndo();document.AddStyle(value);MarkDirty();RefreshAll(selected?.Number-1??0);Status.Text="已新增独立样式："+value;});}
    void ApplyStyleClick(object sender,RoutedEventArgs e)
    {
        if(selected==null||!ApplyEditor())return;Guard(()=>{double size=double.Parse(SizeBox.Text,CultureInfo.InvariantCulture),outline=double.Parse(OutlineBox.Text,CultureInfo.InvariantCulture);if(!double.IsFinite(size)||size<1||size>500||!double.IsFinite(outline)||outline<0||outline>20)throw new FormatException("字号范围 1–500，描边范围 0–20。");string color=AssTags.HexToAss(ColorBox.Text),font=FontBox.Text;if(font.IndexOfAny([',','\r','\n','{','}','\\'])>=0)throw new FormatException("字体名含不允许的字符。");
            if(document.LegacySsa)throw new InvalidOperationException("旧 SSA 样式本版只保留，请使用 ASS 项目编辑排版。");
            if(ScopeBox.SelectedIndex==1){var s=document.FindStyle(selected)??throw new InvalidOperationException("找不到样式。");int count=document.Rows.Count(r=>r.Style==s.Name);if(MessageBox.Show(this,$"将修改共享样式「{s.Name}」，影响 {count} 句。已有行内覆盖仍然优先。继续？","修改共享样式",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;PushUndo();s.Set("Fontname",font);s.Set("Fontsize",size.ToString(CultureInfo.InvariantCulture));s.Set("Outline",outline.ToString(CultureInfo.InvariantCulture));s.Set("PrimaryColour",color.Replace("&H","&H00").TrimEnd('&'));s.Set("Bold",BoldBox.IsChecked==true?"-1":"0");s.Set("Italic",ItalicBox.IsChecked==true?"-1":"0");foreach(var r in document.Rows.Where(r=>r.Style==s.Name))r.Reviewed=false;}
            else{PushUndo();AssTags.Set(selected,"fn",font);AssTags.Set(selected,"fs",size.ToString(CultureInfo.InvariantCulture));AssTags.Set(selected,"bord",outline.ToString(CultureInfo.InvariantCulture));AssTags.Set(selected,"c",color);AssTags.Set(selected,"b",BoldBox.IsChecked==true?"1":"0");AssTags.Set(selected,"i",ItalicBox.IsChecked==true?"1":"0");selected.Reviewed=false;}
            MarkDirty();LoadEditor();RefreshIssues();Status.Text="已应用样式；可撤销。";});
    }
    void AnchorClick(object sender,RoutedEventArgs e){if(selected==null||!ApplyEditor())return;Guard(()=>{int a=int.Parse(((Button)sender).Tag.ToString()!);double x=((a-1)%3) switch{0=>60d,1=>document.PlayResX/2d,_=>document.PlayResX-60d};double y=a<=3?document.PlayResY-60:a<=6?document.PlayResY/2d:60;PushUndo();AssTags.Set(selected,"pos",$"({x.ToString("0",CultureInfo.InvariantCulture)},{y.ToString("0",CultureInfo.InvariantCulture)})");AssTags.Set(selected,"an",a.ToString());selected.Reviewed=false;MarkDirty();LoadEditor();Seek(selected.Start);});}
    void ApplyPositionClick(object sender,RoutedEventArgs e){if(selected==null||!ApplyEditor())return;Guard(()=>{double x=double.Parse(XBox.Text,CultureInfo.InvariantCulture),y=double.Parse(YBox.Text,CultureInfo.InvariantCulture);if(!double.IsFinite(x)||!double.IsFinite(y))throw new FormatException("无效坐标。");PushUndo();AssTags.Set(selected,"pos",$"({x.ToString("0",CultureInfo.InvariantCulture)},{y.ToString("0",CultureInfo.InvariantCulture)})");selected.Reviewed=false;MarkDirty();LoadEditor();Seek(selected.Start);});}
    static string? Prompt(string title,string label){var w=new Window{Title=title,Width=360,Height=155,WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize};var p=new StackPanel{Margin=new Thickness(12)};p.Children.Add(new TextBlock{Text=label});var t=new TextBox();p.Children.Add(t);var b=new Button{Content="确定",IsDefault=true};b.Click+=(_,_)=>w.DialogResult=true;p.Children.Add(b);w.Content=p;return w.ShowDialog()==true?t.Text:null;}
    void RefreshIssues()
    {
        double gap=double.TryParse(GapThreshold.Text,out var g)&&g>=0?g:120,shortMs=double.TryParse(ShortThreshold.Text,out var s)&&s>=0?s:500;
        var issues=Quality.Scan(document,new QualityOptions(gap,shortMs));IssueList.ItemsSource=issues;ReviewModeButton.Content=$"校对 {issues.Count}";GapSummary.Text=issues.Any(i=>i.Kind=="疑似闪轴")?$"  ⚠ {issues.Count(i=>i.Kind=="疑似闪轴")} 处疑似闪轴":"";Wave.Gaps=issues.Where(i=>i.Kind=="疑似闪轴"&&i.Next!=null).Select(i=>(i.Row.End,i.Next!.Start)).ToArray();Wave.InvalidateVisual();RefreshRows();
    }
    void ScanClick(object sender,RoutedEventArgs e){if(ApplyEditor())RefreshIssues();}
    void IssueSelected(object sender,SelectionChangedEventArgs e){if(IssueList.SelectedItem is Issue issue){SelectRow(issue.Row);EditorTabs.SelectedIndex=2;}}
    void FixGapClick(object sender,RoutedEventArgs e){if(IssueList.SelectedItem is not Issue issue||issue.Kind!="疑似闪轴"){Status.Text="请先选择一项疑似闪轴。";return;}if(!ApplyEditor())return;PushUndo();Quality.FixGap(issue);MarkDirty();LoadEditor();RefreshIssues();Status.Text="已延长前句至下一句起点；可撤销。";}
    void IssuePlayClick(object sender,RoutedEventArgs e){if(IssueList.SelectedItem is Issue issue)PlayRange(Math.Max(0,issue.Row.Start-.5),(issue.Next?.End??issue.Row.End)+.5);}
    void ReviewedClick(object sender,RoutedEventArgs e){if(selected==null||!ApplyEditor())return;PushUndo();selected.Note=NoteBox.Text;selected.Reviewed=true;MarkDirty();RefreshIssues();Status.Text="已记录人工校对状态；自动问题仍保留供复核。";}
    void NoteLostFocus(object sender,RoutedEventArgs e){if(loading||selected==null||selected.Note==NoteBox.Text)return;PushUndo();selected.Note=NoteBox.Text;MarkDirty();}
    void MediaClick(object sender,RoutedEventArgs e){var dlg=new OpenFileDialog{Filter="视频 / 音频|*.mp4;*.m4v;*.mov;*.mkv;*.avi;*.wmv;*.wav;*.mp3;*.m4a;*.aac;*.wma|所有文件|*.*"};if(dlg.ShowDialog(this)==true)OpenMedia(dlg.FileName);}
    public async void OpenMedia(string path)
    {
        Pause();dummyVideo=false;dummyClock.Reset();externalAudioPath=null;audioClosed=false;audioLoadCancel?.Cancel();AudioOnly.IsEnabled=true;Wave.Spectrum=null;videoZoom=aspectOverride=0;VideoSurface.Background=new SolidColorBrush(Color.FromRgb(20,29,40));duration=60;timingFrames=null;CloseAudio();ApplyAudioVolume();decodeCancel?.Cancel();decodeCancel=new CancellationTokenSource();var token=decodeCancel.Token;Pause();mediaReady=false;Player.Close();Player.Source=null;mediaPath=path;MediaLabel.Text=Path.GetFileName(path);MediaPlaceholder.Text="正在加载媒体…";MediaPlaceholder.Visibility=Visibility.Visible;Player.Source=new Uri(Path.GetFullPath(path));Player.Play();Player.Pause();Wave.Peaks=[];RefreshFineTiming(true);WaveStatus.Text=" · 正在解码音轨（可继续编辑）";pendingAudioPosition=selected?.Start??0;
        try{var progress=new Progress<double>(sec=>{if(!token.IsCancellationRequested)WaveStatus.Text=$" · 已分析 {sec:0} 秒";});var result=await Task.Run(()=>AudioPeaks.Decode(path,token,progress),token);if(token.IsCancellationRequested)return;Wave.Peaks=result.Peaks;duration=Math.Max(duration,result.Duration);Wave.Duration=duration;WaveScroll.Maximum=duration;SeekBar.Maximum=duration;WaveStatus.Text=$" · {result.SampleRate} Hz / {result.Channels} 声道";Wave.InvalidateVisual();RefreshFineTiming(true);Status.Text="真实音频波形已生成；左键设起点，右键设终点，Shift+单击定位。";if(AudioMode)PrepareAudio();}
        catch(OperationCanceledException){}catch(Exception ex){if(token.IsCancellationRequested)return;WaveStatus.Text=" · 音轨解码失败";Status.Text="系统解码器不支持此媒体或没有音轨；可另开 PCM WAV。";MessageBox.Show(this,"无法生成音频波形："+ex.Message+"\n\n可使用 PCM WAV / 常见 MP4 AAC；当前版本依赖 Windows 系统解码器。","媒体兼容性",MessageBoxButton.OK,MessageBoxImage.Information);}
    }
    void MediaOpened(object sender,RoutedEventArgs e){mediaReady=true;if(mediaPath!=null){Remember(mediaPath,"media");if(Player.NaturalVideoWidth>0)RememberMedia(mediaPath,"video");}if(externalAudioPath==null)duration=Player.NaturalDuration.HasTimeSpan?Player.NaturalDuration.TimeSpan.TotalSeconds:60;SeekBar.Maximum=duration;WaveScroll.Maximum=duration;Wave.Duration=duration;sourceVideoWidth=Player.NaturalVideoWidth>0?Player.NaturalVideoWidth:document.PlayResX;sourceVideoHeight=Player.NaturalVideoHeight>0?Player.NaturalVideoHeight:document.PlayResY;UpdateVideoPresentation();ApplyAudioVolume();MediaPlaceholder.Visibility=Player.NaturalVideoWidth>0?Visibility.Collapsed:Visibility.Visible;MediaPlaceholder.Text="音频模式 · 字幕预览";if(AudioMode){Player.Position=TimeSpan.FromSeconds(selected?.Start??0);Overlay.Time=selected?.Start??0;Overlay.InvalidateDocument();}Seek(selected?.Start??0);}
    void MediaFailed(object sender,ExceptionRoutedEventArgs e){mediaReady=false;Pause();MediaPlaceholder.Visibility=Visibility.Visible;MediaPlaceholder.Text="媒体播放失败 · 请尝试常见 MP4 / WAV";Status.Text=e.ErrorException?.Message??"系统解码器不支持此文件。";}
    void MediaEnded(object sender,RoutedEventArgs e){if(!RestartFineAudition())Pause();}
    void EnsureWave(double t){if(t<Wave.ViewStart||t>Wave.ViewStart+Wave.Span){Wave.ViewStart=Math.Max(0,t-Wave.Span*.2);WaveScroll.Value=Wave.ViewStart;Wave.InvalidateVisual();}}
    void PlayClick(object sender,RoutedEventArgs e){if(playing)Pause();else PlayRange(PlaybackPosition,duration);}
    void PlaySelectionClick(object sender,RoutedEventArgs e){if(selected!=null&&ApplyEditor())PlayRange(selected.Start,selected.End);}
    void PlayBeginningClick(object sender,RoutedEventArgs e){if(selected!=null)PlayRange(selected.Start,Math.Min(selected.Start+.5,selected.End));}
    void PlayEndingClick(object sender,RoutedEventArgs e){if(selected!=null)PlayRange(Math.Max(selected.Start,selected.End-.5),selected.End);}
    void StopClick(object sender,RoutedEventArgs e)=>Pause();
    void SeekChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!ready||timerSeeking)return;Seek(e.NewValue);}
    void SeekDown(object sender,MouseButtonEventArgs e)=>scrubbing=true;void SeekUp(object sender,MouseButtonEventArgs e){scrubbing=false;Seek(SeekBar.Value);}
    void SpeedChanged(object sender,SelectionChangedEventArgs e){if(!ready)return;if(dummyVideo&&dummyClock.IsRunning){dummyPosition=VideoPositionSeconds;dummyClock.Restart();}Player.SpeedRatio=AudioPlayer.SpeedRatio=SpeedBox.SelectedIndex switch{0=>.75,2=>1.25,_=>1};}
    void WaveScrollChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!ready)return;Wave.ViewStart=e.NewValue;Wave.InvalidateVisual();ScheduleSpectrum();}
    void AutoSave(){if(SmokeMode||waveEditing||!dirty&&!draftDirty)return;try{if(!ApplyEditor(false))return;Directory.CreateDirectory(recoveryDir);string file=Path.Combine(recoveryDir,sessionId+".ass");SubtitleDocument.AtomicSave(file,document.Bytes(),false);ReviewStore.Save(file,document,mediaPath);Status.Text="恢复副本已更新（未覆盖原字幕）。";}catch(Exception ex){Status.Text="恢复副本写入失败："+ex.Message;}}
    void RecoveryClick(object sender,RoutedEventArgs e)=>Guard(()=>{Directory.CreateDirectory(recoveryDir);Process.Start(new ProcessStartInfo("explorer.exe",recoveryDir){UseShellExecute=true});});
    void WindowClosing(object? sender,CancelEventArgs e){if(!SmokeMode&&!ConfirmDiscard()){e.Cancel=true;return;}DisposeMediaMenuResources();decodeCancel?.Cancel();StopPlaybackRendering();timer.Stop();autosave.Stop();Player.Close();CloseAudio();}
    void WindowDrop(object sender,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is not string[] files||files.Length==0)return;string path=files[0];if(new[]{".ass",".ssa",".srt"}.Contains(Path.GetExtension(path).ToLowerInvariant()))Guard(()=>OpenSubtitle(path));else OpenMedia(path);}
    void HelpClick(object sender,RoutedEventArgs e)=>MessageBox.Show(this,"常用 Aegisub 键位（按焦点生效）\n\n编辑正文：Enter 提交下一句；Shift+Enter 换行。\n非文字输入区域（含列表、播放按钮）：S/Space/R 播放选段，H 停止，G 提交下一句。\nQ/W/E/D：起点前 / 终点后 / 起点后 / 终点前 500ms。\nZ/X：上一句 / 下一句；波形内左右键切句；A/F：滚动波形。\nCtrl+I：时间平移；Ctrl+5：贴齐关键帧场景；Ctrl+6：整体对齐视频。\nCtrl+3/4：用播放位置设置起点 / 终点。\nCtrl+1/2：视频定位句首 / 句尾；Ctrl+G 跳转；Ctrl+P 播放/暂停。\nF11 全屏工作区；Esc 恢复。\nCtrl+S 或 F2 保存；Ctrl+Shift+S 另存；工具栏撤销修改。\n小键盘（NumLock 开启、非输入框）：5 播放、8 停止、1/3 前后试听、0/2 切句。\n正文、说话人、时间、批注等输入区：字母不触发打轴；Ctrl+P 播放暂停。\n\n当前为 Alpha：媒体依赖 Windows 解码器；精确 ASS 模式使用 FFmpeg/libass 渲染动画、卡拉 OK、遮罩；轻量模式只用于基础排版。预览按源尺寸渲染后缩至最高 720p / 30fps，系统视频定位与颜色显示仍可能和成片不同，可用成片对照检查。\n尚未实现逐帧精确 VFR 定位、全部键位自定义、完整 Automation 4 插件兼容。自动化菜单已提供受限 Lua 宏、预览和撤销；时间菜单支持卡拉 OK 计时转移。\n\n自动恢复在程序旁 Recovery 文件夹；保存前生成备份。","快捷键与限制",MessageBoxButton.OK,MessageBoxImage.Information);
}
