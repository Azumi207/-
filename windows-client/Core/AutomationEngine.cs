using System.Globalization;
using MoonSharp.Interpreter;

namespace Frameflow.Core;
internal sealed record MacroInfo(string Name,string Description);
internal sealed record AutomationRequest(string Source,string? Macro,string Ass,int[] Selected,int Active);
internal sealed record AutomationResult(MacroInfo[] Macros,string? Ass,int[] Selected,string Log);
internal static class AutomationEngine
{
    internal const string Example="""
script_name = "帧语基础清理"
script_description = "只修改所选行正文，保留时间与样式。"
script_version = "1.0"
function trim_lines(subs, selected, active)
  for _, i in ipairs(selected) do
    local line = subs[i]
    if line.class == "dialogue" then
      line.text = line.text:gsub("^%s+", ""):gsub("%s+$", "")
      subs[i] = line
    end
  end
  aegisub.set_undo_point("去除首尾空白")
  return selected
end
aegisub.register_macro("去除所选行首尾空白", "保留时间、样式与 ASS 标签", trim_lines)
function comment_lines(subs, selected)
  for _, i in ipairs(selected) do local line = subs[i]; line.comment = not line.comment; subs[i] = line end
  return selected
end
aegisub.register_macro("切换所选行注释", "批量切换对白与注释，可统一撤销", comment_lines)
""";
    static void Need(bool value,string message){if(!value)throw new ScriptRuntimeException(message);}
    internal static AutomationResult Execute(AutomationRequest request)
    {
        Need(System.Text.Encoding.UTF8.GetByteCount(request.Source)<=262144&&System.Text.Encoding.UTF8.GetByteCount(request.Ass)<=4_000_000,"脚本最多 256 KB，当前自动化字幕快照最多 4 MB。");var doc=SubtitleDocument.Parse(request.Ass);Need(doc.Records.Count<=30000&&doc.Rows.Count()<=10000,"自动化最多处理 10000 句 / 30000 条记录。");
        var script=new Script(CoreModules.Basic|CoreModules.GlobalConsts|CoreModules.TableIterators|CoreModules.String|CoreModules.Table|CoreModules.Math|CoreModules.Bit32);
        var log=new System.Text.StringBuilder();void Log(string text){if(log.Length<8000)log.AppendLine(text[..Math.Min(text.Length,1000)]);}
        script.Options.DebugPrint=Log;var macros=new List<(MacroInfo Info,DynValue Run,DynValue Validate)>();var api=new Table(script);
        api.Set("register_macro",DynValue.NewCallback((_,a)=>{Need(a.Count>=3&&a[0].Type==DataType.String&&a[2].Type==DataType.Function,"register_macro 需要名称、描述和 Lua 函数。");string name=a[0].String;Need(name.Length is >0 and <=120&&macros.Count<64&&!macros.Any(m=>m.Info.Name==name),"宏名称无效、重复或超过 64 项。");macros.Add((new(name,a[1].CastToString()??""),a[2],a.Count>3?a[3]:DynValue.Nil));return DynValue.Nil;}));
        api.Set("set_undo_point",DynValue.NewCallback((_,a)=>{Log("撤销点："+(a.Count>0?a[0].CastToString():"宏更改")+"（主程序统一一次提交）");return DynValue.Nil;}));
        api.Set("cancel",DynValue.NewCallback((_,_)=>throw new ScriptRuntimeException("脚本主动取消；未提交更改。")));
        DynValue Unsupported(string feature)=>DynValue.NewCallback((_,_)=>throw new ScriptRuntimeException("本版本尚未实现 "+feature+"；未提交更改。"));
        foreach(string name in new[]{"register_filter","decode_path","text_extents","frame_from_ms","ms_from_frame","video_size"})api.Set(name,Unsupported("aegisub."+name));
        var dialog=new Table(script);dialog.Set("display",Unsupported("交互式 Lua dialog / Automation 4 完整接口"));api.Set("dialog",DynValue.NewTable(dialog));var progress=new Table(script);foreach(string name in new[]{"set","title","task"})progress.Set(name,DynValue.NewCallback((_,a)=>{if(a.Count>0)Log(a[0].ToPrintString());return DynValue.Nil;}));progress.Set("is_cancelled",DynValue.NewCallback((_,_)=>DynValue.False));api.Set("progress",DynValue.NewTable(progress));script.Globals.Set("aegisub",DynValue.NewTable(api));
        DynValue Call(DynValue function,params DynValue[] args){var co=script.CreateCoroutine(function).Coroutine;co.AutoYieldCounter=10000;var result=co.Resume(args);int budget=0;while(co.State!=CoroutineState.Dead){Need(++budget<400,"Lua 指令预算耗尽，未提交更改。");result=co.Resume();}return result;}
        Call(script.LoadString(request.Source));if(request.Macro==null)return new(macros.Select(m=>m.Info).ToArray(),null,[],log.ToString());var macro=macros.SingleOrDefault(m=>m.Info.Name==request.Macro);Need(macro.Info!=null,"找不到所选宏，请重新加载脚本。");
        string section="";var sections=new string[doc.Records.Count];for(int i=0;i<sections.Length;i++){if(doc.Records[i].Raw.TrimStart().StartsWith('['))section=doc.Records[i].Raw.Trim();sections[i]=section;}
        Table ReadLine(int index)
        {
            Need(index>0&&index<=doc.Records.Count,"字幕索引越界。");var record=doc.Records[index-1];var t=new Table(script);t.Set("raw",DynValue.NewString(record.Serialize()));t.Set("section",DynValue.NewString(sections[index-1]));
            if(record.Row is AssRow r){t.Set("class",DynValue.NewString("dialogue"));foreach(var (key,val) in new[]{("text",r.Text),("actor",r.Actor),("style",r.Style),("effect",r.Effect)})t.Set(key,DynValue.NewString(val));t.Set("start_time",DynValue.NewNumber(r.Start*1000));t.Set("end_time",DynValue.NewNumber(r.End*1000));t.Set("comment",DynValue.NewBoolean(r.Comment));foreach(var (key,field) in new[]{("layer","Layer"),("margin_l","MarginL"),("margin_r","MarginR"),("margin_t","MarginV"),("margin_b","MarginV")})t.Set(key,DynValue.NewNumber(double.TryParse(r.Get(field),out var n)?n:0));}
            else if(record.Style is AssStyle style){t.Set("class",DynValue.NewString("style"));t.Set("name",DynValue.NewString(style.Name));t.Set("fontname",DynValue.NewString(style.Get("Fontname")));t.Set("fontsize",DynValue.NewNumber(style.Number("Fontsize",48)));}
            else if(sections[index-1]=="[Script Info]"&&record.Raw.Contains(':')&&!record.Raw.TrimStart().StartsWith(';')){t.Set("class",DynValue.NewString("info"));var parts=record.Raw.Split(':',2);t.Set("key",DynValue.NewString(parts[0].Trim()));t.Set("value",DynValue.NewString(parts[1].Trim()));}
            else t.Set("class",DynValue.NewString("unknown"));return t;
        }
        bool validating=false;
        void WriteLine(int index,Table t)
        {
            Need(!validating,"验证函数不可修改字幕。");Need(index>0&&index<=doc.Records.Count&&doc.Records[index-1].Row!=null,"本版仅支持修改已有对白/注释行，不支持增删行或改写样式/元数据。");Need(t.Get("class").String=="dialogue","不能改变记录类型。");var row=doc.Records[index-1].Row!;
            string Str(string key){var v=t.Get(key);Need(v.Type==DataType.String,"字段 "+key+" 必须为字符串。");Need(v.String.Length<=100000,"单行字段过长。");return v.String;}
            double Num(string key){var v=t.Get(key);Need(v.Type==DataType.Number&&double.IsFinite(v.Number),"字段 "+key+" 必须为有限数字。");return v.Number;}
            double start=Num("start_time")/1000,end=Num("end_time")/1000;Need(start>=0&&end>start&&end<=86400,"脚本生成无效起止时间。");string text=Str("text"),actor=Str("actor"),style=Str("style"),effect=Str("effect");Need(text.IndexOfAny(['\r','\n'])<0,"正文换行请使用 ASS \\N。");Need(new[]{actor,style,effect}.All(s=>s.IndexOfAny([',','\r','\n'])<0),"脚本字段含非法分隔符。");Need(t.Get("comment").Type==DataType.Boolean,"comment 必须为布尔值。");
            var nums=new[]{"layer","margin_l","margin_r","margin_t","margin_b"}.Select(Num).ToArray();Need(nums.All(n=>n>=0&&n<=100000&&n==Math.Truncate(n)),"层与边距须为非负整数。");Need(nums[3]==nums[4],"ASS 单一垂直边距要求 margin_t 与 margin_b 相同。");if(row.Start!=start)row.Start=start;if(row.End!=end)row.End=end;Need(row.End>row.Start,"取整至 ASS 精度后时长为零。");row.Text=text;row.Actor=actor;row.Style=style;row.Effect=effect;row.Comment=t.Get("comment").Boolean;if(double.Parse(row.Layer,CultureInfo.InvariantCulture)!=nums[0])row.Layer=nums[0].ToString(CultureInfo.InvariantCulture);string[] fields=["MarginL","MarginR","MarginV"];for(int n=0;n<3;n++)if(!double.TryParse(row.Get(fields[n]),out double old)||old!=nums[n+1])row.Set(fields[n],nums[n+1].ToString(CultureInfo.InvariantCulture));
        }
        var subs=new Table(script);subs.Set("n",DynValue.NewNumber(doc.Records.Count));foreach(string op in new[]{"append","insert","delete","deleterange"})subs.Set(op,Unsupported("subtitles."+op+"（结构编辑）"));var meta=new Table(script);meta.Set("__len",DynValue.NewCallback((_,_)=>DynValue.NewNumber(doc.Records.Count)));meta.Set("__index",DynValue.NewCallback((_,a)=>{Need(a[1].Type==DataType.Number&&a[1].Number==Math.Truncate(a[1].Number),"无效字幕索引。");return DynValue.NewTable(ReadLine((int)a[1].Number));}));meta.Set("__newindex",DynValue.NewCallback((_,a)=>{Need(a[1].Type==DataType.Number&&a[1].Number==Math.Truncate(a[1].Number)&&a[2].Type==DataType.Table,"仅支持 subtitles[i] = dialogue_table。");WriteLine((int)a[1].Number,a[2].Table);return DynValue.Nil;}));subs.MetaTable=meta;
        var selection=new Table(script);foreach(int i in request.Selected){Need(i>0&&i<=doc.Records.Count&&doc.Records[i-1].Row!=null,"选择记录不是字幕行。");selection.Append(DynValue.NewNumber(i));}var arguments=new[]{DynValue.NewTable(subs),DynValue.NewTable(selection),DynValue.NewNumber(request.Active)};
        if(macro.Validate.Type==DataType.Function){validating=true;var allowed=Call(macro.Validate,arguments);validating=false;Need(allowed.CastToBool(),"宏验证函数认为当前选择不可运行。");}
        var result=Call(macro.Run,arguments);Need(!subs.Pairs.Any(p=>p.Key.Type==DataType.Number),"不支持通过 table/raw 操作改写字幕结构。");if(result.Type==DataType.Tuple)result=result.Tuple[0];var selected=request.Selected;if(result.Type==DataType.Table){selected=result.Table.Values.Select(v=>{Need(v.Type==DataType.Number&&v.Number==Math.Truncate(v.Number),"返回选择必须是行索引数组。");return(int)v.Number;}).Distinct().ToArray();Need(selected.All(i=>i>0&&i<=doc.Records.Count&&doc.Records[i-1].Row!=null),"宏返回的选择越界。");}
        Need(System.Text.Encoding.UTF8.GetByteCount(doc.Serialize())<=4_000_000,"输出字幕超过 4 MB 限制。");return new(macros.Select(m=>m.Info).ToArray(),doc.Serialize(),selected,log.ToString());
    }
}
