using Frameflow.Core;
using Frameflow.Controls;
using System.Windows;

namespace Frameflow;
internal static class SubtitleCoreTests
{
    internal static void Run(string dir,Action<bool,string> check)
    {
        SubtitleDocument Fixture(){var d=SubtitleDocument.New();d.Add(1,2,"甲");d.Add(2,3,"乙");d.Add(4,5,"丙");return d;}
        void Reject(Action action,string label){bool rejected=false;try{action();}catch(Exception ex) when(ex is InvalidOperationException or ArgumentException or FormatException){rejected=true;}check(rejected,label);}
        var d=Fixture();var original=d.Serialize();var copy=SubtitleTools.Copy(d);copy.Rows.First().Text="副本";check(d.Serialize()==original,"subtitle tool staging never modifies original document");
        foreach(string op in new[]{"insert-before","insert-after","insert-before-video","insert-after-video"})
        {
            d=Fixture();var added=SubtitleTools.Execute(d,[d.Rows.ElementAt(1)],op,1.25).Single();check(d.Rows.Count()==4&&added.Number==(op.Contains("after")?3:2)&&added.Start==(op.EndsWith("video")?1.25:0)&&added.End-added.Start==5,"subtitle "+op+" inserts at requested slot with five second duration");
        }
        d=Fixture();d.Rows.First().Actor="说话人";d.Rows.First().Note="批注";var duplicate=SubtitleTools.Execute(d,[d.Rows.First(),d.Rows.Last()],"duplicate");check(duplicate.Length==2&&duplicate[0].Actor=="说话人"&&duplicate[0].Note=="批注"&&duplicate[1].Text=="丙"&&!duplicate[0].Reviewed,"duplicate preserves selected row fields and notes in order");
        foreach(string op in new[]{"split-before","split-after"}){d=Fixture();var selected=SubtitleTools.Execute(d,[d.Rows.First()],op,1.4).Single();check(d.Rows.Count()==4&&d.Rows.First().End==1.4&&d.Rows.ElementAt(1).Start==1.4&&d.Rows.ElementAt(1).End==2&&selected.Number==(op=="split-before"?1:2),op+" duplicates complete text and partitions time");}
        d=Fixture();Reject(()=>SubtitleTools.Execute(d,[d.Rows.First()],"split-after",2),"split at endpoint is rejected");
        d=Fixture();SubtitleTools.Execute(d,[d.Rows.First(),d.Rows.Last()],"join-concat");check(d.Rows.Count()==2&&d.Rows.First().Text=="甲 丙"&&d.Rows.First().Start==1&&d.Rows.First().End==5,"join concatenate covers time range and separates full text by spaces");
        d=Fixture();SubtitleTools.Execute(d,d.Rows.ToArray(),"join-first");check(d.Rows.Single().Text=="甲"&&d.Rows.Single().End==5,"join keep-first retains first-row text and covers selection");
        d=Fixture();var karaoke=SubtitleTools.Execute(d,d.Rows.ToArray(),"join-karaoke").Single();check(karaoke.Text==@"{\k100}甲{\k100}乙{\k100}{\k100}丙","karaoke join uses centiseconds and represents silence explicitly");
        var syllables=SubtitleTools.Execute(d,[karaoke],"split-karaoke");check(syllables.Length==3&&syllables[2].Start==4&&syllables[2].End==5&&syllables[2].Text=="丙","karaoke split reconstructs timed syllables and skips silent gap");
        d=Fixture();d.Rows.First().Text=@"{\i1\kf50}甲{\ko50}乙";var parts=SubtitleTools.KaraokeParts(d.Rows.First());check(parts.Length==2&&parts.All(r=>r.Text.Contains(@"\i1"))&&parts[1].Start==1.5,"karaoke split carries formatting to each independent syllable");
        d.Rows.First().Text=@"{\k9999}甲";Reject(()=>SubtitleTools.KaraokeParts(d.Rows.First()),"karaoke overflow rejects instead of extending beyond source time");
        d.Rows.First().Text=@"{\kt20\k20}甲";Reject(()=>SubtitleTools.KaraokeParts(d.Rows.First()),"absolute karaoke timing is explicitly rejected");
        d=Fixture();d.Rows.ElementAt(1).Start=1.5;Reject(()=>SubtitleTools.Execute(d,d.Rows.ToArray(),"join-karaoke"),"overlapping karaoke join rejects");
        d=Fixture();d.Rows.First().Text="a";d.Rows.ElementAt(1).Text=@"a\Nb";d.Rows.Last().Text="b";d.Rows.Last().Start=3;
        var recombined=SubtitleTools.Execute(d,d.Rows.ToArray(),"recombine");check(recombined.Length==2&&recombined[0].Text=="a"&&recombined[0].Start==1&&recombined[0].End==3&&recombined[1].Text=="b"&&recombined[1].Start==2&&recombined[1].End==5,"recombine reduces a / a-newline-b / b to continuous independent fragments");
        d=Fixture();d.Rows.First().Text=@"{\i1}a";Reject(()=>SubtitleTools.Execute(d,d.Rows.ToArray(),"recombine"),"recombine refuses complex tagged input");
        foreach(string field in new[]{"start","end","style","actor","effect","layer"})foreach(string scope in new[]{"all","selected"})
        {
            d=Fixture();var rows=d.Rows.ToArray();rows[0].Start=4;rows[0].End=6;rows[2].Start=0;rows[2].End=1;
            rows[0].Style="Z";rows[2].Style="A";rows[0].Actor="Z";rows[2].Actor="A";rows[0].Effect="Z";rows[2].Effect="A";rows[0].Layer="3";rows[2].Layer="1";
            var targets=scope=="all"?rows:[rows[0],rows[2]];SubtitleTools.Execute(d,targets,"sort-"+scope+"-"+field);
            var result=d.Rows.ToArray();check(result[0]!=rows[0]&&(scope=="all"||result[1]==rows[1]),"sort "+scope+" by "+field+" orders requested slots only");
        }
        d=Fixture();SubtitleTools.Execute(d,[d.Rows.First(),d.Rows.Last()],"swap");check(string.Join("",d.Rows.Select(r=>r.Text))=="丙乙甲","swap exchanges exactly two noncontiguous row slots");
        Reject(()=>SubtitleTools.Execute(d,d.Rows.ToArray(),"swap"),"swap rejects more than two rows");SubtitleTools.Execute(d,d.Rows.ToArray(),"delete");check(!d.Rows.Any(),"delete can remove all selected rows");SubtitleTools.Execute(d,[],"insert-before");check(d.Rows.Count()==1,"insert works in empty document");
        d=Fixture();d.Rows.ElementAt(1).Comment=true;d.Rows.Last().Actor="Actor";
        check(SubtitleTools.Select(d,"actor","说话人","完全相同",false,true,true,false).Length==1,"conditional selection matches actor case-insensitively");
        check(SubtitleTools.Select(d,"[甲乙]","字幕正文","正则表达式",true,true,false,false).Length==1,"conditional selection regex respects dialogue/comment scope");
        check(SubtitleTools.Select(d,"乙","字幕正文","包含",true,true,true,true).Length==2,"conditional selection supports inverse match");
        Reject(()=>SubtitleTools.Select(d,"[","字幕正文","正则表达式",true,true,true,false),"invalid regex fails without selecting arbitrary rows");
        d=Fixture();d.AddStyle("Accent");d.Rows.First().Text=@"{\rDefault}甲{\rAccent}乙";SubtitleTools.RenameStyle(d,d.Styles.First(),"正文");check(d.Rows.All(r=>r.Style=="正文")&&d.Rows.First().Text==@"{\r正文}甲{\rAccent}乙","style rename updates row references and matching inline resets only");
        Reject(()=>SubtitleTools.DeleteStyle(d,d.Styles.First()),"referenced shared style cannot be deleted");Reject(()=>SubtitleTools.DeleteStyle(d,d.Styles.Last()),"inline-reset style reference prevents deletion");d.Rows.First().Text="甲";SubtitleTools.DeleteStyle(d,d.Styles.Last());check(d.Styles.Count()==1,"unreferenced style deletion works");
        var formatted=@"{\i1}Hello{\i0}\Nworld";var replaced=SubtitleTools.ReplacePart(formatted,1+1,"你好");check(replaced==@"{\i1}你好{\i0}\Nworld","translation block replacement preserves every surrounding override tag");
        Reject(()=>SubtitleTools.ReplacePart(formatted,2,"{坏标签}"),"translation assistant rejects override injection");
        d=Fixture();d.Rows.First().Text=@"{\pos(960,900)\fs48\bord2\shad1}甲";SubtitleTools.Resample(d,1280,720);check(d.PlayResX==1280&&d.PlayResY==720&&d.Styles.First().Get("Fontsize")=="32"&&d.Rows.First().Text.Contains(@"\pos(640,600)\fs32"),"uniform resampling scales script, styles and static position tags");
        Reject(()=>SubtitleTools.Resample(d,1000,1000),"resampling rejects aspect-ratio changes");d.Rows.First().Text=@"{\move(0,0,100,100)}甲";Reject(()=>SubtitleTools.Resample(d,1920,1080),"resampling refuses unsupported animation");
        foreach(string tag in new[]{@"\fs+10",@"\unknown42",@"\clip(0,0,100,100)",@"\p1"}){d=Fixture();d.Rows.First().Text="{"+tag+"}甲";Reject(()=>SubtitleTools.Resample(d,1280,720),"resampling refuses unhandled tag "+tag);}
        d=Fixture();d.Rows.First().Note="keep note";d.Rows.First().Reviewed=true;var staged=SubtitleTools.Copy(d);check(staged.Rows.First().Note=="keep note"&&staged.Rows.First().Reviewed,"staging preserves independent review metadata");
        var tied=Fixture();SubtitleTools.Execute(tied,tied.Rows.ToArray(),"sort-all-style");check(string.Join("",tied.Rows.Select(r=>r.Text))=="甲乙丙","equal sort keys retain original row order");
        var custom=SubtitleDocument.Parse("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Custom, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Default,A,0,0,0,,X,one\nFormat: "+SubtitleDocument.EventFormat+"\nDialogue: 0,0:00:00.00,0:00:01.00,Default,B,0,0,0,,two\n");
        SubtitleTools.Execute(custom,custom.Rows.ToArray(),"swap");var parsed=SubtitleDocument.Parse(custom.Serialize());check(parsed.Warnings.Count==0&&parsed.Rows.Last().Get("Custom")=="X"&&parsed.Rows.First().Actor=="B","row reorder preserves per-row custom formats across serialization");
        var polygon=BasicDrawing.Create([new Point(100,100),new Point(300,100),new Point(200,200)]);check(polygon.StartsWith(@"{\an7\pos(100,100)\bord0\shad0\p1}m 0 0 l")&&BasicDrawing.Geometry(polygon)?.Bounds.Width==200,"basic polygon writes parseable ASS p1 drawing and preview geometry");
        Reject(()=>BasicDrawing.Create([new Point(0,0),new Point(1,1),new Point(2,2)]),"degenerate polygon is rejected");
        File.WriteAllText(Path.Combine(dir,"subtitle-tools-roundtrip.ass"),custom.Serialize());check(SubtitleDocument.Load(Path.Combine(dir,"subtitle-tools-roundtrip.ass")).Rows.Last().Get("Custom")=="X","new subtitle operation output reopens from real ASS file");
    }
}
