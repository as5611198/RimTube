using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using RimTube.Core;

internal static class ToolUpdateTests
{
    public static void Network(Action<bool,string> check,string root)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var store=new ResolverToolStore(Path.Combine(root,"官方更新 profile"));
        string version=ResolverToolUpdater.UpdateAsync(store,deadline.Token).GetAwaiter().GetResult();
        string path=store.ActivePath;
        check(File.Exists(path),"official stable resolver passes checksum and installs in isolated profile");
        check(ProcessRunner.Run(path,new[]{"--version"},deadline.Token,15).Output.Trim()==version,"actual downloaded executable reports the exact release version");
        check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path),"THIRD_PARTY_NOTICES.txt")).Contains("THIRD_PARTY_LICENSES"),"actual Windows release third-party notices accompany update");
        store.Revert();
        check(store.ActivePath==""&&File.Exists(path),"real official update can revert without removing the downloaded tool");
    }
    public static void Run(Action<bool,string> check,string root)
    {
        var type=typeof(Library).Assembly.GetType("RimTube.Core.ResolverToolStore");
        check(type!=null,"resolver updates have a separate profile store");
        string profile=Path.Combine(root,"更新 profile"),mod=Path.Combine(root,"Workshop Mod");
        object store=Activator.CreateInstance(type,new object[]{profile});
        object Call(string method,params object[] args)
        {
            try{return type.GetMethod(method).Invoke(store,args);}
            catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        string Active()=>(string)type.GetProperty("ActivePath").GetValue(store);
        void Reject(Action action,string name)
        {try{action();check(false,name);}catch(InvalidDataException){check(true,name);}}
        byte[] exe=new byte[256];exe[0]=(byte)'M';exe[1]=(byte)'Z';exe[70]=1;
        string hash=Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant();
        string version="2026.11.01",notice="Test fixture license notice";
        Func<string,string> inspect=_=>version;
        check(Active()=="","clean profile keeps the bundled resolver");
        Reject(()=>Call("Install",version,new MemoryStream(exe),new string('0',64),notice,inspect,CancellationToken.None),"checksum mismatch never activates a download");
        check(Active()=="","failed checksum leaves original selection intact");
        Reject(()=>Call("Install","../bad",new MemoryStream(exe),hash,notice,inspect,CancellationToken.None),"version path traversal is rejected");
        Reject(()=>Call("Install",version,new MemoryStream(exe),hash,notice,(Func<string,string>)(_=>"2026.10.26"),CancellationToken.None),"wrong executable version is rejected");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try{Call("Install",version,new MemoryStream(exe),hash,notice,inspect,cancelled.Token);check(false,"cancelled update must abort");}
            catch(OperationCanceledException){check(Active()=="","cancellation leaves bundled selection unchanged");}
        }
        Reject(()=>Call("Install",version,new MemoryStream(exe),hash,"",inspect,CancellationToken.None),"update requires accompanying notices");
        Call("Install",version,new MemoryStream(exe),hash,notice,inspect,CancellationToken.None);
        string first=Active();
        check(File.Exists(first)&&File.ReadAllBytes(first).SequenceEqual(exe),"verified resolver is activated in the profile");
        check(first.StartsWith(Path.Combine(profile,"RimTube","Tools"))&&!first.StartsWith(mod),"update never writes to installed Workshop content");
        check(Directory.GetFiles(Path.GetDirectoryName(first),"*.txt").Any(f=>File.ReadAllText(f).Contains(notice)),"active tool keeps its license notices");
        object restarted=Activator.CreateInstance(type,new object[]{profile});
        check((string)type.GetProperty("ActivePath").GetValue(restarted)==first,"verified selection survives a restart");
        object other=Activator.CreateInstance(type,new object[]{Path.Combine(root,"other-profile")});
        check((string)type.GetProperty("ActivePath").GetValue(other)=="","other game profile does not inherit updates");
        var settings=new Settings();
        var configure=typeof(Settings).GetMethod("ConfigureResolverUpdate");
        check(configure!=null,"bundled defaults accept a verified profile resolver");
        settings.ConfigureBundledTools(mod);configure.Invoke(settings,new object[]{first});
        check(settings.YtDlp==first,"automatic tool selection uses profile update");
        string custom=Path.Combine(root,"my-resolver.exe");settings.YtDlp=custom;
        configure.Invoke(settings,new object[]{""});
        check(settings.YtDlp==custom,"reverting automatic update preserves explicit custom override");
        settings.YtDlp="";
        check(settings.YtDlp==Path.Combine(mod,"Tools","bin","yt-dlp.exe"),"clearing override returns to bundled fallback");
        Reject(()=>Call("Install",version,new MemoryStream(new byte[]{1,2,3}),hash,notice,inspect,CancellationToken.None),"truncated download never replaces active tool");
        check(Active()==first,"failure during later update preserves current tool");
        Call("Revert");
        check(Active()==""&&File.Exists(first),"revert selects bundle and retains old verified executable");
        Call("Install",version,new MemoryStream(exe),hash,notice,inspect,CancellationToken.None);
        check(Active()==first,"same verified release can be selected again");
        File.WriteAllBytes(first,new byte[]{1,2,3});
        check(Active()=="","corrupted installed update falls back to bundled resolver");
        check(File.Exists(first),"corrupt update is left for inspection rather than deleting player files");
        Call("Install",version,new MemoryStream(exe),hash,notice,inspect,CancellationToken.None);
        check(Active()==first&&File.ReadAllBytes(first).SequenceEqual(exe),"reinstall repairs an owned corrupt update after validation");
        check(Directory.GetFiles(Path.GetDirectoryName(first),"*.damaged-*").Any(),"repair retains the damaged owned executable for inspection");
        using(var saved=new Library(Path.Combine(root,"updated-library")))
        {
            saved.Options.ConfigureBundledTools(mod);configure.Invoke(saved.Options,new object[]{first});saved.Save();
            check((string)System.Xml.Linq.XDocument.Load(saved.StateFile).Root.Element("Settings").Attribute("ytDlp")=="","profile update default is never saved as a custom absolute override");
        }
        using(var lateCancel=new CancellationTokenSource())
        {
            try
            {
                Call("Install","2026.11.02",new MemoryStream(exe),hash,notice,(Func<string,string>)(_=>{lateCancel.Cancel();return "2026.11.02";}),lateCancel.Token);
                check(false,"late cancellation must abort activation");
            }
            catch(OperationCanceledException){check(Active()==first,"cancellation after verification keeps previous active selection");}
        }
        string pointer=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(first)),"active.xml");
        bool blocked=false;
        using(var held=new FileStream(pointer,FileMode.Open,FileAccess.Read,FileShare.None))
        {
            try{Call("Install","2026.11.03",new MemoryStream(exe),hash,notice,(Func<string,string>)(_=>"2026.11.03"),CancellationToken.None);}
            catch(IOException){blocked=true;}
        }
        check(blocked&&Active()==first,"failed atomic selection replacement keeps the previous tool");
        var updater=typeof(Library).Assembly.GetType("RimTube.Core.ResolverToolUpdater");
        check(updater!=null,"resolver updater validates official stable release metadata");
        object Parse(string json)
        {
            try{return updater.GetMethod("ParseRelease").Invoke(null,new object[]{json});}
            catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}
        }
        string release="{\"tag_name\":\"2026.11.01\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"yt-dlp.exe\",\"size\":256},{\"name\":\"SHA2-256SUMS\",\"size\":1000},{\"name\":\"yt-dlp_win.zip\",\"size\":1024}]}";
        var plan=Parse(release);
        check((string)plan.GetType().GetField("Version").GetValue(plan)==version,"stable tag is preserved for binary version validation");
        Reject(()=>Parse(release.Replace("\"prerelease\":false","\"prerelease\":true")),"prerelease metadata is rejected");
        Reject(()=>Parse(release.Replace("yt-dlp.exe","other.exe")),"missing exact Windows executable is rejected");
        Reject(()=>Parse(release.Replace("256","999999999")),"oversized executable metadata is rejected before download");
        try{updater.GetMethod("ExpectedHash").Invoke(null,new object[]{hash+"  yt-dlp.exe\n","yt-dlp.exe"});check(true,"official checksums match exact asset name");}
        catch{check(false,"official checksums match exact asset name");}
        try{updater.GetMethod("ExpectedHash").Invoke(null,new object[]{hash+"  other-yt-dlp.exe\n","yt-dlp.exe"});check(false,"partial checksum filenames must be rejected");}
        catch(TargetInvocationException e){check(e.InnerException is InvalidDataException,"partial checksum filenames must be rejected");}
    }
}
