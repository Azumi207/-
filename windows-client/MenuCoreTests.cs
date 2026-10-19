using Frameflow.Core;
namespace Frameflow;
public static class MenuCoreTests
{
    public static void Run(string dir,Action<bool,string> check)
    {
        var source=SubtitleDocument.New();source.AddStyle("Speaker");var a=source.Add(1,2,@"{\pos(100,200)}甲,乙\N第二行");a.Actor="朝奈";a.Style="Speaker";a.Effect="effect";a.Comment=true;
        a.Note="需要复核";a.Reviewed=true;var packet=RowTransfer.Decode(RowTransfer.Encode(RowTransfer.Capture(source,source.Rows)));
        check(packet.Rows.Length==1&&RowTransfer.Row(packet.Rows[0]).Text==a.Text&&packet.Rows[0].Comment,"row clipboard JSON preserves Unicode commas override tags and comments");
        var text=RowTransfer.AssText(packet);var external=RowTransfer.FromText(text,0,2);check(RowTransfer.Row(external.Rows[0]).Actor=="朝奈"&&RowTransfer.Row(external.Rows[0]).Start==1,"ASS event clipboard text roundtrips timestamps and speaker");
        var target=SubtitleDocument.New();target.Add(4,5,"existing");var inserted=RowTransfer.Insert(target,packet,0);var reopened=SubtitleDocument.Parse(target.Serialize());
        check(reopened.Rows.Count()==2&&reopened.Rows.First().Text==a.Text&&reopened.Rows.Last().Text=="existing","paste inserts before target without replacing existing row");
        check(inserted[0].Note=="需要复核"&&!inserted[0].Reviewed,"native row clipboard retains note but pasted row needs fresh review");
        check(reopened.Styles.Any(s=>s.Name=="Speaker")&&reopened.Rows.First().Style=="Speaker","cross-document paste imports a missing named style definition");
        var currentStyle=target.Styles.First(s=>s.Name=="Speaker");currentStyle.Set("Fontsize","88");RowTransfer.Insert(target,packet,2);check(target.Styles.Count(s=>s.Name=="Speaker")==1&&currentStyle.Get("Fontsize")=="88","same-name style keeps destination definition instead of overwriting shared style");
        var custom=SubtitleDocument.Parse("[Events]\nFormat: Start, End, Style, Actor, Layer, Extra, Text\nDialogue: 0:00:01.00,0:00:02.00,Default,person,0,opaque,custom,comma\n");
        var customPacket=RowTransfer.Capture(custom,custom.Rows);RowTransfer.Insert(target,customPacket,1);reopened=SubtitleDocument.Parse(target.Serialize());check(reopened.Warnings.Count==0&&reopened.Rows.ElementAt(1).Get("Extra")=="opaque"&&reopened.Rows.ElementAt(1).Actor=="person"&&reopened.Rows.Last().Text==a.Text,"format boundaries preserve unknown clipboard fields and following original events");
        var blank=SubtitleDocument.New();RowTransfer.Insert(blank,customPacket,0);check(SubtitleDocument.Parse(blank.Serialize()).Rows.Single().Get("Extra")=="opaque","paste into empty document retains custom event format");
        var plain=RowTransfer.FromText("第一行\r\n第二行",3,2);check(plain.Rows.Length==2&&RowTransfer.Row(plain.Rows[1]).Start==5,"plain-text clipboard becomes sequential subtitle rows");
        bool rejected=false;try{RowTransfer.FromText("Dialogue: broken\nordinary",0,2);}catch(FormatException){rejected=true;}check(rejected,"malformed mixed ASS clipboard is rejected without partial parsing");
        rejected=false;try{RowTransfer.Decode("{\"Version\":2,\"Rows\":[],\"Styles\":[]}");}catch(FormatException){rejected=true;}check(rejected,"unsupported clipboard version rejected");
        var dest=SubtitleDocument.New();dest.Add(8,10,"one");dest.Add(12,14,"two");dest.Rows.First().Note="keep note";
        RowTransfer.PasteFields(dest,packet,dest.Rows.ToArray(),new HashSet<string>{"Text","Actor"});check(dest.Rows.All(r=>r.Text==a.Text&&r.Actor=="朝奈")&&dest.Rows.First().Start==8&&dest.Rows.Last().End==14&&dest.Rows.First().Note=="keep note","selective one-to-many copies chosen fields while preserving timing and notes");
        string before=dest.Serialize();var badSource=SubtitleDocument.New();badSource.Add(.1,.2,"bad end");rejected=false;try{RowTransfer.PasteFields(dest,RowTransfer.Capture(badSource,badSource.Rows),dest.Rows.ToArray(),new HashSet<string>{"End"});}catch(FormatException){rejected=true;}check(rejected&&dest.Serialize()==before,"invalid selective timing rejects the whole operation atomically");
        rejected=false;try{RowTransfer.PasteFields(dest,plain,[dest.Rows.First()],new HashSet<string>{"Text"});}catch(FormatException){rejected=true;}check(rejected&&dest.Serialize()==before,"selective paste count mismatch leaves target unchanged");
        RowTransfer.PasteFields(dest,plain,dest.Rows.ToArray(),new HashSet<string>{"Text"});check(dest.Rows.First().Text=="第一行"&&dest.Rows.Last().Text=="第二行","selective multiple rows map in subtitle order");
        var unusual=SubtitleDocument.Parse("[V4+ Styles]\nFormat: Name, Fontsize, Fontname\nStyle: Other,72,Arial\n[Events]\nFormat: "+SubtitleDocument.EventFormat+"\nDialogue: 0,0:00:01.00,0:00:02.00,Other,,0,0,0,,x\n");
        RowTransfer.Insert(dest,RowTransfer.Capture(unusual,unusual.Rows),0);dest.AddStyle("AfterPaste");var styleCopy=SubtitleDocument.Parse(dest.Serialize());
        check(styleCopy.Styles.Single(s=>s.Name=="Other").Get("Fontsize")=="72"&&styleCopy.Styles.Single(s=>s.Name=="AfterPaste").Get("Fontname")=="Microsoft YaHei","new style after custom-format clipboard import retains correct field layout");
        var recent=Path.Combine(dir,"core-recent.json");RecentFiles.Clear(recent);for(int i=0;i<25;i++)RecentFiles.Add(recent,Path.Combine(dir,$"file-{i}.ass"),"subtitle");
        check(RecentFiles.Load(recent).Count==20&&RecentFiles.Load(recent)[0].Path.EndsWith("file-24.ass"),"recent files persist with 20-item newest-first cap");
        RecentFiles.Add(recent,Path.Combine(dir,"FILE-20.ass"),"subtitle");check(RecentFiles.Load(recent).Count==20&&RecentFiles.Load(recent)[0].Path.EndsWith("FILE-20.ass"),"recent paths deduplicate case-insensitively on Windows");
        File.WriteAllText(recent,"bad json");check(RecentFiles.Load(recent).Count==0,"corrupted recent list recovers as empty");RecentFiles.Clear(recent);check(File.Exists(recent)&&RecentFiles.Load(recent).Count==0,"clear recent records writes empty list without deleting source files");
    }
}
