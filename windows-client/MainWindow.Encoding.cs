using System.Windows;
using Frameflow.Controls;
namespace Frameflow;
public partial class MainWindow
{
    void BurnInClick(object sender,RoutedEventArgs e)=>Guard(()=>{if(!ApplyEditor())return;Pause();var dialog=new BurnInDialog(document.Serialize(),dummyVideo?null:mediaPath,selected?.Start??0){Owner=this};dialog.ShowDialog();});
}
