using System;
using System.IO;

namespace RimTube.Core
{
    public static class PortableRuntime
    {
        public static string InterfacePath(string profileDirectory)
            => Path.Combine(Absolute(profileDirectory),"RimTube","Interface","ui.xml");
        public static string DataDirectory(string profileDirectory,string modDirectory,string configured)
        {
            string mod=Absolute(modDirectory);
            string data=String.IsNullOrWhiteSpace(configured)?Path.Combine(Absolute(profileDirectory),"RimTube","Library"):Absolute(configured);
            string boundary=mod.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(String.Equals(data.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),mod.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||data.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(LocalizedText.T("RimTube.DataOutsideMod"));
            return data;
        }
        private static string Absolute(string path)
        {
            if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)
                ||!String.Equals(Path.GetPathRoot(path),Path.GetPathRoot(Path.GetFullPath(path)),StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(LocalizedText.T("RimTube.AbsoluteDataPath"));
            if(path.Length>=2&&path[1]==':'&&(path.Length<3||(path[2]!='\\'&&path[2]!='/')))
                throw new ArgumentException(LocalizedText.T("RimTube.AbsoluteDataPath"));
            return Path.GetFullPath(path);
        }
    }
    public static class JavaScriptRuntime
    {
        public static bool IsNode(string path)=>String.Equals(Path.GetFileNameWithoutExtension(path),"node",StringComparison.OrdinalIgnoreCase);
        public static string DisplayName(string path)=>IsNode(path)?"Node.js":"Deno";
        public static string ResolverArgument(string path)=>(IsNode(path)?"node:":"deno:")+Path.GetFullPath(path);
    }
    // Defaults are resolved once on attachment, never by searching PATH or in OnGUI.
    internal sealed class ConfiguredToolPath
    {
        private string configured="",automatic="",resolved="";
        public string Value
        {
            get=>resolved;
            set{configured=String.IsNullOrWhiteSpace(value)||String.Equals(value,automatic,StringComparison.OrdinalIgnoreCase)?"":value;Update();}
        }
        public string Override=>configured;
        public void Default(string path){automatic=path;if(String.Equals(configured,automatic,StringComparison.OrdinalIgnoreCase))configured="";Update();}
        private void Update(){resolved=String.IsNullOrEmpty(configured)?automatic:configured;}
    }
}
