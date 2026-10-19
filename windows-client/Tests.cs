using System.IO;
using System.Text;
using Frameflow.Core;
using Frameflow.Media;

namespace Frameflow;
public static class Tests
{
    public static string Run(string output)
    {
        var log=new List<string>();void Assert(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);log.Add("PASS: "+name);}
        var d=SubtitleDocument.New();d.Add(0.5,2.12,"第一句，含中文和逗号",null);d.Add(2.20,4,"第二句");d.Add(4.2,4.5,"短句");
        var rows=d.Rows.ToArray();rows[0].Actor="朝奈";rows[1].Actor="悠真";rows[0].Effect="custom-effect";
        var serialized=d.Serialize()+"\r\n[Custom Metadata]\r\nopaque: preserve me\r\n";
        var bytes=new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(serialized)).ToArray();var copy=SubtitleDocument.ParseBytes(bytes);
        Assert(copy.Bytes().SequenceEqual(bytes),"unmodified ASS byte-identical roundtrip including BOM and unknown section");
        Assert(copy.Rows.First().Text=="第一句，含中文和逗号","comma and Unicode text retained");
        copy.Rows.First().Actor="测试说话人";Assert(copy.Serialize().Contains("opaque: preserve me")&&copy.Rows.First().Effect=="custom-effect","edit preserves unknown section and effect");
        Assert(Timecode.Ass(59.999)=="0:01:00.00"&&Timecode.Parse("1:02:03.45")==3723.45,"timecode carry and parse");
        var issues=Quality.Scan(copy);Assert(issues.Count(i=>i.Kind=="疑似闪轴")==1&&issues.Any(i=>i.Kind=="显示过短"),"flash gap and short line detected");
        Quality.FixGap(issues.First(i=>i.Kind=="疑似闪轴"));Assert(Math.Abs(copy.Rows.First().End-2.2)<.001,"flash gap repair touches previous end only");
        var row=copy.Rows.First();row.Text=@"{\t(0,100,\blur2)\clip(0,0,100,100)}复杂文本";AssTags.Set(row,"pos","(960,900)");Assert(row.Text.Contains(@"\t(0,100,\blur2)")&&row.Text.Contains(@"\clip(0,0,100,100)")&&row.Text.Contains(@"\pos(960,900)"),"position edit retains advanced tags");
        row.Text=@"{\move(0,0,10,10)}动态";bool refused=false;try{AssTags.Set(row,"pos","(1,1)");}catch(InvalidOperationException){refused=true;}Assert(refused,"conflicting motion tag is not silently destroyed");
        row.Text="{\\fnMicrosoft YaHei}字体";AssTags.Set(row,"fn","Arial");Assert(row.Text.Contains(@"\fnArial")&&!row.Text.Contains("Microsoft"),"font tag replacement");
        Assert(AssTags.AssToHex(AssTags.HexToAss("#123456"))=="#123456","ASS BGR color conversion");
        var srt=SubtitleDocument.FromSrt("1\r\n00:00:01,250 --> 00:00:03,500\r\nHello\r\n你好\r\n\r\n");Assert(srt.Rows.Count()==1&&srt.Rows.First().Text==@"Hello\N你好"&&srt.ExportSrt().Contains("00:00:01,250"),"SRT multiline import and export");
        var malformed=SubtitleDocument.Parse("[Events]\nFormat: "+SubtitleDocument.EventFormat+"\nDialogue: broken\n");Assert(malformed.Warnings.Count==1&&malformed.Serialize().Contains("Dialogue: broken"),"malformed lines retained with warning");
        var reordered=SubtitleDocument.Parse("[Events]\nFormat: Start, End, Style, Name, Layer, MarginL, MarginR, MarginV, Effect, Extra, Text\nDialogue: 0:00:01.00,0:00:02.00,Default,Actor,0,0,0,0,,opaque,text,with,commas\n");reordered.Rows.First().End=3;Assert(reordered.Rows.First().Get("Extra")=="opaque"&&reordered.Rows.First().Text=="text,with,commas","custom event format retains extra fields and commas");
        var fixture=SubtitleDocument.ParseBytes(bytes);fixture.Rows.First().Note="请确认语气";fixture.Rows.First().Reviewed=true;
        var file=Path.Combine(output,"sample.ass");SubtitleDocument.AtomicSave(file,fixture.Bytes(),false);ReviewStore.Save(file,fixture,null);var reopened=SubtitleDocument.Load(file);ReviewStore.Load(file,reopened);Assert(reopened.Rows.First().Note=="请确认语气"&&reopened.Rows.First().Reviewed,"review sidecar roundtrip");
        File.AppendAllText(file,"; external edit");bool stale=false;try{ReviewStore.Load(file,reopened);}catch(InvalidOperationException){stale=true;}Assert(stale,"stale sidecar rejected after external edit");SubtitleDocument.AtomicSave(file,fixture.Bytes());Assert(Directory.GetFiles(output,"sample.ass.bak-*").Length>0,"save creates backup before replacement");ReviewStore.Save(file,fixture,null);
        var wav=Path.Combine(output,"sample.wav");WriteWave(wav);var audio=AudioPeaks.Decode(wav,CancellationToken.None);Assert(audio.Peaks.Length>=590&&audio.Peaks.Max()>.2&&Math.Abs(audio.Duration-6)<.02,"Media Foundation decodes real WAV to timestamped waveform peaks");
        var canceled=new CancellationToken(true);bool cancel=false;try{AudioPeaks.Decode(wav,canceled);}catch(OperationCanceledException){cancel=true;}Assert(cancel,"audio decode cancellation releases resources");
        var avi=Path.Combine(output,"sample.avi");VideoFixture.Write(avi);var av=AudioPeaks.Decode(avi,CancellationToken.None);Assert(av.Peaks.Length>=590&&av.Peaks.Max()>.1&&Math.Abs(av.Duration-6)<.1,"Media Foundation extracts PCM track from generated AVI video");
        var extracted=Path.Combine(output,"audio-cache-"+Guid.NewGuid().ToString("N")+".wav");AudioPeaks.Decode(avi,CancellationToken.None,null,extracted);
        var decodedCache=AudioPeaks.Decode(extracted,CancellationToken.None);Assert(Math.Abs(decodedCache.Duration-av.Duration)<.001&&decodedCache.Peaks.Length==av.Peaks.Length&&decodedCache.Peaks.Zip(av.Peaks).All(p=>Math.Abs(p.First-p.Second)<.001),"audio-only WAV preserves AVI audio timestamps and peaks");File.Delete(extracted);
        var gapFile=Path.Combine(output,"gap-cache-"+Guid.NewGuid().ToString("N")+".wav");using(var writer=new PcmWaveWriter(gapFile,100,1)){writer.Write(.5,new byte[]{1,2,3,4});writer.Complete();}
        var gapBytes=File.ReadAllBytes(gapFile);Assert(gapBytes.Length==148&&gapBytes.Skip(44).Take(100).All(b=>b==0)&&gapBytes[144]==1,"PCM cache preserves leading timestamp gap as silence");File.Delete(gapFile);
        var canceledFile=Path.Combine(output,"canceled-cache-"+Guid.NewGuid().ToString("N")+".wav");try{AudioPeaks.Decode(avi,new CancellationToken(true),null,canceledFile);}catch(OperationCanceledException){}
        Assert(!File.Exists(canceledFile),"canceled extraction removes incomplete audio cache");
        var incomplete=Path.Combine(output,"incomplete-"+Guid.NewGuid().ToString("N")+".wav");using(var writer=new PcmWaveWriter(incomplete,16000,1)){writer.Write(0,new byte[]{1,2});}Assert(!File.Exists(incomplete),"failed or incomplete PCM writer removes its owned file");
        var legacy=SubtitleDocument.Parse("[V4 Styles]\n[Events]\nFormat: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: Marked=0,0:00:01.00,0:00:03.00,Default,Actor,0,0,0,,legacy\n");Assert(legacy.Rows.First().Layer=="0","SSA Marked field displays numeric layer");legacy.Rows.First().Layer="1";Assert(legacy.Serialize().Contains("Marked=1"),"SSA Marked prefix preserved on edit");
        var comment=SubtitleDocument.ParseBytes(bytes);comment.Rows.First().Comment=true;Assert(comment.Serialize().Contains("Comment:"),"comment toggle serialized even with unchanged text");
        MenuCoreTests.Run(output,Assert);
        SubtitleCoreTests.Run(output,Assert);
        TimingCoreTests.Run(output,Assert);
        MultimediaCoreTests.Run(output,Assert);
        EncodingTests.Run(output,Assert);
        GpuTests.Run(output,Assert);
        log.Add($"AUDIO: {audio.SampleRate} Hz, {audio.Channels} channel, {audio.Peaks.Length} peaks, {audio.Duration:0.000} seconds");log.Add($"TOTAL: {log.Count(x=>x.StartsWith("PASS:"))} passed");return string.Join(Environment.NewLine,log);
    }
    public static void WriteWave(string file)
    {
        const int rate=16000,count=rate*6;using var w=new BinaryWriter(File.Create(file));w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(count*2);
        for(int i=0;i<count;i++){double t=i/(double)rate;double envelope=t>.5&&t<2.12||t>2.2&&t<4||t>4.2&&t<4.5?.45:0;w.Write((short)(Math.Sin(2*Math.PI*440*t)*envelope*short.MaxValue));}
    }
}
