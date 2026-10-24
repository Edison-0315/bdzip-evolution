using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BandizipControl {
public sealed class Patch {
 public string Feature,Reason;public int Offset;public ulong Address;public byte[] Before,After;
}
public sealed class FilePlan {
 public string RelativePath,OriginalHash,PatchedHash;public byte[] Original,Patched;public List<Patch> Patches=new List<Patch>();
}
public sealed class Analysis {
 public string Root,Version;public List<FilePlan> Files=new List<FilePlan>();public List<string> Details=new List<string>();
 public void Finish(){foreach(var f in Files){f.OriginalHash=Util.Hash(f.Original);f.Patched=(byte[])f.Original.Clone();var used=new HashSet<int>();foreach(var p in f.Patches){Util.Require(p.Offset>=0 && p.Offset+p.Before.Length<=f.Original.Length && p.Before.Length==p.After.Length,"补丁范围不合法。");Util.Require(f.Original.Skip(p.Offset).Take(p.Before.Length).SequenceEqual(p.Before),"补丁原始字节不匹配。");for(int i=0;i<p.After.Length;i++){Util.Require(used.Add(p.Offset+i),"补丁区间重叠。");f.Patched[p.Offset+i]=p.After[i];}}f.PatchedHash=Util.Hash(f.Patched);}}
}
public static class Analyzer {
 static readonly string[] seeds={"/nosleep","/force","LaunchPopupAdWindow","/ad","skin_ad.xml","BandizipAD","STATIC_WEB_AD1","STATIC_WEB_AD2","Korean","txt%d","url%d","https://adv.bandi.so/bandizip.std/line.php","https://adv.bandi.so/bandizip.pro/line.php","https://adv.bandi.so/bandizip.ent/line.php"};
 static Function Unique(IEnumerable<Function> f,string feature){var a=f.Where(x=>x.Complete&&!x.Stub).ToArray();Util.Require(a.Length==1,feature+"：需要唯一且完整的函数匹配，实际为 "+a.Length+" 个。可能不属于已支持的程序结构，或已经被修改；未应用任何新修改。");return a[0];}
 static Patch At(PeImage pe,ulong address,byte[] after,string feature,string reason){int o=pe.Offset(address);return new Patch{Feature=feature,Reason=reason,Address=address,Offset=o,Before=pe.Bytes.Skip(o).Take(after.Length).ToArray(),After=after};}
 static void EntryPatch(PeImage pe,FilePlan fp,Function f,string feature,string reason){Util.Require(f.End-f.Start>=3,"目标函数过短。");fp.Patches.Add(At(pe,f.Start,new byte[]{0x31,0xc0,0xc3},feature,reason));}
 static bool ReachesImport(PeImage pe,ulong addr,string name,int depth,HashSet<ulong> visited){if(!visited.Add(addr))return false;var f=pe.Functions.FirstOrDefault(x=>x.Start==addr);if(f==null||!f.Complete)return false;if(f.CallsImport(name))return true;if(depth==0)return false;return f.Code.Where(i=>i.Call.HasValue).Any(i=>ReachesImport(pe,i.Call.Value,name,depth-1,visited));}
 static List<Instruction> VisibilityWrites(PeImage pe,Function f){
  var candidates=new Dictionary<ulong,List<Instruction>>();
  for(int k=0;k<f.Code.Count;k++){
   var i=f.Code[k];if(i.Mnemonic!="call"||!i.Call.HasValue)continue;
   // Require a local, explicitly prepared third boolean argument and a direct call.
   Instruction writer=null;bool foundArg2=false;
   for(int p=k-1;p>=Math.Max(0,k-5);p--){var prev=f.Code[p];if(prev.Mnemonic=="call"||prev.Mnemonic.StartsWith("j"))break;if(prev.Mnemonic=="lea"&&prev.Operands.StartsWith("rdx, [rsp"))foundArg2=true;
    if(prev.Mnemonic=="mov"&&Regex.IsMatch(prev.Operands,@"^r8d, (e[a-z]{2}|r\d+d|0x[0-9a-f]+|[012])$")&&prev.Bytes.Length>=3){writer=prev;break;}}
   if(writer!=null&&foundArg2){ulong t=i.Call.Value;if(!candidates.ContainsKey(t))candidates[t]=new List<Instruction>();candidates[t].Add(writer);}
  }
  var matches=candidates.Where(x=>x.Value.Count==2&&ReachesImport(pe,x.Key,"ShowWindow",2,new HashSet<ulong>())).ToArray();
  Util.Require(matches.Length==1,"广告栏：无法唯一验证两个可见性调用与 ShowWindow 的关系。");return matches[0].Value;
 }
 public static Analysis Scan(string directory,Action<string> log){
  string root=Files.SafeRoot(directory);log("解析本地 PE，正在按功能特征识别……");
  byte[] mainBytes=Files.ReadTarget(root,"Bandizip.exe");var pe=new PeImage(mainBytes);pe.Analyze(seeds);
  var a=new Analysis{Root=root,Version=System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(root,"Bandizip.exe")).FileVersion??"未知"};
  var main=new FilePlan{RelativePath="Bandizip.exe",Original=mainBytes};a.Files.Add(main);
  var launch=Unique(pe.Functions.Where(f=>f.Has("/nosleep","/force")&&f.CallsImport("CreateProcessW")),"更新启动");
  EntryPatch(pe,main,launch,"更新提示","/nosleep + /force + CreateProcessW 唯一调用函数");
  var banner=Unique(pe.Functions.Where(f=>f.Has("STATIC_WEB_AD1","STATIC_WEB_AD2")&&f.End-f.Start<4096),"广告栏");
  foreach(var ins in VisibilityWrites(pe,banner)){var patch=Enumerable.Repeat((byte)0x90,ins.Bytes.Length).ToArray();patch[0]=0x45;patch[1]=0x31;patch[2]=0xc0;main.Patches.Add(At(pe,ins.Address,patch,"广告栏","两个广告区域的第三可见性参数置为 0；已验证到 ShowWindow 的调用关系"));}
  // Derive the advertisement mode field from this build instead of embedding an offset.
  var modes=banner.Code.Where(i=>i.Mnemonic=="mov").Select(i=>Regex.Match(i.Operands,@"^ecx, dword ptr \[rax \+ (0x[0-9a-f]+)\]$")).Where(m=>m.Success).Select(m=>m.Groups[1].Value).Distinct().ToArray();
  Util.Require(modes.Length==1,"广告栏：无法确定本构建的广告布局状态字段。");string field=modes[0];
  var mode=Unique(pe.Functions.Where(f=>f.Has("Korean")&&f.Code.Take(25).Any(i=>i.Mnemonic=="mov"&&i.Operands=="dword ptr [rcx + "+field+"], 1")&&f.Code.Any(i=>i.Mnemonic=="mov"&&i.Operands.EndsWith("+ "+field+"], 2"))),"广告布局启用");
  uint displacement=Convert.ToUInt32(field.Substring(2),16);var modePatch=new List<byte>{0xc7,0x81};modePatch.AddRange(BitConverter.GetBytes(displacement));modePatch.AddRange(new byte[]{0,0,0,0,0xc3});
  Util.Require(mode.End-mode.Start>=(ulong)modePatch.Count,"广告状态函数太短。");main.Patches.Add(At(pe,mode.Start,modePatch.ToArray(),"广告栏","从本构建推导布局状态字段 "+field+"，将其设为 0；不修改许可字段"));
  var popup=Unique(pe.Functions.Where(f=>f.Has("LaunchPopupAdWindow","/ad")&&f.CallsImport("ShellExecuteW")),"自动广告弹窗");
  EntryPatch(pe,main,popup,"广告弹窗","LaunchPopupAdWindow + /ad + ShellExecuteW");
  var direct=Unique(pe.Functions.Where(f=>f.Has("skin_ad.xml","BandizipAD")&&f.CallsImport("GetMessageW")),"直接广告弹窗");
  EntryPatch(pe,main,direct,"广告弹窗","skin_ad.xml + BandizipAD 互斥量 + 窗口消息循环");
  var text=Unique(pe.Functions.Where(f=>f.Has("txt%d","url%d")&&f.Strings.Count(s=>s.Contains("/line.php"))>=2),"文字推广");
  EntryPatch(pe,main,text,"文字推广","多个 line.php 通道 + txt%d / url%d 条目解析");
  string updater=Path.Combine(root,"Updater.exe");if(File.Exists(updater)){
   byte[] b=Files.ReadTarget(root,"Updater.exe");var up=new PeImage(b);ulong[] exits=up.Imports.Where(x=>x.Value=="ExitProcess").Select(x=>x.Key).ToArray();Util.Require(exits.Length==1,"更新器：ExitProcess 导入不是唯一匹配。");ulong start=up.ImageBase+up.EntryRva;long disp=checked((long)exits[0]-(long)start-8);Util.Require(disp>=int.MinValue&&disp<=int.MaxValue,"更新器：入口到导入表的相对距离超出范围。");
   using(var d=new Decoder()){int o=up.Offset(start);var code=d.Decode(b.Skip(o).Take(32).ToArray(),start);Util.Require(code.Count>0&&!(code[0].Mnemonic=="xor"&&code[0].Operands=="ecx, ecx"),"更新器入口似乎已修改，请先恢复或使用已有备份。");}
   var fp=new FilePlan{RelativePath="Updater.exe",Original=b};var patch=new List<byte>{0x31,0xc9,0xff,0x25};patch.AddRange(BitConverter.GetBytes((int)disp));fp.Patches.Add(At(up,start,patch.ToArray(),"更新提示","从 PE 入口与本构建 ExitProcess 导入生成退出跳转"));a.Files.Add(fp);
  }else a.Details.Add("目录没有独立 Updater.exe；已匹配主程序的更新器启动入口。");
  a.Finish();
  string html=Path.Combine(root,"data","local.html");if(File.Exists(html)){byte[] b=Files.ReadTarget(root,"data\\local.html");byte[] blank=Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Bandizip</title></head><body style=\"margin:0\"></body></html>\r\n");a.Files.Add(new FilePlan{RelativePath="data\\local.html",Original=b,Patched=blank,OriginalHash=Util.Hash(b),PatchedHash=Util.Hash(blank)});}
  foreach(var f in a.Files)foreach(var p in f.Patches)log(p.Feature+"：0x"+p.Address.ToString("x")+"，文件偏移 0x"+p.Offset.ToString("x")+"；"+p.Reason);
  log("功能识别完成。显示版本 "+a.Version+" 仅供参考，不参与放行判断。");return a;
 }
}
}
