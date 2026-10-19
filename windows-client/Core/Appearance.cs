using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Frameflow.Core;

// Stored separately from subtitle data, shortcuts and the existing display settings.
internal static class Appearance
{
    internal static string Mode { get; private set; } = "system";
    internal static bool IsDark { get; private set; }
    internal static int Revision { get; private set; }
    internal static event Action? Changed;
    internal static string SettingsPath { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Frameflow", "appearance-v1.json");
    static bool subscribed;
    static readonly Dictionary<string,(string Light,string Dark)> Palette = new()
    {
        ["AppBackground"]=("#EEF1F6","#12151B"), ["PanelBackground"]=("#FFFFFF","#1C2029"),
        ["SecondaryBackground"]=("#F6F8FC","#222834"), ["FieldBackground"]=("#F4F6FA","#151A23"),
        ["ForegroundBrush"]=("#222939","#EDF0F7"), ["MutedBrush"]=("#637087","#A1ACC0"),
        ["BorderBrush"]=("#E1E6EF","#303849"), ["Accent"]=("#4E5DD6","#A9B3FF"),
        ["OnAccent"]=("#FFFFFF","#191E40"), ["SelectionBrush"]=("#ECEFFF","#2B324C"),
        ["SuccessBrush"]=("#267865","#90CBB6"), ["SuccessBackground"]=("#EAF5F0","#223B34"),
        ["WarningBrush"]=("#996117","#ECC17E"), ["WarningBackground"]=("#FFF5E5","#3B3024"),
        ["ErrorBrush"]=("#B53042","#F6A2AE"), ["WaveBrush"]=("#7888A5","#8B9BBC"),
        ["WaveStartBrush"]=("#B94C67","#EF9CB0"), ["WaveEndBrush"]=("#367FA7","#8BC7E9"),
        ["WaveGapBrush"]=("#50DD9B37","#609E763C"), ["MediaBackground"]=("#141D28","#10151D")
    };
    internal static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    internal static void Initialize(string? path=null, bool load=true)
    {
        if(path!=null)SettingsPath=path;
        string mode="system";
        if(load)try
        {
            if(File.Exists(SettingsPath)&&new FileInfo(SettingsPath).Length<8192)
                mode=JsonSerializer.Deserialize<Preference>(File.ReadAllText(SettingsPath))?.Mode??"system";
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { }
        Apply(mode is "light" or "dark" or "system"?mode:"system",false);
        if(!subscribed){SystemEvents.UserPreferenceChanged+=SystemThemeChanged;EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler(WindowLoaded));subscribed=true;}
    }
    internal static bool SystemIsDark()
    {
        try{return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value&&value==0;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){return false;}
    }
    internal static void Apply(string mode,bool persist=true)
    {
        if(mode is not ("light" or "dark" or "system"))throw new ArgumentException("主题必须为浅色、深色或跟随系统。");
        bool dark=mode=="dark"||mode=="system"&&SystemIsDark();
        if(persist){Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);SubtitleDocument.AtomicSave(SettingsPath,JsonSerializer.SerializeToUtf8Bytes(new Preference(mode)),false);}
        Mode=mode;IsDark=dark;
        foreach(var (key,pair) in Palette)
        {
            var color=(Color)ColorConverter.ConvertFromString(dark?pair.Dark:pair.Light);
            if(Application.Current.Resources[key] is SolidColorBrush current&&!current.IsFrozen)current.Color=color;
            else Application.Current.Resources[key]=new SolidColorBrush(color);
        }
        Revision++;Changed?.Invoke();
        foreach(Window window in Application.Current.Windows)ApplyTitleBar(window);
    }
    static void SystemThemeChanged(object? sender,UserPreferenceChangedEventArgs e)
    {
        var dispatcher=Application.Current?.Dispatcher;
        if(Mode=="system"&&dispatcher!=null&&!dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(new Action(()=>{if(Mode=="system")Apply("system",false);}));
    }
    static void WindowLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is Window window&&ReferenceEquals(e.OriginalSource,window))
        {
            if(window.Style==null)window.SetResourceReference(FrameworkElement.StyleProperty,typeof(Window));
            ApplyTitleBar(window);
        }
    }
    static void ApplyTitleBar(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
        int value=IsDark?1:0;
        // Unsupported Windows builds simply retain their normal native title bar.
        _=DwmSetWindowAttribute(handle,20,ref value,sizeof(int));
    }
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    internal static void Shutdown(){if(subscribed){SystemEvents.UserPreferenceChanged-=SystemThemeChanged;subscribed=false;}}
    sealed record Preference(string Mode);
}
