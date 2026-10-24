using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.Win32;

public static class InstallerEngine
{
    public static string PayloadPath;
    public class Entry { public int id; public string file, category, key, visual; }
    public class Block { public int offset; public string before, after; }
    public class Binary { public string name, before_sha256, after_sha256; public Block[] patches; }
    public class Asset { public string path, sha256; }
    public class Manifest { public string version; public Entry[] entries; public Binary[] binaries; public Asset[] assets; }
    public class Record { public string path, backup, before, after; public bool existed; }
    public class Journal { public string version, status; public Record[] records; }
    static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 20000000 }; }
    public static string Hash(byte[] data) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    static byte[] Hex(string s) { return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray(); }
    static string Target(string root, string relative)
    {
        if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Split('/','\\').Any(x=>x=="..")) throw new IOException("مسار غير آمن في الحزمة.");
        string full=Path.GetFullPath(Path.Combine(root,relative));
        if (!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("مسار خارج مجلد اللعبة.");
        for (string p=full; !String.IsNullOrEmpty(p); p=Path.GetDirectoryName(p))
            if ((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("اختر مجلدًا مباشرًا دون روابط رمزية: "+p);
        return full;
    }
    static string Root(string game)
    {
        string root=Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(root)||!Directory.Exists(Path.Combine(root,"config"))||!Directory.Exists(Path.Combine(root,"maps"))) throw new IOException("اختر مجلد Amnesia Rebirth الذي يحتوي على config وmaps وملف اللعبة.");
        foreach (string name in new[]{"AmnesiaRebirth","AmnesiaRebirth_NoSteam"})
            if (Process.GetProcessesByName(name).Length>0) throw new IOException("أغلق اللعبة أولًا ثم أعد المحاولة.");
        Target(root,".amnesia-arabic/state.json"); return root;
    }
    static void Write(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string tmp=path+".arabic-"+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(tmp,data); if(File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path); }
        finally { if(File.Exists(tmp)) File.Delete(tmp); }
    }
    static byte[] XmlBytes(XmlDocument doc) { using(var ms=new MemoryStream()) { using(var w=XmlWriter.Create(ms,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=false})) doc.Save(w); return ms.ToArray(); } }
    static byte[] Translate(string path, Entry[] entries)
    {
        var doc=new XmlDocument { PreserveWhitespace=true, XmlResolver=null }; doc.LoadXml(File.ReadAllText(path));
        if(entries[0].file=="config/Credits.cfg")
        {
            var nodes=doc.SelectSingleNode("/*/Content").ChildNodes.Cast<XmlNode>().Where(n=>n.NodeType==XmlNodeType.Element).ToArray();
            foreach(var e in entries) { int i=Int32.Parse(e.key); if(i>=nodes.Length||nodes[i].Name!=e.category) throw new IOException("نسخة ملف الشكر غير مدعومة."); nodes[i].InnerText=e.visual; }
        }
        else
        {
            var nodes=doc.SelectNodes("/LANGUAGE/CATEGORY/Entry").Cast<XmlNode>().ToArray();
            if(nodes.Length!=entries.Length) throw new IOException("عدد النصوص مختلف في "+entries[0].file);
            for(int i=0;i<nodes.Length;i++) { var e=entries[i]; if(nodes[i].Attributes["Name"].Value!=e.key||nodes[i].ParentNode.Attributes["Name"].Value!=e.category) throw new IOException("مفاتيح الترجمة مختلفة: "+e.file); nodes[i].InnerText=e.visual; }
        }
        return XmlBytes(doc);
    }
    static void Restore(string root, Journal state)
    {
        string dir=Target(root,".amnesia-arabic");
        foreach(var r in state.records)
        {
            string path=Target(root,r.path);
            if(r.existed) { byte[] bytes=File.ReadAllBytes(Target(dir,r.backup)); if(Hash(bytes)!=r.before) throw new IOException("النسخة الاحتياطية تالفة: "+r.path); }
            string current=File.Exists(path)?Hash(File.ReadAllBytes(path)):null;
            if(current!=r.after && current!=r.before) throw new IOException("تغيّر الملف بعد التثبيت؛ احتُفظ به لحمايته: "+r.path);
        }
        foreach(var r in state.records)
        {
            string path=Target(root,r.path);
            if(r.existed) Write(path,File.ReadAllBytes(Target(dir,r.backup))); else if(File.Exists(path)) File.Delete(path);
        }
    }
    public static void Install(string game, Action<int,string> progress)
    {
        string root=Root(game), statePath=Target(root,".amnesia-arabic/state.json");
        if(File.Exists(statePath))
        {
            var old=Json().Deserialize<Journal>(File.ReadAllText(statePath));
            if(old.status=="installed") { foreach(var r in old.records) if(!File.Exists(Target(root,r.path))||Hash(File.ReadAllBytes(Target(root,r.path)))!=r.after) throw new IOException("تغيّرت ملفات التعريب. أزل الإصدار السابق أو استعد الملفات قبل إعادة التثبيت."); progress(100,"التعريب مثبت بالفعل."); return; }
            if(old.status=="prepared") throw new IOException("يوجد تثبيت لم يكتمل. استخدم إزالة التعريب لاستعادة الملفات أولًا.");
        }
        var changes=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        progress(5,"فحص الحزمة ونسخة اللعبة…");
        using(Stream input=PayloadPath==null?Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"):File.OpenRead(PayloadPath))
        using(var zip=new ZipArchive(input,ZipArchiveMode.Read))
        {
            Manifest manifest; using(var reader=new StreamReader(zip.GetEntry("manifest.json").Open())) manifest=Json().Deserialize<Manifest>(reader.ReadToEnd());
            if(manifest.version!="1.0.0"||manifest.entries.Length!=5008) throw new IOException("حزمة غير متوافقة.");
            int found=0;
            foreach(var binary in manifest.binaries)
            {
                string path=Target(root,binary.name); if(!File.Exists(path)) continue; found++;
                byte[] bytes=File.ReadAllBytes(path); string hash=Hash(bytes);
                if(hash!=binary.before_sha256&&hash!=binary.after_sha256) throw new IOException("إصدار ملف اللعبة غير مدعوم: "+binary.name+"\nلم يتم تعديل أي ملف. راجع صفحة الإصدارات.");
                if(hash==binary.before_sha256) foreach(var b in binary.patches) { byte[] before=Hex(b.before), after=Hex(b.after); if(!bytes.Skip(b.offset).Take(before.Length).SequenceEqual(before)) throw new IOException("البصمة غير مطابقة."); Buffer.BlockCopy(after,0,bytes,b.offset,after.Length); }
                if(Hash(bytes)!=binary.after_sha256) throw new IOException("فشل التحقق من تعديل اتجاه النص."); changes[binary.name]=bytes;
            }
            if(found==0) throw new IOException("لم يُعثر على ملف اللعبة التنفيذي.");
            foreach(var group in manifest.entries.GroupBy(e=>e.file)) changes[group.Key]=Translate(Target(root,group.Key),group.ToArray());
            foreach(var asset in manifest.assets)
            {
                Target(root,asset.path); var entry=zip.GetEntry("assets/"+asset.path); if(entry==null||entry.Length>10000000) throw new IOException("ملف مفقود في الحزمة.");
                using(var s=entry.Open()) using(var ms=new MemoryStream()) { s.CopyTo(ms); byte[] bytes=ms.ToArray(); if(Hash(bytes)!=asset.sha256) throw new IOException("بصمة ملف الخط غير مطابقة."); changes[asset.path]=bytes; }
            }
        }
        string cfg="config/game.cfg", script="script/custom/modules/FontHandler.hps", defaults="config/default_user_settings.cfg";
        string config=File.ReadAllText(Target(root,cfg));
        foreach(string family in new[]{"work_72","architect_56"}) { string replacement=family=="work_72"?"arabic_72":"arabic_56"; if(!config.Contains(family+".fnt")&&!config.Contains(replacement+".fnt")) throw new IOException("صيغة إعدادات الخط غير مدعومة."); config=config.Replace(family+".fnt",replacement+".fnt"); }
        changes[cfg]=Encoding.UTF8.GetBytes(config);
        string code=File.ReadAllText(Target(root,script)); var match=Regex.Match(code,@"tString\s+GetFont\([^)]*\)\s*\{");
        if(!match.Success) throw new IOException("دالة الخط غير معروفة."); int start=match.Index+match.Length, end=start, braces=1;
        while(end<code.Length&&braces>0) { if(code[end]=='{')braces++; if(code[end]=='}')braces--; end++; }
        if(braces!=0) throw new IOException("دالة الخط غير مكتملة.");
        code=code.Substring(0,start)+"\n int lSize = (alSize > 72) ? 96 : ((alSize > 0 && alSize <= 56) ? 56 : 72);\n return \"arabic_\"+lSize+\".fnt\";\n}"+code.Substring(end);
        changes[script]=Encoding.UTF8.GetBytes(code);
        string settings=File.ReadAllText(Target(root,defaults));
        var sound=Regex.Match(settings,@"<Sound\b[^>]*/>"); if(!sound.Success) throw new IOException("إعدادات الترجمة غير معروفة.");
        string section=sound.Value; section=Regex.IsMatch(section,@"\bShowSubtitles\s*=")?Regex.Replace(section,"ShowSubtitles\\s*=\\s*\"[^\"]*\"","ShowSubtitles=\"true\""):section.Replace("<Sound","<Sound ShowSubtitles=\"true\"");
        settings=settings.Substring(0,sound.Index)+section+settings.Substring(sound.Index+sound.Length); changes[defaults]=Encoding.UTF8.GetBytes(settings);
        progress(35,"حفظ نسخة احتياطية…");
        string batch="backups/"+Guid.NewGuid().ToString("N"); var records=new List<Record>();
        foreach(var pair in changes)
        {
            string path=Target(root,pair.Key); bool exists=File.Exists(path); byte[] before=exists?File.ReadAllBytes(path):null;
            string backup=batch+"/"+records.Count+".bin"; if(exists) Write(Target(root,".amnesia-arabic/"+backup),before);
            records.Add(new Record{path=pair.Key,existed=exists,backup=backup,before=exists?Hash(before):null,after=Hash(pair.Value)});
        }
        var state=new Journal{version="1.0.0",status="prepared",records=records.ToArray()}; Write(statePath,Encoding.UTF8.GetBytes(Json().Serialize(state)));
        try
        {
            int n=0; foreach(var pair in changes) { Write(Target(root,pair.Key),pair.Value); if(Hash(File.ReadAllBytes(Target(root,pair.Key)))!=Hash(pair.Value)) throw new IOException("فشل التحقق بعد الكتابة."); progress(40+(++n*55/changes.Count),"تثبيت النصوص والخطوط…"); }
            state.status="installed"; Write(statePath,Encoding.UTF8.GetBytes(Json().Serialize(state))); progress(100,"اكتمل التثبيت. اختر العربية من قائمة اللغات (خانة English).");
        }
        catch { Restore(root,state); state.status="rolled_back"; Write(statePath,Encoding.UTF8.GetBytes(Json().Serialize(state))); throw; }
    }
    public static void Uninstall(string game, Action<int,string> progress)
    {
        string root=Root(game), path=Target(root,".amnesia-arabic/state.json"); if(!File.Exists(path)) throw new IOException("لا توجد نسخة احتياطية أنشأها هذا المثبّت.");
        var state=Json().Deserialize<Journal>(File.ReadAllText(path)); if(state.status=="removed"||state.status=="rolled_back") { progress(100,"التعريب غير مثبت."); return; }
        progress(20,"التحقق من النسخ الاحتياطية…"); Restore(root,state); state.status="removed"; Write(path,Encoding.UTF8.GetBytes(Json().Serialize(state))); progress(100,"استُعيدت الملفات كما كانت قبل التثبيت.");
    }
    public static string FindGameDirectory()
    {
        var roots=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam")};
        foreach(var key in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam"}) { var value=Registry.GetValue(key,key.Contains("CURRENT")?"SteamPath":"InstallPath",null) as string; if(value!=null) roots.Add(value); }
        foreach(var steam in roots.ToArray()) { string file=Path.Combine(steam,"steamapps/libraryfolders.vdf"); if(File.Exists(file)) foreach(Match m in Regex.Matches(File.ReadAllText(file),"\"path\"\\s+\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace("\\\\","\\")); }
        foreach(var steam in roots) { string game=Path.Combine(steam,"steamapps/common/Amnesia Rebirth"); if(File.Exists(Path.Combine(game,"AmnesiaRebirth.exe"))||File.Exists(Path.Combine(game,"AmnesiaRebirth_NoSteam.exe"))) return game; } return "";
    }
}
