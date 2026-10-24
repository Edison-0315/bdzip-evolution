# 验证

`test-results-20261024.txt` 是本地原版 7.46.0.1 x64 样本的自测试记录。测试使用样本副本与内存注册表实现，不操作安装目录和真实注册表。

覆盖识别、应用、恢复及重复循环、应用和恢复失败回滚、扫描后文件变化、版本资源变化、PE 基址变化、节文件偏移变化、特征缺失和歧义、不支持架构、损坏备份、截断 PE。

需要自行准备合法取得的原版样本目录，至少包含分析器需要的 `Bandizip.exe`、`Updater.exe` 及对应 `data` 文件。测试输出目录必须尚不存在；它包含第三方程序副本，必须保留在被忽略的 `work/` 目录中，不要上传。

```powershell
.\scripts\build.ps1
$process = Start-Process .\bin\bdzip-evolution.exe -ArgumentList '--self-test "D:\LocalFixtures\Bandizip" ".\work\self-test-new"' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Self-test failed; inspect work/self-test-new/test-error.txt' }
Get-Content .\work\self-test-new\test-results.txt
```

测试不是跨版本兼容承诺。GitHub Actions 仅执行构建和打包，不下载或分发 Bandizip。实际 UI、目标程序启动、解压及恢复应在用户自己的测试副本中检查。此前本地验证过修补样本打开 ZIP 的窗口就绪、更新与 `/ad` 入口退出；这不能证明所有运行时场景或网络广告路径均已覆盖。
