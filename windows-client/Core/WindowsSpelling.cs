using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Frameflow.Core;
internal sealed record SpellingIssue(int Start,int Length,string[] Suggestions);
internal sealed record SpellingSuggestion(string Replacement)
{
    public override string ToString()=>Replacement.Length==0?"（删除重复词）":Replacement;
}
internal static class WindowsSpelling
{
    // Public Windows spellcheck.h ABI. No registration, dictionary writes, network,
    // keyboard-language changes, or dependency on WPF's current input language.
    [ComImport,Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Factory
    {
        [return:MarshalAs(UnmanagedType.Interface)] IEnumString Languages();
        int Supports([MarshalAs(UnmanagedType.LPWStr)] string language);
        [return:MarshalAs(UnmanagedType.Interface)] Checker Create([MarshalAs(UnmanagedType.LPWStr)] string language);
    }
    [ComImport,Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Checker
    {
        [return:MarshalAs(UnmanagedType.LPWStr)] string Language();
        [return:MarshalAs(UnmanagedType.Interface)] Errors Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        [return:MarshalAs(UnmanagedType.Interface)] IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }
    [ComImport,Guid("803E3BD4-2828-4410-8290-418D1D73C762"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Errors { [return:MarshalAs(UnmanagedType.Interface)] Error? Next(); }
    [ComImport,Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Error
    {
        uint Start();uint Length();int Correction();
        [return:MarshalAs(UnmanagedType.LPWStr)] string Replacement();
    }
    static void Release(object? obj){if(obj!=null&&Marshal.IsComObject(obj))Marshal.ReleaseComObject(obj);}
    internal static SpellingIssue[] Check(string text)
    {
        if(text.Length>100_000)throw new InvalidOperationException("请将正文片段拆分到 10 万字符以内再检查。");
        Factory? factory=null;Checker? checker=null;Errors? errors=null;
        try
        {
            factory=(Factory)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"),true)!)!;
            if(factory.Supports("en-US")==0)throw new InvalidOperationException("本机未安装英语拼写词典。请在 Windows 语言设置中安装英语基本输入后重试；本次没有执行拼写检查。");
            checker=factory.Create("en-US");errors=checker.Check(text);var result=new List<SpellingIssue>();
            for(int i=0;i<1000;i++)
            {
                var error=errors.Next();if(error==null)break;
                try
                {
                    int start=checked((int)error.Start()),length=checked((int)error.Length()),action=error.Correction();if(action==0||start<0||length<=0||start+length>text.Length)continue;
                    var suggestions=new List<string>();
                    if(action==2)suggestions.Add(error.Replacement());else if(action==3)suggestions.Add("");else
                    {
                        IEnumString? words=null;try{words=checker.Suggest(text.Substring(start,length));var buffer=new string[1];for(int j=0;j<20&&words.Next(1,buffer,IntPtr.Zero)==0;j++)suggestions.Add(buffer[0]);}finally{Release(words);}
                    }
                    result.Add(new(start,length,suggestions.Distinct().ToArray()));
                }
                finally{Release(error);}
            }
            return result.ToArray();
        }
        catch(COMException ex){throw new InvalidOperationException("Windows 英语拼写服务暂不可用，未完成检查："+ex.Message,ex);}
        finally{Release(errors);Release(checker);Release(factory);}
    }
}
