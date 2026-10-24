using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Xml.Serialization;
using System.Text;
using Microsoft.Win32;

namespace BandizipControl {
[Serializable] public class SavedSetting { public string Name,Kind,Value;public bool Exists; }
[Serializable] public class SavedPatch { public string Feature,Reason,Address,Before,After;public int Offset; }
[Serializable] public class SavedFile { public string RelativePath,OriginalHash,PatchedHash;public long LastWriteTicks;public int Attributes;public List<SavedPatch> Patches=new List<SavedPatch>(); }
[Serializable] public class Backup {
 public string Format="BandizipControl-1",Status,OriginalDirectory,UserSid,CreatedUtc;
 public List<SavedFile> Files=new List<SavedFile>();public List<SavedSetting> Settings=new List<SavedSetting>();
}
public interface ISettings { string Identity{get;} List<SavedSetting> Read();void WriteDisabled();void Restore(List<SavedSetting> snapshot); }
public sealed class UserSettings:ISettings {
 public string Identity{get{return WindowsIdentity.GetCurrent().User.Value;}}
 public static readonly string[] Names={"update","ad_url","ad_text","ad_t_check"};const string Key="Software\\Bandizip";
 public List<SavedSetting> Read(){var a=new List<SavedSetting>();using(var k=Registry.CurrentUser.OpenSubKey(Key)){foreach(string name in Names){bool exists=k!=null&&k.GetValueNames().Contains(name);var s=new SavedSetting{Name=name,Exists=exists};if(exists){var kind=k.GetValueKind(name);Util.Require(kind==RegistryValueKind.DWord||kind==RegistryValueKind.QWord||kind==RegistryValueKind.String||kind==RegistryValueKind.ExpandString,"配置类型不受支持："+name);s.Kind=kind.ToString();s.Value=Convert.ToString(k.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames),System.Globalization.CultureInfo.InvariantCulture);}a.Add(s);}}return a;}
 public void WriteDisabled(){using(var k=Registry.CurrentUser.CreateSubKey(Key)){k.SetValue("update",2,RegistryValueKind.DWord);k.SetValue("ad_url","",RegistryValueKind.String);k.SetValue("ad_text","",RegistryValueKind.String);k.SetValue("ad_t_check",0,RegistryValueKind.DWord);}}
 public void Restore(List<SavedSetting> a){Util.Require(a!=null&&a.Count==4&&a.Select(s=>s.Name).OrderBy(s=>s).SequenceEqual(Names.OrderBy(s=>s)),"备份设置列表无效。");using(var k=Registry.CurrentUser.CreateSubKey(Key)){foreach(var s in a){if(!s.Exists){k.DeleteValue(s.Name,false);continue;}RegistryValueKind kind;Util.Require(Enum.TryParse(s.Kind,out kind),"备份注册表类型无效。");object value=s.Value;switch(kind){case RegistryValueKind.DWord:value=int.Parse(s.Value,System.Globalization.CultureInfo.InvariantCulture);break;case RegistryValueKind.QWord:value=long.Parse(s.Value,System.Globalization.CultureInfo.InvariantCulture);break;case RegistryValueKind.String:case RegistryValueKind.ExpandString:break;default:throw new InvalidDataException("备份设置类型不受支持。");}k.SetValue(s.Name,value,kind);}}}
}
public sealed class MemorySettings:ISettings {
 public string Identity{get{return "test-user";}}public List<SavedSetting> State=new List<SavedSetting>();
 public MemorySettings(){foreach(var n in UserSettings.Names)State.Add(new SavedSetting{Name=n,Exists=true,Kind=n=="update"||n=="ad_t_check"?"DWord":"String",Value=n=="update"?"1":n=="ad_t_check"?"123":"original-"+n});}
 public List<SavedSetting> Read(){return State.Select(s=>new SavedSetting{Name=s.Name,Exists=s.Exists,Kind=s.Kind,Value=s.Value}).ToList();}
 public void WriteDisabled(){State[0].Value="2";State[1].Value="";State[2].Value="";State[3].Value="0";}
 public void Restore(List<SavedSetting> a){State=a.Select(s=>new SavedSetting{Name=s.Name,Exists=s.Exists,Kind=s.Kind,Value=s.Value}).ToList();}
}
public static class Files {
 public static readonly string[] Allowed={"Bandizip.exe","Updater.exe","data\\local.html"};public const string BackupName=".bandizip-control-backup";
 public static string SafeRoot(string path){Util.Require(!string.IsNullOrWhiteSpace(path),"请选择安装文件夹。");string root=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);Util.Require(Directory.Exists(root),"文件夹不存在。");CheckLinks(root);Util.Require(File.Exists(Path.Combine(root,"Bandizip.exe")),"请选择包含 Bandizip.exe 的安装文件夹。");return root;}
 public static void CheckLinks(string path){var p=Path.GetFullPath(path);while(!string.IsNullOrEmpty(p)){if(File.Exists(p)||Directory.Exists(p))Util.Require((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"为避免写到其他位置，不处理符号链接或重解析路径："+p);string next=Path.GetDirectoryName(p);if(next==p)break;p=next;}}
 public static string Target(string root,string rel){Util.Require(Allowed.Contains(rel),"备份中含有不允许的文件路径。");string p=Path.GetFullPath(Path.Combine(root,rel));Util.Require(p.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase),"目标超出所选文件夹。");CheckLinks(p);return p;}
 public static byte[] ReadTarget(string root,string rel){string p=Target(root,rel);var info=new FileInfo(p);Util.Require(info.Exists&&info.Length>0&&info.Length<=64*1024*1024,"目标文件不存在、为空或超过 64 MiB："+rel);using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.Read)){byte[] b=new byte[checked((int)f.Length)];int n=0;while(n<b.Length){int r=f.Read(b,n,b.Length-n);if(r==0)throw new EndOfStreamException();n+=r;}return b;}}
 public static void WriteNew(string p,byte[] b){using(var f=new FileStream(p,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(b,0,b.Length);f.Flush(true);}}
 public static void Replace(string path,byte[] b){string temp=path+".bzctl-"+Guid.NewGuid().ToString("N")+".tmp";try{WriteNew(temp,b);File.Replace(temp,path,null);}finally{if(File.Exists(temp))File.Delete(temp);}}
 public static string BackupDir(string root){string p=Path.Combine(root,BackupName);CheckLinks(p);return p;}
 public static void EnsureNotRunning(string root){foreach(var p in Process.GetProcesses()){try{if(p.MainModule!=null){string f=p.MainModule.FileName;if(f.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)&&new[]{"Bandizip.exe","Updater.exe","web32.exe"}.Contains(Path.GetFileName(f),StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("请先关闭所选目录中的 Bandizip、Updater 和 web32，然后重试。");}}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException e){if(e.Message.StartsWith("请先关闭"))throw;}finally{p.Dispose();}}}
 public static Backup LoadBackup(string root){string dir=BackupDir(root),path=Path.Combine(dir,"manifest.xml");if(!File.Exists(path))return null;Util.Require(new FileInfo(path).Length<=1024*1024,"备份清单过大。");CheckLinks(path);Backup b;var settings=new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null};using(var r=System.Xml.XmlReader.Create(path,settings))b=(Backup)new XmlSerializer(typeof(Backup)).Deserialize(r);
  Util.Require(b.Format=="BandizipControl-1"&&b.Files.Count>=1&&b.Files.Count<=3&&b.Files.Select(f=>f.RelativePath).Distinct().Count()==b.Files.Count&&b.Files.Any(f=>f.RelativePath=="Bandizip.exe"),"备份清单格式无效。");
  foreach(var f in b.Files){Target(root,f.RelativePath);Util.Require(System.Text.RegularExpressions.Regex.IsMatch(f.OriginalHash??"","^[0-9a-f]{64}$")&&System.Text.RegularExpressions.Regex.IsMatch(f.PatchedHash??"","^[0-9a-f]{64}$"),"备份哈希无效。");string p=Path.Combine(dir,"original",f.RelativePath);CheckLinks(p);Util.Require(File.Exists(p)&&Util.FileHash(p)==f.OriginalHash,"原文件备份缺失或校验失败："+f.RelativePath);}return b;
 }
 public static void SaveBackup(string root,Backup b){string path=Path.Combine(BackupDir(root),"manifest.xml"),temp=path+".tmp-"+Guid.NewGuid().ToString("N");try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new XmlSerializer(typeof(Backup)).Serialize(f,b);f.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
public static class Engine {
 public static string State(string root,Backup b){int orig=0,patched=0;foreach(var f in b.Files){string h=Util.FileHash(Files.Target(root,f.RelativePath));if(h==f.OriginalHash)orig++;else if(h==f.PatchedHash)patched++;else return "Changed";}if(orig==b.Files.Count)return "Original";if(patched==b.Files.Count)return "Applied";return "Partial";}
 public static void Apply(Analysis a,ISettings settings,Action<string> log,Action<int> fault){
  string root=Files.SafeRoot(a.Root);Files.EnsureNotRunning(root);var existing=Files.LoadBackup(root);
  if(existing!=null){Util.Require(State(root,existing)=="Original","已有备份对应的文件不是完整原版，请先恢复。");Util.Require(existing.UserSid==settings.Identity,"已有备份属于其他 Windows 用户，请使用原用户恢复。");}
  foreach(var f in a.Files)Util.Require(Util.Hash(Files.ReadTarget(root,f.RelativePath))==f.OriginalHash,"扫描后文件已改变，请重新扫描："+f.RelativePath);
  var b=new Backup{Status="Prepared",OriginalDirectory=root,UserSid=settings.Identity,CreatedUtc=DateTime.UtcNow.ToString("o"),Settings=settings.Read()};string dir=Files.BackupDir(root);
  if(existing==null){Util.Require(!Directory.Exists(dir),"备份目录已存在但缺少有效清单，请保留并检查该目录。");Directory.CreateDirectory(Path.Combine(dir,"original"));}
  foreach(var f in a.Files){string source=Files.Target(root,f.RelativePath),saved=Path.Combine(dir,"original",f.RelativePath);if(existing==null){Directory.CreateDirectory(Path.GetDirectoryName(saved));Files.WriteNew(saved,f.Original);}else Util.Require(File.Exists(saved)&&Util.FileHash(saved)==f.OriginalHash,"已有备份不对应本次文件。");
   var sf=new SavedFile{RelativePath=f.RelativePath,OriginalHash=f.OriginalHash,PatchedHash=f.PatchedHash,LastWriteTicks=File.GetLastWriteTimeUtc(source).Ticks,Attributes=(int)File.GetAttributes(source)};sf.Patches=f.Patches.Select(p=>new SavedPatch{Feature=p.Feature,Reason=p.Reason,Address="0x"+p.Address.ToString("x"),Offset=p.Offset,Before=Util.Hex(p.Before),After=Util.Hex(p.After)}).ToList();b.Files.Add(sf);}
  Files.SaveBackup(root,b);log("完整原文件及四项设置已备份："+dir);var written=new List<FilePlan>();bool regTouched=false;
  try{foreach(var f in a.Files){Util.Require(Util.FileHash(Files.Target(root,f.RelativePath))==f.OriginalHash,"文件在写入前发生改变："+f.RelativePath);Files.Replace(Files.Target(root,f.RelativePath),f.Patched);written.Add(f);Util.Require(Util.FileHash(Files.Target(root,f.RelativePath))==f.PatchedHash,"写入后校验失败。");if(fault!=null)fault(written.Count);}
   regTouched=true;settings.WriteDisabled();b.Status="Applied";Files.SaveBackup(root,b);log("已应用。文件哈希校验通过。");
  }catch(Exception ex){var errors=new List<string>();foreach(var f in written.AsEnumerable().Reverse())try{string current=Util.FileHash(Files.Target(root,f.RelativePath));Util.Require(current==f.PatchedHash||current==f.OriginalHash,"回滚时文件已被其他程序改变。");Files.Replace(Files.Target(root,f.RelativePath),f.Original);}catch(Exception e){errors.Add(e.Message);}if(regTouched)try{settings.Restore(b.Settings);}catch(Exception e){errors.Add(e.Message);}b.Status=errors.Count==0?"RolledBack":"RecoveryRequired";try{Files.SaveBackup(root,b);}catch(Exception e){errors.Add(e.Message);}throw new InvalidOperationException(ex.Message+"\r\n"+(errors.Count==0?"已自动回滚本次变更。":"恢复未完成，保留备份并使用恢复功能："+string.Join("；",errors)),ex);}
 }
 public static void Restore(string directory,ISettings settings,Action<string> log,Action<int> fault){
  string root=Files.SafeRoot(directory);Files.EnsureNotRunning(root);var b=Files.LoadBackup(root);Util.Require(b!=null,"所选目录没有本工具创建的备份。");Util.Require(b.UserSid==settings.Identity,"备份属于其他 Windows 用户；请使用原用户恢复。");Util.Require(State(root,b)!="Changed","程序已升级或被其他方式修改；为避免覆盖新文件，已拒绝恢复。");var before=b.Files.ToDictionary(f=>f.RelativePath,f=>Files.ReadTarget(root,f.RelativePath));var regBefore=settings.Read();var written=new List<SavedFile>();bool regTouched=false;
  try{foreach(var f in b.Files){string p=Files.Target(root,f.RelativePath);Util.Require(Util.Hash(Files.ReadTarget(root,f.RelativePath))==Util.Hash(before[f.RelativePath]),"恢复前文件已改变。");byte[] original=File.ReadAllBytes(Path.Combine(Files.BackupDir(root),"original",f.RelativePath));Files.Replace(p,original);written.Add(f);File.SetLastWriteTimeUtc(p,new DateTime(f.LastWriteTicks,DateTimeKind.Utc));Util.Require(Util.FileHash(p)==f.OriginalHash,"恢复后哈希不匹配。");if(fault!=null)fault(written.Count);}regTouched=true;settings.Restore(b.Settings);b.Status="Restored";Files.SaveBackup(root,b);log("原文件和四项用户设置已恢复；备份继续保留。");}
  catch(Exception ex){var errors=new List<string>();foreach(var f in written.AsEnumerable().Reverse())try{Files.Replace(Files.Target(root,f.RelativePath),before[f.RelativePath]);}catch(Exception e){errors.Add(e.Message);}if(regTouched)try{settings.Restore(regBefore);}catch(Exception e){errors.Add(e.Message);}b.Status=errors.Count==0?"RestoreRolledBack":"RecoveryRequired";try{Files.SaveBackup(root,b);}catch(Exception e){errors.Add(e.Message);}throw new InvalidOperationException(ex.Message+"\r\n"+(errors.Count==0?"恢复操作已回滚，保留原先状态。":"恢复中断；请保留备份并重试。"),ex);}
 }
}
}
