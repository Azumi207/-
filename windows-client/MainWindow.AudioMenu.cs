using System.Windows;
using System.Windows.Controls;
using Frameflow.Core;
using Frameflow.Media;
using Microsoft.Win32;

namespace Frameflow;
public partial class MainWindow
{
    string? externalAudioPath;bool audioClosed,playbackMuted;double playbackVolume=.7;CancellationTokenSource? audioLoadCancel,spectrumCancel;readonly List<string> generatedAudio=[];
    async void AudioOperationClick(object sender,RoutedEventArgs e)
    {
        try{await RunAudioOperationAsync((string)((MenuItem)sender).Tag);}catch(OperationCanceledException){Status.Text="音频操作已取消。";}catch(Exception ex){Status.Text="音频操作失败："+ex.Message;}
    }
    async Task RunAudioOperationAsync(string op)
    {
        if(op=="open"){var p=new OpenFileDialog{Filter="音频或含音轨的媒体|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.mp4;*.avi;*.mkv|所有文件|*.*"};if(p.ShowDialog(this)==true)await LoadIndependentAudioAsync(p.FileName,true);}
        else if(op=="from-video"){if(mediaPath==null||!VideoAvailable||dummyVideo)throw new InvalidOperationException("需要实际视频文件。");await LoadIndependentAudioAsync(mediaPath,false);}
        else if(op=="close")CloseAudioTrack();
        else if(op=="waveform"||op=="spectrum"){Wave.SpectrumMode=op=="spectrum";WaveformItem.IsChecked=!Wave.SpectrumMode;SpectrumItem.IsChecked=Wave.SpectrumMode;AudioDisplayLabel.Text=Wave.SpectrumMode?"音频频谱（低频在下）":"音频波形";Wave.InvalidateVisual();ScheduleSpectrum();}
        else if(op is "silence" or "noise"){string? input=Prompt(op=="noise"?"生成白噪声（低电平）":"生成静音音频","时长（秒，0.1～3600），不会自动播放");if(input==null)return;await GenerateAudioAsync(Timecode.Parse(input),op=="noise");}
        else if(op=="clip"){if(selected==null||!ApplyEditor())return;double start=selected.Start,end=selected.End;var p=new SaveFileDialog{Filter="PCM WAV|*.wav",FileName=$"字幕-{selected.Number}.wav"};if(p.ShowDialog(this)==true)await ExportAudioClipAsync(p.FileName,start,end);}
        else if(op=="play")PlaySelectionClick(this,new RoutedEventArgs());else if(op=="stop")Pause();else if(op=="mute"){playbackMuted=!playbackMuted;ApplyAudioVolume();AudioMuteItem.IsChecked=playbackMuted;}
    }
    async Task LoadIndependentAudioAsync(string path,bool independent)
    {
        path=Path.GetFullPath(path);if(!File.Exists(path))throw new FileNotFoundException("找不到音频文件。",path);Pause();audioLoadCancel?.Cancel();var cancel=new CancellationTokenSource();audioLoadCancel=cancel;var token=cancel.Token;
        string cache=Path.Combine(Path.GetTempPath(),"Frameflow-audio-"+Guid.NewGuid().ToString("N")+".wav");double previous=PlaybackPosition;Status.Text="正在准备独立音轨，旧音轨在成功前保留…";
        try{var result=await Task.Run(()=>AudioPeaks.Decode(path,token,null,cache),token);token.ThrowIfCancellationRequested();if(result.Duration<=0)throw new InvalidOperationException("音轨为空。");decodeCancel?.Cancel();CloseAudio();externalAudioPath=independent?path:null;audioClosed=false;audioCachePath=cache;audioCaches.Add(cache);Wave.Peaks=result.Peaks;Wave.Spectrum=null;Wave.Duration=result.Duration;WaveScroll.Maximum=result.Duration;duration=independent?result.Duration:VideoDuration;SeekBar.Maximum=duration;pendingAudioPosition=Math.Clamp(previous,0,Math.Max(0,result.Duration-.01));AudioPlayer.IsMuted=SmokeMode||playbackMuted;AudioPlayer.Open(new Uri(cache));audioPreparing=true;
            AudioOnly.IsEnabled=!independent;if(independent)AudioOnly.IsChecked=true;WaveStatus.Text=$" · {Path.GetFileName(path)} · {result.SampleRate} Hz / {result.Channels} 声道";Wave.InvalidateVisual();ApplyAudioVolume();RememberMedia(path,"audio");Status.Text=independent?"独立音频已加载，视频保留并暂停；从视频打开音频可恢复关联音轨。":"视频音轨已重新加载。";ScheduleSpectrum();
        }catch{TryDeleteAudio(cache);throw;}
    }
    async Task GenerateAudioAsync(double seconds,bool noise)
    {
        string path=Path.Combine(Path.GetTempPath(),"Frameflow-generated-"+Guid.NewGuid().ToString("N")+".wav");try{await Task.Run(()=>PcmTools.Generate(path,seconds,noise));await LoadIndependentAudioAsync(path,true);generatedAudio.Add(path);}catch{TryDeleteAudio(path);throw;}
    }
    void CloseAudioTrack()
    {
        Pause();audioLoadCancel?.Cancel();decodeCancel?.Cancel();spectrumCancel?.Cancel();CloseAudio();externalAudioPath=null;audioClosed=true;AudioOnly.IsEnabled=true;AudioOnly.IsChecked=false;Wave.Peaks=[];Wave.Spectrum=null;Wave.InvalidateVisual();WaveStatus.Text=" · 音频已关闭（视频保留，静音播放）";duration=VideoAvailable?VideoDuration:60;SeekBar.Maximum=duration;ApplyAudioVolume();
    }
    async Task<string> EnsureAudioCacheAsync(CancellationToken token)
    {
        if(audioClosed)throw new InvalidOperationException("音频已关闭，请重新打开音频。");if(audioCachePath!=null&&File.Exists(audioCachePath))return audioCachePath;
        if(externalAudioPath==null&&mediaPath==null)throw new InvalidOperationException("请先打开音频或视频。");PrepareAudio();while(audioPreparing){token.ThrowIfCancellationRequested();await Task.Delay(60,token);}if(audioCachePath==null||!File.Exists(audioCachePath))throw new InvalidOperationException("PCM 音轨准备失败，请检查解码器或文件。");return audioCachePath;
    }
    async Task ExportAudioClipAsync(string output,double start,double end)
    {
        string full=Path.GetFullPath(output);if(new[]{mediaPath,externalAudioPath}.Where(p=>p!=null).Any(p=>Path.GetFullPath(p!).Equals(full,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("不能覆盖当前媒体源文件。");var cache=await EnsureAudioCacheAsync(CancellationToken.None);await Task.Run(()=>PcmTools.Clip(cache,full,start,end));Status.Text="选段已导出为 PCM WAV，原媒体未改动。";
    }
    async void ScheduleSpectrum()
    {
        spectrumCancel?.Cancel();if(!ready||!Wave.SpectrumMode)return;var cancel=new CancellationTokenSource();spectrumCancel=cancel;var token=cancel.Token;double start=Wave.ViewStart,span=Wave.Span;Wave.Spectrum=null;
        try{await Task.Delay(120,token);string cache=await EnsureAudioCacheAsync(token);var result=await Task.Run(()=>PcmTools.Spectrum(cache,start,span,token),token);if(token.IsCancellationRequested)return;Wave.Spectrum=result;Wave.InvalidateVisual();WaveStatus.Text=" · 频谱：当前视窗，线性 0～Nyquist，512 点 FFT";}
        catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested){WaveStatus.Text=" · 频谱不可用："+ex.Message;Wave.SpectrumMode=false;SpectrumItem.IsChecked=false;WaveformItem.IsChecked=true;Wave.InvalidateVisual();}}
    }
    void ApplyAudioVolume(){Player.Volume=AudioPlayer.Volume=playbackVolume;Player.IsMuted=SmokeMode||playbackMuted||audioClosed||externalAudioPath!=null;AudioPlayer.IsMuted=SmokeMode||playbackMuted;}
    void DisposeMediaMenuResources(){audioLoadCancel?.Cancel();spectrumCancel?.Cancel();dummyClock.Stop();if(detachedVideo!=null)detachedVideo.Close();foreach(var path in generatedAudio)TryDeleteAudio(path);generatedAudio.Clear();}
}
