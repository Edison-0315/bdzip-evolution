# bdzip进化

**Edison · 个人制作禁止盈利**

Windows x64 的 Bandizip 更新提示与广告控制工具。选择安装文件夹，扫描功能结构，备份后应用修改，也可以恢复原文件与相关用户设置。界面保留 Edison 水印及“个人制作禁止盈利”提示。

本项目公开源码，采用自定义非商业许可，**不属于允许商业使用的 OSI 开源许可**。原始代码的使用、修改与再分发须遵守 [LICENSE](LICENSE)；Capstone 继续适用其原有 BSD 许可。

## 下载与使用

从 [Releases](https://github.com/Edison-0315/bdzip-evolution/releases) 下载发布包，解压后运行 `bdzip-evolution.exe`。

1. 退出 Bandizip 及其更新程序。
2. 选择包含 `Bandizip.exe` 的安装文件夹，点击“扫描目录”。
3. 完整识别后点击“应用关闭”。安装目录需要写入权限时，使用界面中的管理员重启按钮，并保持同一 Windows 用户。
4. 如需撤销，选中同一文件夹，扫描后点击“恢复原版”。

备份位于所选目录的 `.bandizip-control-backup` 文件夹。请保留整个备份；恢复后也不会自动删除。安装程序升级或其他工具再次修改文件后，本工具会拒绝用旧备份覆盖不一致的文件。

## 功能与范围

| 功能 | 处理方式 |
| --- | --- |
| 更新提示 | 关闭用户更新设置，禁用已识别的更新启动函数和 Updater 入口 |
| 广告栏 | 关闭已识别的广告模式写入和两处广告控件显示参数 |
| 广告弹窗 | 关闭已识别的自动启动与 `/ad` 弹窗入口 |
| 文字推广 | 关闭已识别的文字推广加载函数，清理对应用户缓存设置与本地回退页面 |

不按版本号、固定文件哈希或固定地址设白名单。分析器依据字符串引用、导入函数、PE 函数边界与指令结构生成补丁位置；应用前再次校验扫描时的文件哈希。

**无版本号限制不等于兼容所有版本。** 当前仅实现 Windows x64 原生 PE 的已知功能识别。任一必需特征缺失、重复或结构不符时，扫描失败并停止修改。真实样本验证基于 7.46.0.1，其他版本需要实际扫描确认，详见 [兼容性](docs/COMPATIBILITY.md)。

修改会使目标 EXE 原有数字签名失效，升级可能覆盖修改。工具不会提供或分发 Bandizip 原程序，也不包含许可证激活或付费功能解锁逻辑。Bandizip 商标及程序属于其各自权利人，本项目为独立个人项目。

## 从源码构建

需要 Windows x64 和 .NET Framework 4.8。使用系统自带的 64 位 C# 编译器，无需安装 NuGet 包。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

EXE 输出到 `bin/bdzip-evolution.exe`，发布 ZIP 与校验文件输出到 `dist/`。Capstone 动态库压缩后嵌入单文件 EXE，运行时解压到当前用户的 LocalAppData 缓存并校验 SHA-256；工具自身不联网。

发布 GitHub Release 后，`Release binaries` 工作流会从对应标签源码构建并附加 EXE、ZIP 和 SHA-256 校验文件。构建时仅使用 GitHub 为该工作流提供的仓库令牌，无需个人访问令牌。

只读命令行扫描：

```powershell
Start-Process .\bin\bdzip-evolution.exe -ArgumentList '--scan "D:\Apps\Bandizip" "D:\scan-report.txt"' -Wait
```

## 验证与源码结构

[测试说明](docs/TESTING.md)包含本地样本测试方法与边界，仓库不提供第三方原程序样本。

- `src/Native.cs`：PE、导入表、指令及字符串引用分析。
- `src/Analyzer.cs`：功能识别与补丁生成。
- `src/Engine.cs`：备份、校验、应用、回滚、恢复与用户设置。
- `src/App.cs`：Windows Forms 界面和命令行入口。
- `src/Tests.cs`：使用用户自备样本与内存设置执行回归测试。

报告问题请提供工具日志和文件版本，勿上传原始 EXE、恢复备份、个人路径或注册表导出。提交贡献即表示同意贡献按本项目非商业许可发布。
