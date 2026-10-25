# Windows 客户端 · 免 .NET 便携版

用于打轴、翻译、校对、ASS 样式调整和硬字幕压制。客户端功能版本 0.2.0，便携包内置 .NET 10.0.13，适用于 Windows 10/11 x64。

## 普通用户：三步启动

1. 从 [Releases](https://github.com/Azumi207/-/releases) 下载 **Frameflow-Client-Windows-0.2.0-Portable-win-x64.zip**。
2. 右键 ZIP → **全部解压**，选择桌面或其他可写目录。不要直接在压缩包里运行。
3. 打开 Frameflow 文件夹，双击 **启动帧语.cmd**（start-client.cmd 为同一入口）。

**不需要自己安装 .NET、Python 或开发工具，也不需要管理员权限。** 首次会自动联网下载独立媒体组件并验证 SHA256；成功后本地字幕编辑可离线使用。准备完成后也可直接打开 Frameflow.exe。

请保留整个文件夹，不要只复制 EXE。包内「便携版快速开始.txt」提供故障排查。软件尚未数字签名；仅从本仓库下载、核对 SHA256，不建议关闭 Windows 防护。

## 媒体组件与网络

FFmpeg 是独立 GPL 组件，未附带在公开 ZIP 内；首次从上游下载固定版本并严格校验。下载失败可重试，校验失败请等待维护者审核更新，不能跳过校验。
有原始下载包时，可使用 `powershell -NoProfile -ExecutionPolicy Bypass -File install-ffmpeg.ps1 -ArchivePath 完整ZIP路径 -Destination Engine`。
暂存的 .ffmpeg-download-* 保留供排查，可自行清理；保留 Engine。内置 .NET 的两份许可及第三方通知在 Licenses 下。

## 更新与卸载

新版本解压到新文件夹，不覆盖旧版，先备份字幕再打开验证。内置运行时不会依赖系统安装或自动更新，需要下载新版便携包。关闭程序后删除程序目录即可卸载；不要删掉自己的字幕和视频。

## 源码构建

开发机器需要 .NET 10 SDK：`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-client.ps1 -WithoutMedia`。默认自包含 win-x64，不裁剪 WPF/MoonSharp，不要求用户安装运行时。已有输出目录拒绝覆盖。
使用 `python scripts/package-client.py --publish 编译输出目录` 生成便携 ZIP；该脚本拒绝包含 Engine、数据或缺失运行时许可证的构建。开发说明包含隔离运行时验证命令。

[完整使用说明](../docs/使用说明.md) · [便携版快速开始](../docs/便携版快速开始.txt)

## 边听边打轴（本地预览）

Ctrl+P 连续播放，F8 暂存起点，F9 提交起止时间，声音不停止。可选“完成后下一句”，Esc 取消暂存。旧 S/H/D/E 与 Ctrl+3/4 不变。见 `../docs/边听边打轴说明.txt`。
