using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace RimTube.Core
{
    // Called on attachment or an explicit update, never from a draw callback.
    public sealed class ResolverToolStore
    {
        public const long MaximumExecutableBytes=64L*1024*1024;
        private readonly string root,selection;
        public ResolverToolStore(string profile)
        {
            root=Path.GetFullPath(Path.Combine(profile,"RimTube","Tools","yt-dlp"));
            selection=Path.Combine(root,"active.xml");
        }
        public string ActivePath
        {
            get
            {
                try
                {
                    if(!File.Exists(selection))return "";
                    var xml=Load(selection);string version=(string)xml.Attribute("version"),hash=(string)xml.Attribute("sha256");
                    if(version=="bundled")return "";
                    Validate(version,hash);
                    string path=Executable(version,hash);
                    if(!File.Exists(path)||new FileInfo(path).Length>MaximumExecutableBytes)return "";
                    return Digest(path)==hash?path:"";
                }
                catch{return "";}
            }
        }
        public void Install(string version,Stream executable,string expectedSha256,string notices,Func<string,string> inspect,CancellationToken token)
        {
            Validate(version,expectedSha256);
            if(String.IsNullOrWhiteSpace(notices)||notices.Length>4*1024*1024)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateNoticesInvalid"));
            token.ThrowIfCancellationRequested();Directory.CreateDirectory(root);
            using(var gate=new FileStream(Path.Combine(root,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                string pending=Path.Combine(root,"pending-"+Guid.NewGuid().ToString("N")+".exe");
                try
                {
                    using(var output=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {
                        byte[] buffer=new byte[65536];long total=0;int read;
                        while((read=executable.Read(buffer,0,buffer.Length))>0)
                        {
                            token.ThrowIfCancellationRequested();total+=read;
                            if(total>MaximumExecutableBytes)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateTooLarge"));
                            output.Write(buffer,0,read);
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if(Digest(pending)!=expectedSha256)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateChecksumInvalid"));
                    if(!String.Equals(inspect(pending)?.Trim(),version,StringComparison.Ordinal))throw new InvalidDataException(LocalizedText.T("RimTube.UpdateVersionInvalid"));
                    string path=Executable(version,expectedSha256),folder=Path.GetDirectoryName(path);
                    Directory.CreateDirectory(folder);
                    if(File.Exists(path))
                    {
                        if(Digest(path)!=expectedSha256)
                        {
                            token.ThrowIfCancellationRequested();
                            File.Replace(pending,path,path+".damaged-"+Guid.NewGuid().ToString("N"));
                        }
                    }
                    else File.Move(pending,path);
                    // This release is immutable; only the small active pointer is replaced.
                    File.WriteAllText(Path.Combine(folder,"THIRD_PARTY_NOTICES.txt"),notices);
                    token.ThrowIfCancellationRequested();
                    Commit(new XElement("ResolverSelection",new XAttribute("version",version),new XAttribute("sha256",expectedSha256)));
                }
                finally{if(File.Exists(pending))File.Delete(pending);}
            }
        }
        public void Revert()
        {
            Directory.CreateDirectory(root);
            using(var gate=new FileStream(Path.Combine(root,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                Commit(new XElement("ResolverSelection",new XAttribute("version","bundled")));
        }
        private string Executable(string version,string hash)=>Path.Combine(root,version+"-"+hash.Substring(0,12),"yt-dlp.exe");
        private void Commit(XElement xml)
        {
            string pending=selection+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                xml.Save(pending);
                if(File.Exists(selection))File.Replace(pending,selection,null);
                else File.Move(pending,selection);
            }
            finally{if(File.Exists(pending))File.Delete(pending);}
        }
        private static XElement Load(string path)
        {
            using(var reader=System.Xml.XmlReader.Create(path,new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4096}))
                return XElement.Load(reader);
        }
        private static void Validate(string version,string hash)
        {
            if(version==null||!Regex.IsMatch(version,@"^\d{4}\.\d{2}\.\d{2}$")||hash==null||!Regex.IsMatch(hash,@"^[0-9a-f]{64}$"))
                throw new InvalidDataException(LocalizedText.T("RimTube.UpdateReleaseInvalid"));
        }
        internal static string Digest(string path)
        {using(var sha=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
    }
}
