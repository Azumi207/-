using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Frameflow.Controls;
using Frameflow.Core;
using Frameflow.Media;

namespace Frameflow;
public partial class MainWindow
{
    async Task RunEncodingTestsAsync(Action<bool,string> check,string dir)
    {
        Pause();var saved=Capture();string? savedPath=subtitlePath;bool savedDirty=dirty;
        var fileMenu=((Menu)VideoMenu.Parent).Items.OfType<MenuItem>().First();check(fileMenu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"压制字幕到视频…"))&&VideoMenu.Items.OfType<MenuItem>().Any(i=>Equals(i.Header,"压制字幕到视频…")),"burn-in is reachable from both File and Video menus");
        fileMenu.IsSubmenuOpen=true;await Task.Delay(70);var popup=(Popup)fileMenu.Template.FindName("PART_Popup",fileMenu);CaptureElement((FrameworkElement)popup.Child,Path.Combine(dir,"encoding-file-menu.png"));fileMenu.IsSubmenuOpen=false;
        document=SubtitleDocument.Parse(EncodingTests.StyledAss());subtitlePath=EncodingTests.FixtureAss(dir);RefreshAll();TextEditor.Text="红色样式 RED · 未保存编辑";string sourceHash=EncodingTests.Hash(subtitlePath);string video=EncodingTests.FixtureVideo(dir),outDir=EncodingTests.FixtureFolder(dir);string output=Path.Combine(outDir,"ui-burned-"+Guid.NewGuid().ToString("N")+".mp4");
        Exception? failure=null;_=Dispatcher.BeginInvoke(new Action(async()=>
        {
            BurnInDialog? dialog=null;
            try
            {
                await Task.Delay(80);dialog=Application.Current.Windows.OfType<BurnInDialog>().Single();check(dialog.InputVideo.Text==mediaPath&&dialog.Quality.SelectedIndex==0&&dialog.KeepAudio.IsChecked==true,"burn-in dialog defaults to current video balanced quality and original audio");
                check(document.Rows.First().Text.Contains("未保存编辑")&&EncodingTests.Hash(subtitlePath)==sourceHash,"opening burn-in commits editor draft to memory without saving or changing source ASS");
                check(dialog.Acceleration.SelectedIndex==0&&dialog.Acceleration.Items.Count==2&&dialog.EncoderLabel.Text.Contains("无需"),"GPU UI defaults to automatic with only one optional CPU-only choice");
                dialog.InputVideo.Text=video;dialog.OutputVideo.Text=output;dialog.PreviewTime.Text="5";dialog.UpdateLayout();CaptureElement(dialog,Path.Combine(dir,"encoding-dialog.png"));
                dialog.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(dialog.Busy&&!dialog.InputVideo.IsEnabled&&!dialog.StartButton.IsEnabled&&Equals(dialog.CancelButton.Content,"取消压制"),"encode start disables configuration and enables cancellation while process is running");await dialog.Running!;
                check(dialog.LastError==null&&dialog.LastResult?.Output==output&&File.Exists(output),"actual Start button completes FFmpeg burn-in and commits output");check(dialog.Progress.Value==100&&dialog.OpenButton.IsEnabled&&dialog.FolderButton.IsEnabled&&!dialog.Busy,"completed encoding exposes playback folder and 100-percent state");await Task.Delay(60);CaptureElement(dialog,Path.Combine(dir,"encoding-complete.png"));
                check(dialog.LastResult!=null&&dialog.EncoderLabel.Text.Contains(dialog.LastResult.Encoder.Label)&&dialog.EncoderLabel.Text.Contains("实际编码器"),"GPU completion UI reports the actual encoder including any fallback");
                string outputHash=EncodingTests.Hash(output);dialog.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.Running!;check(dialog.LastError?.Contains("已存在")==true&&EncodingTests.Hash(output)==outputHash,"repeated Start refuses existing output without overwriting it");
                dialog.OutputVideo.Text=Path.Combine(outDir,"ui-preview-"+Guid.NewGuid().ToString("N")+".mp4");dialog.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.Running!;check(dialog.LastError==null&&dialog.LastResult!=null&&Math.Abs(dialog.LastResult.Video.Duration-7)<.1,"actual Preview button generates short clip from requested absolute subtitle time");
                dialog.Acceleration.SelectedIndex=1;dialog.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.Running!;check(dialog.LastResult?.Encoder==HardwareEncoding.Cpu&&dialog.EncoderLabel.Text.Contains("仅 CPU")&&dialog.Acceleration.IsEnabled,"native CPU-only choice generates preview and remains visible after completion");dialog.Acceleration.SelectedIndex=0;
                dialog.OutputVideo.Text=Path.Combine(outDir,"ui-cancel-"+Guid.NewGuid().ToString("N")+".mp4");dialog.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.Running!;check(!File.Exists(dialog.OutputVideo.Text)&&!dialog.Busy&&dialog.StatusLabel.Text.Contains("取消"),"Cancel button leaves no final output and restores controls");
                dialog.InputVideo.Text=video+".missing";dialog.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.Running!;check(dialog.LastError!=null&&dialog.Message.Text.Length>0&&!dialog.Busy,"invalid input is shown inline without modal error or stuck controls");
            }
            catch(Exception ex){failure=ex;}
            finally{if(dialog?.IsVisible==true)dialog.Close();}
        }));
        fileMenu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"压制字幕到视频…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));if(failure!=null)throw new Exception("Encoding window test failure",failure);
        check(EncodingTests.Hash(EncodingTests.FixtureAss(dir))==sourceHash&&document.Rows.First().Text.Contains("未保存编辑"),"closing encoder retains unsaved document and original disk subtitle");
        var closeDialog=new BurnInDialog(document.Serialize(),video,0){Owner=this,ShowActivated=false};closeDialog.OutputVideo.Text=Path.Combine(outDir,"ui-close-"+Guid.NewGuid().ToString("N")+".mp4");closeDialog.Show();var running=closeDialog.StartAsync(false);closeDialog.Close();await running;check(!closeDialog.IsVisible&&!closeDialog.Busy&&!File.Exists(closeDialog.OutputVideo.Text),"closing busy encoder cancels waits for cleanup then closes without partial output");
        var missing=new BurnInDialog(document.Serialize().Replace("Arial","Fixture Missing Font 9876"),video,0);missing.ShowActivated=false;missing.Owner=this;missing.Show();missing.UpdateLayout();check(MenuVisuals<TextBlock>(missing).Any(t=>t.Text.Contains("本机名称未匹配")&&t.Text.Contains("Fixture Missing Font")),"missing font names trigger explicit preview and font-directory warning");missing.Close();
        subtitlePath=savedPath;Restore(saved);dirty=savedDirty;undo.Clear();redo.Clear();UpdateTitle();check(document.Rows.Count()==3,"encoding UI tests restore original document and prior menus");
    }
}
