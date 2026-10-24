using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BandizipControl {
public static class Util {
 public static string Hash(byte[] b) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
 public static string FileHash(string p) { return Hash(File.ReadAllBytes(p)); }
 public static byte[] Hex(string s) { if(s.Length%2!=0) throw new InvalidDataException("Invalid hex"); return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray(); }
 public static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-","").ToLowerInvariant(); }
 public static void Require(bool ok,string why) { if(!ok) throw new InvalidDataException(why); }
}
public sealed class Instruction {
 public ulong Address; public byte[] Bytes; public string Mnemonic,Operands; public ulong? Reference,Call; public string Import;
 public bool Is(string m,string o) { return Mnemonic==m && Operands==o; }
}
public sealed class Function {
 public ulong Start,End; public List<Instruction> Code=new List<Instruction>(); public HashSet<string> Strings=new HashSet<string>();
 public bool Complete { get { return Code.Count>0 && Code.Sum(i=>i.Bytes.Length)==(long)(End-Start); } }
 public bool Has(params string[] s) { return s.All(x=>Strings.Contains(x)); }
 public bool CallsImport(string s) { return Code.Any(i=>i.Import==s && i.Mnemonic=="call"); }
 public bool Stub { get { return Code.Count>=2 && Code[0].Mnemonic=="xor" && Code[0].Operands=="eax, eax" && Code[1].Mnemonic=="ret"; } }
}
public sealed class Section { public string Name; public uint Rva,VirtualSize,Size,Offset,Flags; }
public sealed class PeImage {
 public byte[] Bytes; public ulong ImageBase; public uint EntryRva; public int Optional,Directories; public List<Section> Sections=new List<Section>();
 public Dictionary<ulong,string> Imports=new Dictionary<ulong,string>(); public List<Function> Functions=new List<Function>();
 public ushort U16(int o) { Bounds(o,2); return BitConverter.ToUInt16(Bytes,o); }
 public uint U32(int o) { Bounds(o,4); return BitConverter.ToUInt32(Bytes,o); }
 public ulong U64(int o) { Bounds(o,8); return BitConverter.ToUInt64(Bytes,o); }
 void Bounds(int o,int n) { Util.Require(o>=0 && n>=0 && (long)o+n<=Bytes.Length,"PE 数据超出文件范围。"); }
 public PeImage(byte[] b) {
  Bytes=b; Util.Require(b.Length>=256 && b[0]==77 && b[1]==90,"目标不是 Windows PE 程序。");
  int pe=checked((int)U32(60)); Util.Require(U32(pe)==0x4550,"PE 签名无效。");
  Util.Require(U16(pe+4)==0x8664,"当前识别器支持 x64 原生 Bandizip；无法将 x64 补丁应用于其他架构。");
  Optional=pe+24; Util.Require(U16(Optional)==0x20b,"需要 PE32+ 映像。"); Directories=Optional+112;
  Util.Require(U32(Optional+108)>=14,"PE 数据目录不完整。"); ImageBase=U64(Optional+24); EntryRva=U32(Optional+16);
  int count=U16(pe+6), table=Optional+U16(pe+20); Util.Require(count>0 && count<128,"PE 节数量无效。");
  for(int k=0;k<count;k++) { int o=table+k*40; Bounds(o,40); var s=new Section { Name=Encoding.ASCII.GetString(b,o,8).TrimEnd('\0'),VirtualSize=U32(o+8),Rva=U32(o+12),Size=U32(o+16),Offset=U32(o+20),Flags=U32(o+36) }; Util.Require((long)s.Offset+s.Size<=b.Length,"PE 节超出文件范围。"); Sections.Add(s); }
  for(int a=0;a<Sections.Count;a++) for(int c=a+1;c<Sections.Count;c++) {
   var x=Sections[a];var y=Sections[c];Util.Require(x.Size==0||y.Size==0||((ulong)x.Offset+x.Size<=y.Offset || (ulong)y.Offset+y.Size<=x.Offset),"PE 原始节范围重叠。");
  }
  ReadImports(); ReadFunctions();
 }
 public int Offset(ulong va) { Util.Require(va>=ImageBase,"VA 小于映像基址。"); ulong r=va-ImageBase; var a=Sections.Where(s=>r>=s.Rva && r<(ulong)s.Rva+s.Size).ToArray(); Util.Require(a.Length==1,"无法唯一映射 PE 地址：0x"+va.ToString("x")); return checked((int)(a[0].Offset+r-a[0].Rva)); }
 public ulong Va(int offset) { var a=Sections.Where(s=>offset>=s.Offset && (ulong)offset<(ulong)s.Offset+s.Size).ToArray(); Util.Require(a.Length==1,"字符串不在唯一映射节内。"); return ImageBase+a[0].Rva+(ulong)offset-a[0].Offset; }
 string CString(int o) { int z=o;while(z<Bytes.Length && Bytes[z]!=0 && z-o<1024)z++; Util.Require(z<Bytes.Length && Bytes[z]==0,"PE 字符串未终止。");return Encoding.ASCII.GetString(Bytes,o,z-o); }
 void Thunks(uint names,uint iat) {
  int q=Offset(ImageBase+names);
  for(int j=0;j<16384;j++) { ulong v=U64(q+j*8);if(v==0)return; string n=(v>>63)!=0?"#"+(v&65535):CString(Offset(ImageBase+v)+2);Imports[ImageBase+iat+(ulong)j*8]=n; }
  throw new InvalidDataException("PE 导入表过长。");
 }
 void ReadImports() {
  uint r=U32(Directories+8);if(r!=0){int p=Offset(ImageBase+r);for(int k=0;k<1024;k++,p+=20){if(U32(p)==0&&U32(p+12)==0&&U32(p+16)==0)break; Thunks(U32(p)==0?U32(p+16):U32(p),U32(p+16));}}
  r=U32(Directories+13*8);if(r!=0){int p=Offset(ImageBase+r);for(int k=0;k<1024;k++,p+=32){if(U32(p)==0&&U32(p+4)==0)break;Util.Require((U32(p)&1)!=0,"不支持 VA 格式的延迟导入表。");Thunks(U32(p+16),U32(p+12));}}
 }
 void ReadFunctions() {
  uint r=U32(Directories+24),size=U32(Directories+28); Util.Require(r!=0 && size%12==0 && size/12<100000,"缺少可验证的 x64 函数范围表。"); int p=Offset(ImageBase+r);
  for(int j=0;j<size/12;j++) { uint a=U32(p+j*12),z=U32(p+j*12+4); Util.Require(z>a && z-a<4*1024*1024,"函数范围无效。"); int first=Offset(ImageBase+a),last=Offset(ImageBase+z-1);Util.Require(last-first==z-a-1,"函数不连续映射。");Functions.Add(new Function{Start=ImageBase+a,End=ImageBase+z}); }
 }
 public void Analyze(string[] seeds) {
  var refs=new Dictionary<ulong,List<string>>();
  foreach(string seed in seeds.Distinct()) foreach(var enc in new[]{Encoding.Unicode,Encoding.ASCII}) {
   byte[] needle=enc.GetBytes(seed+"\0");
   foreach(var sec in Sections.Where(s=>(s.Flags&0x20000000)==0 && s.Size>0)) {
    int limit=checked((int)(sec.Offset+sec.Size))-needle.Length;
    for(int o=(int)sec.Offset;o<=limit;o++) { if(Bytes[o]!=needle[0])continue; int k=1;for(;k<needle.Length&&Bytes[o+k]==needle[k];k++);if(k!=needle.Length)continue; ulong va=Va(o);if(!refs.ContainsKey(va))refs[va]=new List<string>();refs[va].Add(seed); }
   }
  }
  using(var cs=new Decoder()) foreach(var f in Functions) {
   int offset=Offset(f.Start);byte[] body=new byte[checked((int)(f.End-f.Start))];Buffer.BlockCopy(Bytes,offset,body,0,body.Length);f.Code=cs.Decode(body,f.Start);
   foreach(var i in f.Code) {
    ulong addr; if(i.Reference.HasValue) { addr=i.Reference.Value; if(refs.ContainsKey(addr))foreach(string s in refs[addr])f.Strings.Add(s);if(Imports.ContainsKey(addr))i.Import=Imports[addr]; }
   }
  }
 }
}
public sealed class Decoder:IDisposable {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] struct NativeInsn {
  public uint Id;public ulong Address;public ushort Size;[MarshalAs(UnmanagedType.ByValArray,SizeConst=24)]public byte[] Bytes;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Mnemonic;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=160)]public string Operands;public IntPtr Detail;
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetDllDirectory(string path);
 [DllImport("capstone.dll",CallingConvention=CallingConvention.Cdecl)]static extern int cs_open(int arch,int mode,out UIntPtr handle);
 [DllImport("capstone.dll",CallingConvention=CallingConvention.Cdecl)]static extern UIntPtr cs_disasm(UIntPtr handle,byte[] code,UIntPtr size,ulong address,UIntPtr count,out IntPtr insn);
 [DllImport("capstone.dll",CallingConvention=CallingConvention.Cdecl)]static extern void cs_free(IntPtr insn,UIntPtr count);
 [DllImport("capstone.dll",CallingConvention=CallingConvention.Cdecl)]static extern int cs_close(ref UIntPtr handle);
 static bool loaded;static readonly object gate=new object();UIntPtr handle;
 static void Load() {
  lock(gate) { if(loaded)return;
   const string digest="651096794b3c3c2a1df24345e22844a0311fca56a4f74ea1cfa29107983760e1";
   string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BandizipControl","runtime",digest),path=Path.Combine(dir,"capstone.dll");Directory.CreateDirectory(dir);
   if(!File.Exists(path)||Util.FileHash(path)!=digest) {
    byte[] b;using(var src=Assembly.GetExecutingAssembly().GetManifestResourceStream("capstone.gz"))using(var gz=new GZipStream(src,CompressionMode.Decompress))using(var ms=new MemoryStream()){gz.CopyTo(ms);b=ms.ToArray();}
    Util.Require(Util.Hash(b)==digest,"内置反汇编组件校验失败。");string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(temp,b);if(File.Exists(path))File.Delete(path);File.Move(temp,path);
   }
   // Remove the current working directory from the native library search path.
   SetDllDirectory("");if(LoadLibraryEx(path,IntPtr.Zero,0x1100)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法加载内置反汇编组件。");loaded=true;
  }
 }
 public Decoder(){Load();Util.Require(Marshal.SizeOf(typeof(NativeInsn))==248,"反汇编 ABI 不匹配。");if(cs_open(3,8,out handle)!=0)throw new InvalidOperationException("反汇编初始化失败。");}
 static readonly Regex rip=new Regex(@"\[rip\s*([+-])\s*(0x[0-9a-f]+|\d+)\]",RegexOptions.Compiled);
 public List<Instruction> Decode(byte[] bytes,ulong address) {
  IntPtr p;UIntPtr n=cs_disasm(handle,bytes,(UIntPtr)bytes.Length,address,UIntPtr.Zero,out p);var result=new List<Instruction>();
  try{for(ulong k=0;k<n.ToUInt64();k++) {var x=(NativeInsn)Marshal.PtrToStructure(new IntPtr(p.ToInt64()+checked((long)k*248)),typeof(NativeInsn)); var i=new Instruction{Address=x.Address,Bytes=x.Bytes.Take(x.Size).ToArray(),Mnemonic=x.Mnemonic,Operands=x.Operands};
   var m=rip.Match(i.Operands);if(m.Success){long d=m.Groups[2].Value.StartsWith("0x")?Convert.ToInt64(m.Groups[2].Value.Substring(2),16):long.Parse(m.Groups[2].Value);if(m.Groups[1].Value=="-")d=-d;i.Reference=unchecked((ulong)((long)i.Address+i.Bytes.Length+d));}
   if((i.Mnemonic=="call"||i.Mnemonic=="jmp")&&i.Operands.StartsWith("0x"))i.Call=Convert.ToUInt64(i.Operands.Substring(2),16);result.Add(i);
  }}finally{if(p!=IntPtr.Zero)cs_free(p,n);}return result;
 }
 public void Dispose(){cs_close(ref handle);}
}
}
