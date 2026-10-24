using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Security.Principal;
using System.Reflection;
using System.Diagnostics;

namespace BandizipControl {
public sealed class MainForm:Form {
 TextBox folder=new TextBox(),log=new TextBox();Button browse=new Button(),scan=new Button(),apply=new Button(),restore=new Button(),admin=new Button();Label status=new Label();Analysis plan;bool busy;
 public MainForm(string initial){
  Text="bdzip进化 · Edison";ClientSize=new Size(850,615);MinimumSize=new Size(740,540);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",9F);AutoScaleMode=AutoScaleMode.Dpi;
  var top=new Panel{Dock=DockStyle.Top,Width=ClientSize.Width,Height=195,Padding=new Padding(18)};
  var title=new Label{Text="选择安装文件夹，识别后关闭更新与广告",AutoSize=true,Location=new Point(18,17),Font=new Font(Font.FontFamily,13F,FontStyle.Bold)};top.Controls.Add(title);
  var watermark=new Label{Text="Edison",AutoSize=false,TextAlign=ContentAlignment.TopRight,ForeColor=Color.SlateGray,Font=new Font(Font.FontFamily,18F,FontStyle.Bold),Location=new Point(675,11),Size=new Size(155,35),Anchor=AnchorStyles.Top|AnchorStyles.Right};top.Controls.Add(watermark);
  var hint=new Label{Text="按程序功能识别，不限制版本号。当前支持 x64 原生程序；无法核实的结构不会修改。",AutoSize=true,Location=new Point(18,48)};top.Controls.Add(hint);
  folder.SetBounds(18,78,660,27);folder.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;folder.Text=initial;folder.TextChanged+=(s,e)=>{plan=null;apply.Enabled=false;restore.Enabled=false;status.Text="待扫描";};top.Controls.Add(folder);
  browse.Text="选择文件夹…";browse.SetBounds(690,76,140,30);browse.Anchor=AnchorStyles.Top|AnchorStyles.Right;browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{Description="请选择包含 Bandizip.exe 的安装文件夹",SelectedPath=Directory.Exists(folder.Text)?folder.Text:"",ShowNewFolderButton=false})if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;};top.Controls.Add(browse);
  scan.Text="扫描目录";scan.SetBounds(18,118,120,34);scan.Click+=async(s,e)=>await Scan();top.Controls.Add(scan);
  apply.Text="应用关闭";apply.SetBounds(150,118,120,34);apply.Enabled=false;apply.Click+=async(s,e)=>await Apply();top.Controls.Add(apply);
  restore.Text="恢复原版";restore.SetBounds(282,118,120,34);restore.Enabled=false;restore.Click+=async(s,e)=>await Restore();top.Controls.Add(restore);
  admin.Text="以管理员身份重启";admin.SetBounds(414,118,165,34);admin.Visible=!IsAdmin();admin.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo{FileName=Assembly.GetExecutingAssembly().Location,Arguments="--folder \""+folder.Text.Replace("\"","")+"\"",UseShellExecute=true,Verb="runas"});Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"未重启");}};top.Controls.Add(admin);
  status.SetBounds(18,166,810,23);status.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;status.Text="待扫描";top.Controls.Add(status);
  var bottom=new Panel{Dock=DockStyle.Bottom,Width=ClientSize.Width,Height=84};
  bottom.Controls.Add(new Label{AutoSize=true,Location=new Point(18,8),Text="修改会使原 EXE 数字签名失效；升级可能覆盖修改。\r\n完整备份保存在所选目录的 .bandizip-control-backup 文件夹；恢复后仍保留备份。"});
  bottom.Controls.Add(new Label{AutoSize=true,Location=new Point(18,53),Text="个人制作禁止盈利",ForeColor=Color.DarkRed,Font=new Font(Font.FontFamily,10F,FontStyle.Bold)});
  var license=new LinkLabel{Text="第三方许可",AutoSize=true,Location=new Point(750,55),Anchor=AnchorStyles.Top|AnchorStyles.Right};license.LinkClicked+=(s,e)=>{string text;using(var sr=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("CapstoneLicense")))text=sr.ReadToEnd();using(var dialog=new Form{Text="第三方许可 · Capstone",Size=new Size(760,560),StartPosition=FormStartPosition.CenterParent}){dialog.Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Font=new Font("Consolas",10F),Text=text});dialog.ShowDialog(this);}};bottom.Controls.Add(license);
  log.Dock=DockStyle.Fill;log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Both;log.WordWrap=false;log.Font=new Font("Consolas",9F);log.BackColor=SystemColors.Window;var middle=new Panel{Dock=DockStyle.Fill,Padding=new Padding(18,0,18,8)};middle.Controls.Add(log);Controls.Add(middle);Controls.Add(bottom);Controls.Add(top);
  FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show(this,"正在处理文件，请等待完成。","请稍候");}};
 }
 static bool IsAdmin(){return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}
 void Log(string text){if(InvokeRequired){BeginInvoke(new Action<string>(Log),text);return;}log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
 void Busy(bool value){busy=value;folder.Enabled=browse.Enabled=scan.Enabled=admin.Enabled=!value;if(value){apply.Enabled=restore.Enabled=false;Cursor=Cursors.WaitCursor;}else Cursor=Cursors.Default;}
 async Task Scan(){if(busy)return;Busy(true);plan=null;log.Clear();string selected=folder.Text;try{
  string root=Files.SafeRoot(selected);var backup=await Task.Run(()=>Files.LoadBackup(root));
  if(backup!=null){string st=await Task.Run(()=>Engine.State(root,backup));Log("发现本工具备份，当前文件状态："+st);if(st=="Applied"){status.Text="已应用，可恢复；无需重复修改。";restore.Enabled=true;return;}if(st=="Partial"){status.Text="检测到中断状态，请先恢复。";restore.Enabled=true;return;}Util.Require(st!="Changed","备份后程序已升级或被修改；请保留备份，勿覆盖新文件。");restore.Enabled=true;}
  plan=await Task.Run(()=>Analyzer.Scan(root,Log));status.Text="已识别四类功能，可应用关闭。版本 "+plan.Version+"（仅显示）";apply.Enabled=true;
 }catch(Exception ex){status.Text="未能完整识别，未修改目标。";Log(ex.Message);Log("已有其他工具补丁的目录可能无法重新识别；可先使用对应原备份恢复。保留备份，不要强行套用偏移。");}finally{Busy(false);}}
 async Task Apply(){if(plan==null||busy)return;if(MessageBox.Show(this,"将关闭更新提示、广告栏、广告弹窗及文字推广入口。\r\n\r\n工具会先完整备份，EXE 数字签名将失效。继续应用？","应用到所选目录",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;Busy(true);try{Analysis p=plan;await Task.Run(()=>Engine.Apply(p,new UserSettings(),Log,null));status.Text="已完成，可启动 Bandizip 检查。";restore.Enabled=true;plan=null;}catch(Exception ex){status.Text="应用未完成；请查看日志。";Log(ex.Message);if(ex is UnauthorizedAccessException)Log("没有写入权限时，请使用“以管理员身份重启”；使用同一个 Windows 用户。");}finally{Busy(false);scan.Enabled=true;}}
 async Task Restore(){if(busy)return;if(MessageBox.Show(this,"恢复本工具备份的原文件和四项用户设置？\r\n请先关闭 Bandizip。备份不会删除。","恢复原版",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;Busy(true);try{string p=folder.Text;await Task.Run(()=>Engine.Restore(p,new UserSettings(),Log,null));status.Text="已恢复原版；可重新扫描。";plan=null;}catch(Exception ex){Log(ex.Message);status.Text="恢复未完成；请查看日志。";}finally{Busy(false);scan.Enabled=true;}}
}
public static class Program {
 [STAThread]public static int Main(string[] args){try{
  if(args.Length>=3&&args[0]=="--scan"){var sb=new StringBuilder();var a=Analyzer.Scan(args[1],s=>sb.AppendLine(s));sb.AppendLine("Identified patches: "+a.Files.Sum(f=>f.Patches.Count));File.WriteAllText(args[2],sb.ToString(),new UTF8Encoding(false));return 0;}
  if(args.Length>=3&&args[0]=="--self-test"){SelfTests.Run(args[1],args[2]);return 0;}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Bandizip");if(args.Length>=2&&args[0]=="--folder")path=args[1];Application.Run(new MainForm(path));return 0;
 }catch(Exception ex){if(args.Length>=3){string report=args[0]=="--self-test"?Path.Combine(args[2],"test-error.txt"):args[2];File.WriteAllText(report,ex.ToString(),new UTF8Encoding(false));return 1;}MessageBox.Show(ex.Message,"Bandizip 控制工具");return 1;}}
}
}
