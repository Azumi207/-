# Windows 客户端

用于打轴、翻译、校对、ASS 样式调整以及硬字幕压制。当前客户端版本 0.2.0。

## 使用发行包

1. 从仓库 Releases 下载 `Frameflow-Client-Windows-0.2.0.zip`，完整解压到可写目录，不要只取 EXE。
2. 安装微软官方 [.NET 9 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/9.0)（不是只装 ASP.NET Runtime）。
3. 双击 `start-client.cmd`。首次下载独立 FFmpeg 9.0.2，核对整个 ZIP 及 ffmpeg/ffprobe/许可证文件的 SHA256；不匹配立即停止，不绕过检查。
4. 使用“文件”打开视频与字幕；通过“团队”登录工作台，下载的工作副本仍在本地编辑。

媒体下载需要网络，上游地址或版本变化后固定校验可能拒绝安装，请联系维护者更新审核过的版本，不要删除校验。
可离线传入相同原始下载包：`powershell -NoProfile -ExecutionPolicy Bypass -File install-ffmpeg.ps1 -ArchivePath 完整ZIP路径 -Destination Engine`。
FFmpeg 是独立 GPL 软件，仅作为子进程使用，不因此把其许可证改为 MIT。公开包不包含其二进制，避免把未经完整合规核查的第三方集合直接再发布。
下载暂存目录保留以便排查，程序不会递归删除其他文件。正常使用完成后可自行清理 `.ffmpeg-download-*` 暂存副本，但保留 Engine。

## 从源码构建

需要 .NET 9 SDK，在仓库根运行 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-client.ps1`。
默认输出新的 `release-assets/client-build`，已存在时拒绝覆盖。`-WithoutMedia` 可仅编译程序，不下载媒体组件。
编译器输出不包含 .NET 运行时；使用者需 Desktop Runtime。请按微软支持周期维护运行时，后续应单独规划 .NET LTS 迁移。

完整操作见 [使用说明](../docs/使用说明.md)。字体不随项目自动安装；复杂字幕请先压制短预览。
