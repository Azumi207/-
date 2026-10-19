using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Frameflow.Core;
public sealed record KaraokeUnit(int Duration,string Text);
public sealed record KaraokeGroup(int SourceCount,int TargetCount);
public sealed record TargetElement(string Prefix,string Raw)
{
    public string Display=>Raw.Replace(@"\N","↵").Replace(@"\n","↵").Replace(@"\h","␣");
}
public static class KanjiTiming
{
    static readonly Regex K=new(@"\\(?:kf|ko|[kK])(\d+)",RegexOptions.CultureInvariant);
    static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void ValidateTags(string raw)
    {
        foreach(Match block in Regex.Matches(raw,@"\{[^}]*\}"))foreach(Match tag in Regex.Matches(block.Value,@"\\(?:kf|ko|kt|[kK])[^\\}]*"))
            Need(Regex.IsMatch(tag.Value,@"^\\(?:kf|ko|[kK])\d+\s*$"),"存在不支持或无效的卡拉 OK 计时标签，未修改字幕。");
    }
    public static KaraokeUnit[] Parse(AssRow row)
    {
        Need(!Regex.IsMatch(row.Text,@"\\(?:kt|p[1-9]|t\()"),"暂不转移绝对卡拉 OK、动画或绘图行的计时。");ValidateTags(row.Text);
        Need(Regex.Replace(row.Text,@"\{[^}]*\}","").IndexOfAny(['{','}'])<0,"源行 ASS 标签不完整。");
        var units=new List<KaraokeUnit>();int? duration=null;string text="";
        foreach(Match token in Regex.Matches(row.Text,@"\{[^}]*\}|[^{}]+"))
        {
            if(token.Value.StartsWith('{'))foreach(Match timing in K.Matches(token.Value)){if(duration!=null)units.Add(new(duration.Value,text));Need(int.TryParse(timing.Groups[1].Value,out var value)&&value>=0&&value<=8_640_000,"卡拉 OK 时长无效。");duration=value;text="";}
            else {Need(duration!=null||string.IsNullOrWhiteSpace(token.Value),"源行首个卡拉 OK 标签之前有未计时正文。");if(duration!=null)text+=token.Value;}
        }
        if(duration!=null)units.Add(new(duration.Value,text));Need(units.Count>0,"源行没有可转移的 \\k/\\K/\\kf/\\ko 计时。");
        Need(units.Sum(u=>(long)u.Duration)<=Math.Round((row.End-row.Start)*100)+1,"源音节时长超过源行总时长。");return units.ToArray();
    }
    public static (TargetElement[] Elements,string Trailing) Target(string raw,bool replaceExisting)
    {
        Need(!Regex.IsMatch(raw,@"\\(?:kt|p[1-9]|t\(|move\(|fad\(|fade\()"),"带绝对计时、绘图或动画的目标行暂不转移，以免改变特效时间。");ValidateTags(raw);
        Need(Regex.Replace(raw,@"\{[^}]*\}","").IndexOfAny(['{','}'])<0,"目标行 ASS 标签不完整。");
        var elements=new List<TargetElement>();string prefix="";
        foreach(Match token in Regex.Matches(raw,@"\{[^}]*\}|[^{}]+"))
        {
            string value=token.Value;
            if(value.StartsWith('{')){if(K.IsMatch(value)){Need(replaceExisting,"目标已有卡拉 OK 标签；请明确勾选覆盖后再开始。");value=K.Replace(value,"");}if(value!="{}")prefix+=value;continue;}
            for(int i=0;i<value.Length;)
            {
                string element=value[i]=='\\'&&i+1<value.Length&&"Nnh".Contains(value[i+1])?value.Substring(i,2):StringInfo.GetNextTextElement(value,i);
                elements.Add(new(prefix,element));prefix="";i+=element.Length;
            }
        }
        Need(elements.Count>0,"目标行没有可配对的正文。");return (elements.ToArray(),prefix);
    }
    public static string Transfer(AssRow source,AssRow target,IReadOnlyList<KaraokeGroup> groups,bool overwrite,string tag="k")
    {
        Need(tag is "k" or "kf" or "ko","请选择 k / kf / ko 标签。");var syllables=Parse(source);var (letters,trailing)=Target(target.Text,overwrite);
        Need(groups.Count>0&&groups.All(g=>g.SourceCount>0&&g.TargetCount>=0)&&groups.Sum(g=>(long)g.SourceCount)==syllables.Length&&groups.Sum(g=>(long)g.TargetCount)==letters.Length,"配对尚未完整覆盖源音节和目标正文。");
        var text=new StringBuilder();int s=0,t=0;
        foreach(var group in groups)
        {
            long cs=syllables.Skip(s).Take(group.SourceCount).Sum(u=>(long)u.Duration);s+=group.SourceCount;
            if(group.TargetCount>0){text.Append(letters[t].Prefix);Need(cs>0,"有文字的目标分组不能分配零时长。");}
            text.Append('{').Append('\\').Append(tag).Append(cs).Append('}');
            for(int i=0;i<group.TargetCount;i++){var element=letters[t++];if(i>0)text.Append(element.Prefix);text.Append(element.Raw);}
        }
        text.Append(trailing);return text.ToString();
    }
    public static void Apply(AssRow source,AssRow target,IReadOnlyList<KaraokeGroup> groups,bool overwrite,bool copyLineTimes,string tag="k")
    {
        Need(source!=target,"源行与目标行不能相同。");string text=Transfer(source,target,groups,overwrite,tag);
        if(!copyLineTimes)Need(Parse(source).Sum(u=>(long)u.Duration)<=Math.Round((target.End-target.Start)*100)+1,"目标行时长不足，请勾选同时复制源行起止时间。");
        Need(source.End>source.Start&&source.Start>=0,"源行起止时间无效。");
        if(copyLineTimes){target.Start=source.Start;target.End=source.End;}target.Text=text;target.Reviewed=false;
    }
}
