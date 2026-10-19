using System.Globalization;
using System.Text.RegularExpressions;

namespace Frameflow.Core;

// These operations run against a staged document. The window commits only after
// the complete operation succeeds, so a bad row cannot cause a partial edit.
public static class SubtitleTools
{
    static readonly CultureInfo CI=CultureInfo.InvariantCulture;
    static AssRow Clone(AssRow r)=>RowTransfer.Row(new(r.Format,r.Values,r.Comment,r.Note));
    static RowPacket Packet(IEnumerable<AssRow> rows)=>new(1,rows.Select(r=>new RowData(r.Format,r.Values,r.Comment,r.Note)).ToArray(),[]);
    static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static SubtitleDocument Copy(SubtitleDocument source)
    {
        var copy=SubtitleDocument.Parse(source.Serialize());var rows=source.Rows.ToArray();int i=0;
        foreach(var row in copy.Rows){row.Note=rows[i].Note;row.Reviewed=rows[i++].Reviewed;}
        return copy;
    }
    public static AssRow[] Execute(SubtitleDocument doc,AssRow[] selected,string op,double position=0)
    {
        var all=doc.Rows.ToArray();selected=selected.OrderBy(r=>r.Number).ToArray();
        if(op.StartsWith("insert-",StringComparison.Ordinal))
        {
            bool after=op.Contains("after"),video=op.EndsWith("-video");int index=selected.Length==0?all.Length:selected[0].Number-1+(after?1:0);
            var temp=SubtitleDocument.New();var blank=temp.Add(video?position:0,(video?position:0)+5);blank.Style=selected.FirstOrDefault()?.Style??doc.Styles.FirstOrDefault()?.Name??"Default";
            return RowTransfer.Insert(doc,Packet([blank]),index);
        }
        if(op.StartsWith("sort-",StringComparison.Ordinal))
        {
            var parts=op.Split('-');var targets=parts[1]=="all"?all:selected;Need(targets.Length>0,"请先选择字幕行。");
            Func<AssRow,IComparable> key=parts[2] switch{"start"=>r=>r.Start,"end"=>r=>r.End,"layer"=>r=>int.TryParse(r.Layer,out var n)?n:0,"style"=>r=>r.Style,"actor"=>r=>r.Actor,"effect"=>r=>r.Effect,_=>throw new ArgumentException("未知排序字段")};
            var sorted=targets.OrderBy(key,Comparer<IComparable>.Create((a,b)=>a is string sa&&b is string sb?StringComparer.OrdinalIgnoreCase.Compare(sa,sb):a.CompareTo(b))).ToArray();
            Reorder(doc,targets,sorted);return sorted;
        }
        Need(selected.Length>0,"请先选择字幕行。");
        if(op=="duplicate")return RowTransfer.Insert(doc,Packet(selected),selected[^1].Number);
        if(op=="swap"){Need(selected.Length==2,"交换需要恰好选择两行（可按 Ctrl 多选）。");Reorder(doc,selected,[selected[1],selected[0]]);return selected;}
        if(op=="delete"){foreach(var row in selected)doc.Remove(row);return doc.Rows.Skip(Math.Min(selected[0].Number-1,Math.Max(0,doc.Rows.Count()-1))).Take(1).ToArray();}
        if(op.StartsWith("split-",StringComparison.Ordinal)&&op!="split-karaoke")
        {
            // MediaElement has no VFR frame index: explicitly a centisecond split.
            position=Math.Round(position,2,MidpointRounding.AwayFromZero);
            Need(selected.All(r=>r.Start<position&&position<r.End),"播放点必须位于每个所选行的起止时间之间（至少保留 0.01 秒）。");
            var result=new List<AssRow>();foreach(var row in selected){var next=Clone(row);next.Start=position;row.End=position;row.Reviewed=false;var added=RowTransfer.Insert(doc,Packet([next]),row.Number)[0];result.Add(op=="split-before"?row:added);}return result.ToArray();
        }
        if(op.StartsWith("join-",StringComparison.Ordinal))
        {
            Need(selected.Length>=2,"合并需要至少两行。");var first=selected[0];string text;
            if(op=="join-karaoke")
            {
                Need(selected.All(r=>!Regex.IsMatch(r.Text,@"\\(?:[kK][fo]?|kt)\d")),"请先拆分已有卡拉 OK 标签，避免叠加计时。");
                double end=selected[0].Start;text="";
                foreach(var row in selected){Need(row.Start>=end&&row.End>row.Start,"卡拉 OK 合并要求按时间排序且互不重叠。");if(row.Start>end)text+="{\\k"+Cs(row.Start-end)+"}";text+="{\\k"+Cs(row.End-row.Start)+"}"+row.Text;end=row.End;}
            }
            else text=op=="join-first"?first.Text:string.Join(" ",selected.Select(r=>r.Text));
            first.Start=selected.Min(r=>r.Start);first.End=selected.Max(r=>r.End);first.Text=text;first.Note=string.Join("\n",selected.Select(r=>r.Note).Where(s=>s.Length>0).Distinct());first.Reviewed=false;
            foreach(var row in selected.Skip(1))doc.Remove(row);return [first];
        }
        if(op=="split-karaoke")
        {
            // Validate all rows first; the outer staged transaction provides atomicity.
            var parts=selected.Select(r=>(Row:r,Parts:KaraokeParts(r))).ToArray();var result=new List<AssRow>();
            foreach(var item in parts){int index=item.Row.Number-1;doc.Remove(item.Row);result.AddRange(RowTransfer.Insert(doc,Packet(item.Parts),index));}return result.ToArray();
        }
        if(op=="recombine")return Recombine(doc,selected);
        throw new ArgumentException("未知字幕操作："+op);
    }
    static int Cs(double seconds)=>(int)Math.Round(seconds*100,MidpointRounding.AwayFromZero);
    static void Reorder(SubtitleDocument doc,AssRow[] slots,AssRow[] ordered)
    {
        var map=slots.Select((r,i)=>(r,To:ordered[i])).ToDictionary(p=>p.r,p=>p.To);var records=new List<Record>();
        foreach(var record in doc.Records)
        {
            if(record.Row is not AssRow old||!map.TryGetValue(old,out var next)||old==next){records.Add(record);continue;}
            // Explicit formats protect files with custom or changing event schemas.
            records.Add(new(){Raw="Format: "+string.Join(", ",next.Format)});records.Add(new(){Row=next});records.Add(new(){Raw="Format: "+string.Join(", ",old.Format)});
        }
        doc.Records.Clear();doc.Records.AddRange(records);doc.Renumber();
    }
    public static AssRow[] KaraokeParts(AssRow row)
    {
        Need(!Regex.IsMatch(row.Text,@"\\(?:kt|t|p|move|fad|fade)(?:\d|\()"),"包含绝对卡拉 OK、绘图或动画的行不自动拆分，以免改变特效时间。");
        var tokens=Regex.Matches(row.Text,@"\{[^}]*\}|[^{}]+");var output=new List<AssRow>();string active="",body="";int? length=null;double time=row.Start;
        void Flush(){if(length==null)return;double end=time+length.Value/100d;if(Regex.Replace(body,@"\{[^}]*\}","").Length>0){Need(length>0,"含文字的音节时长必须大于零。");Need(end<=row.End+.005,"卡拉 OK 音节总时长超过行结束时间。");var part=Clone(row);part.Start=time;part.End=end;part.Text=body;output.Add(part);}time=end;body="";}
        foreach(Match token in tokens)
        {
            string value=token.Value;if(value.StartsWith('{'))
            {
                string content=value[1..^1];int start=0;
                foreach(Match k in Regex.Matches(content,@"\\(?:[kK]|kf|ko)(\d+)"))
                {
                    string before=content[start..k.Index];if(before.Length>0){active+="{"+before+"}";if(length!=null)body+="{"+before+"}";}
                    Flush();length=int.Parse(k.Groups[1].Value,CI);body=active;start=k.Index+k.Length;
                }
                string rest=content[start..];if(rest.Length>0){active+="{"+rest+"}";if(length!=null)body+="{"+rest+"}";}
            }
            else {Need(length!=null||string.IsNullOrWhiteSpace(value),"首个卡拉 OK 标签前含正文，无法确定该段时长。");if(length!=null)body+=value;}
        }
        Flush();Need(output.Count>0,"所选行没有可拆分的 \\k / \\K / \\kf / \\ko 音节。");return output.ToArray();
    }
    static AssRow[] Recombine(SubtitleDocument doc,AssRow[] rows)
    {
        Need(rows.Length>=2,"重组需要至少两行。");Need(rows.All(r=>!r.Text.Contains('{')),"基础重组仅处理无 ASS 标签的重复文本，复杂特效不会自动改写。");
        var first=rows[0];Need(rows.All(r=>r.Style==first.Style&&r.Actor==first.Actor&&r.Layer==first.Layer&&r.Comment==first.Comment&&r.Effect==first.Effect&&r.Format.SequenceEqual(first.Format)&&r.Format.Where(f=>!new[]{"Start","End","Text"}.Contains(f,StringComparer.OrdinalIgnoreCase)).All(f=>r.Get(f)==first.Get(f))),"重组要求样式、说话人、层及其他字段一致。");
        var segments=rows.SelectMany(r=>r.Text.Split(@"\N").Select(t=>(Text:t,Row:r))).ToArray();Need(segments.All(p=>p.Text.Length>0),"含空白段的行不自动重组。");
        var result=new List<AssRow>();bool joined=false;
        foreach(var group in segments.GroupBy(p=>p.Text))
        {
            AssRow? current=null;foreach(var p in group.OrderBy(p=>p.Row.Start))
            {
                if(current!=null&&p.Row.Start<=current.End+.011){current.End=Math.Max(current.End,p.Row.End);current.Note=string.Join("\n",new[]{current.Note,p.Row.Note}.Where(s=>s.Length>0).Distinct());joined=true;}
                else {current=Clone(p.Row);current.Text=p.Text;result.Add(current);}
            }
        }
        Need(joined,"没有相邻或重叠的相同文字片段可重组。");int index=rows[0].Number-1;foreach(var row in rows)doc.Remove(row);return RowTransfer.Insert(doc,Packet(result.OrderBy(r=>r.Start)),index);
    }
    public static AssRow[] Select(SubtitleDocument doc,string query,string field,string match,bool caseSensitive,bool dialogue,bool comments,bool negate)
    {
        Need(dialogue||comments,"请至少选择对白或注释。");var comparison=caseSensitive?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
        Regex? regex=match=="正则表达式"?new Regex(query,caseSensitive?RegexOptions.None:RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)):null;
        return doc.Rows.Where(r=>r.Comment?comments:dialogue).Where(r=>{string s=field switch{"样式"=>r.Style,"说话人"=>r.Actor,"特效"=>r.Effect,_=>r.Text};bool found=regex!=null?regex.IsMatch(s):match=="完全相同"?s.Equals(query,comparison):s.Contains(query,comparison);return negate?!found:found;}).ToArray();
    }
    public static void RenameStyle(SubtitleDocument doc,AssStyle style,string name)
    {
        Need(!string.IsNullOrWhiteSpace(name)&&name.IndexOfAny([',','\r','\n','\\','{','}'])<0,"样式名不能为空或含逗号、换行、反斜杠、花括号。");Need(!doc.Styles.Any(s=>s!=style&&s.Name==name),"样式名称已存在。");string old=style.Name;style.Set("Name",name);
        foreach(var row in doc.Rows){if(row.Style==old)row.Style=name;row.Text=Regex.Replace(row.Text,@"\{[^}]*\}",m=>Regex.Replace(m.Value,@"\\r"+Regex.Escape(old)+@"(?=\\|\})",_=>@"\r"+name));}
    }
    public static void DeleteStyle(SubtitleDocument doc,AssStyle style)
    {
        Need(doc.Styles.Count()>1,"至少保留一个样式。");Need(!doc.Rows.Any(r=>r.Style==style.Name||Regex.IsMatch(r.Text,@"\\r"+Regex.Escape(style.Name)+@"(?=\\|\})")),"此样式仍被字幕或 \\r 标签引用，请先更换这些行的样式。");doc.Records.RemoveAll(r=>r.Style==style);
    }
    public static void Resample(SubtitleDocument doc,int width,int height)
    {
        Need(!doc.LegacySsa,"分辨率重采样暂支持 ASS，旧 SSA 保持原样。");Need(width>=16&&height>=16&&width<=16384&&height<=16384,"分辨率须在 16～16384 范围内。");
        double scale=width/(double)doc.PlayResX;Need(Math.Abs(scale-height/(double)doc.PlayResY)<.000001,"本版仅支持等比例缩放；请保持原宽高比。");
        Need(doc.Warnings.Count==0,"文件含无法解析的行；为避免部分重采样，未作修改。");
        Need(!doc.Records.Any(r=>Regex.IsMatch(r.Raw,@"^\s*LayoutRes[XY]\s*:",RegexOptions.IgnoreCase)),"含独立 LayoutRes 排版分辨率的文件暂不重采样。");
        Need(!doc.Records.Any(r=>Regex.IsMatch(r.Raw,@"^\s*ScaledBorderAndShadow\s*:\s*no\s*$",RegexOptions.IgnoreCase)),"当前文件未启用描边阴影随脚本缩放，本版保持原样。");
        foreach(var row in doc.Rows)
        {
            // Explicitly refuse syntax whose coordinate/time semantics we cannot preserve.
            Need(!Regex.IsMatch(row.Text,@"\\(?:p[1-9]|clip|iclip|t\(|move\(|org\(|fsc[xy]|fsp|blur|be|xbord|ybord|xshad|yshad)"),"遇到绘图、裁剪、动画或高级缩放标签，本版不会冒险重采样。请在 Aegisub 中处理该文件。");
            Need(string.IsNullOrEmpty(row.Effect),"含 Effect 特效的行暂不重采样。");
            Need(Regex.Replace(row.Text,@"\{[^}]*\}","").IndexOfAny(['{','}'])<0,"ASS 标签花括号不完整，未重采样。");
            foreach(Match block in Regex.Matches(row.Text,@"\{[^}]*\}"))foreach(Match tag in Regex.Matches(block.Value,@"\\[^\\}]*"))
            {
                // A conservative allow-list is intentional: unknown geometry must not
                // be silently retained at the wrong scale in a transformed script.
                Need(Regex.IsMatch(tag.Value,@"^\\(?:(?:fs|bord|shad)-?\d+(?:\.\d+)?|pos\(\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?\s*\)|an[1-9]|[bius]-?\d+|q[0-3]|(?:[kK]|kf|ko)\d+|(?:[1-4]?c|[1-4]a|alpha)&H[0-9a-fA-F]+&?|(?:r|fn)[^{}\\]*)$"),"包含暂不支持重采样的标签："+tag.Value);
            }
        }
        string Scale(string s,bool integer=false){Need(double.TryParse(s,NumberStyles.Float,CI,out double n)&&double.IsFinite(n),"无法缩放的数值："+s);return integer?Math.Round(n*scale,MidpointRounding.AwayFromZero).ToString(CI):(n*scale).ToString("0.###",CI);}
        foreach(var style in doc.Styles)foreach(string field in new[]{"Fontsize","Spacing","Outline","Shadow","MarginL","MarginR","MarginV"})if(style.Format.Contains(field,StringComparer.OrdinalIgnoreCase))style.Set(field,Scale(style.Get(field),field.StartsWith("Margin")));
        foreach(var row in doc.Rows)
        {
            foreach(string field in new[]{"MarginL","MarginR","MarginV"})if(row.Format.Contains(field,StringComparer.OrdinalIgnoreCase))row.Set(field,Scale(row.Get(field),true));
            row.Text=Regex.Replace(row.Text,@"\{[^}]*\}",block=>{
                var value=Regex.Replace(block.Value,@"\\(fs|bord|shad)(-?\d+(?:\.\d+)?)(?=\\|\})",m=>"\\"+m.Groups[1].Value+Scale(m.Groups[2].Value));
                return Regex.Replace(value,@"\\pos\(\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\)",m=>"\\pos("+Scale(m.Groups[1].Value)+","+Scale(m.Groups[2].Value)+")");});row.Reviewed=false;
        }
        SetInfo(doc,"PlayResX",width.ToString(CI));SetInfo(doc,"PlayResY",height.ToString(CI));
    }
    static void SetInfo(SubtitleDocument doc,string key,string value)
    {
        int header=doc.Records.FindIndex(r=>r.Raw.Trim().Equals("[Script Info]",StringComparison.OrdinalIgnoreCase));if(header<0){doc.Records.Insert(0,new(){Raw="[Script Info]"});header=0;}
        int end=header+1;while(end<doc.Records.Count&&!doc.Records[end].Raw.TrimStart().StartsWith('['))end++;
        var matching=doc.Records.Skip(header+1).Take(end-header-1).Where(r=>r.Raw.TrimStart().StartsWith(key+":",StringComparison.OrdinalIgnoreCase)).ToArray();if(matching.Length==0)doc.Records.Insert(header+1,new(){Raw=key+": "+value});else foreach(var r in matching)r.Raw=key+": "+value;
    }
    public static string[] TextParts(string raw)=>Regex.Split(raw,@"(\{[^}]*\})");
    public static string ReplacePart(string raw,int part,string text)
    {
        var parts=TextParts(raw);Need(part>=0&&part<parts.Length&&!parts[part].StartsWith('{'),"请选择正文片段。");Need(text.IndexOfAny(['{','}'])<0,"辅助翻译只编辑正文，ASS 标签请在主编辑器修改。");parts[part]=text.Replace("\r\n",@"\N").Replace("\n",@"\N");return string.Concat(parts);
    }
}
