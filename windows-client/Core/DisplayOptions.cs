using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Frameflow.Core;
internal sealed record DisplayOptions(string Layout="full",int Tags=0,bool Toolbar=true,bool Crosshair=true,bool Follow=true,bool Linked=true,double Volume=.7,double WaveGain=1,int AutosaveSeconds=30,double RowHeight=32,string MenuLanguage="zh")
{
    internal void Validate(){if(!new[]{"full","subs","video","audio"}.Contains(Layout)||Tags is <0 or >2||!double.IsFinite(Volume)||Volume<0||Volume>1||!double.IsFinite(WaveGain)||WaveGain<.1||WaveGain>10||AutosaveSeconds<15||AutosaveSeconds>600||!double.IsFinite(RowHeight)||RowHeight<22||RowHeight>48||MenuLanguage is not "zh" and not "en")throw new FormatException("无效显示/播放设置。");}
    internal static DisplayOptions Load(string path){try{if(!File.Exists(path)||new FileInfo(path).Length>100000)return new();var v=JsonSerializer.Deserialize<DisplayOptions>(File.ReadAllText(path))??new();v.Validate();return v;}catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or FormatException){return new();}}
    internal void Save(string path){Validate();Directory.CreateDirectory(Path.GetDirectoryName(path)!);SubtitleDocument.AtomicSave(path,JsonSerializer.SerializeToUtf8Bytes(this),false);}
}
internal sealed class TagDisplayConverter(int mode):IValueConverter
{
    public object Convert(object value,Type type,object parameter,CultureInfo culture){string s=value?.ToString()??"";return mode==0?s:Regex.Replace(s,@"\{[^}]*\}",mode==1?"{…}":"");}
    public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
