using System.IO;
using System.Windows;

namespace Frameflow;
public partial class App:Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool test=e.Args.Any(a=>a.EndsWith("-test")||a.EndsWith("-smoke"));
        var appearanceOutput=e.Args.SkipWhile(a=>a!="--appearance-test").Skip(1).FirstOrDefault();
        Frameflow.Core.Appearance.Initialize(appearanceOutput!=null?Path.Combine(appearanceOutput,"appearance.json"):null,!test);
        Exit+=(_,_)=>Frameflow.Core.Appearance.Shutdown();
        if(e.Args.Length==3&&e.Args[0]=="--automation-worker")
        {
            try{Frameflow.Core.AutomationProcess.Worker(e.Args[1],e.Args[2]);Shutdown(0);}catch(Exception ex){Console.Error.WriteLine(ex.Message);Shutdown(1);}return;
        }
        if(e.Args.Contains("--self-test"))
        {
            try{var dir=e.Args.SkipWhile(s=>s!="--self-test").Skip(1).FirstOrDefault()??Path.Combine(AppContext.BaseDirectory,"test-output");Directory.CreateDirectory(dir);var report=Tests.Run(dir);File.WriteAllText(Path.Combine(dir,"core-tests.txt"),report);Console.WriteLine(report);Shutdown(0);}
            catch(Exception ex){Console.Error.WriteLine(ex);Shutdown(1);}return;
        }
        var window=new MainWindow();MainWindow=window;
        if(e.Args.Contains("--preview-test"))
        {
            string dir=Path.GetFullPath(e.Args.SkipWhile(a=>a!="--preview-test").Skip(1).First());Directory.CreateDirectory(dir);Frameflow.Core.Appearance.Initialize(Path.Combine(dir,"appearance.json"),false);window.SmokeMode=true;window.Show();
            try{await window.RunPreviewTestsAsync(dir);Shutdown(0);}catch(Exception ex){File.WriteAllText(Path.Combine(dir,"preview-error.txt"),ex.ToString());Console.Error.WriteLine(ex);Shutdown(1);}return;
        }
        if(e.Args.Contains("--appearance-test"))
        {
            string dir=appearanceOutput!;Directory.CreateDirectory(dir);window.SmokeMode=true;window.Show();
            try{await window.RunAppearanceTestsAsync(dir);Shutdown(0);}catch(Exception ex){File.WriteAllText(Path.Combine(dir,"appearance-error.txt"),ex.ToString());Console.Error.WriteLine(ex);Shutdown(1);}return;
        }
        if(e.Args.Contains("--team-smoke"))
        {
            var dir=e.Args.SkipWhile(s=>s!="--team-smoke").Skip(1).First();window.SmokeMode=true;window.Show();
            try{await window.RunTeamOnlyAsync(dir);Shutdown(0);}catch(Exception ex){Console.Error.WriteLine(ex);Shutdown(1);}return;
        }
        if(e.Args.Contains("--ui-smoke"))
        {
            // Compositor timing assertions require a visible, active test surface, not an occluded background window.
            var dir=e.Args.SkipWhile(s=>s!="--ui-smoke").Skip(1).FirstOrDefault()??Path.Combine(AppContext.BaseDirectory,"test-output");Directory.CreateDirectory(dir);window.SmokeMode=true;window.ShowActivated=true;window.Topmost=true;window.Show();window.Activate();
            try{await window.RunSmokeAsync(dir);Shutdown(0);}catch(Exception ex){File.WriteAllText(Path.Combine(dir,"ui-error.txt"),ex.ToString());Console.Error.WriteLine(ex);Shutdown(1);}return;
        }
        window.Show();if(e.Args.Length>0&&File.Exists(e.Args[0])){try{window.OpenSubtitle(e.Args[0]);}catch(Exception ex){MessageBox.Show(ex.Message,"鎵撳紑瀛楀箷澶辫触");}}
    }
}
