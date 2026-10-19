# 第三方组件

本项目原创代码为 MIT，以下组件保留各自授权，不能统一改标 MIT。

- **MoonSharp 2.0.0**：使用 NuGet 下载；原许可全文位于 windows-client/Licenses/MoonSharp-LICENSE.txt，保留版权和再分发条款。
- **FFmpeg / ffprobe**：作为独立媒体进程使用；当前已验证的上游 Gyan 9.0.2 构建声明 GPLv3。公开客户端包不再附带其二进制，首次由使用者从上游下载，保留 LICENSE/README 与来源校验记录。
  - [FFmpeg 许可说明](https://ffmpeg.org/legal.html)
  - [上游 Windows 构建](https://www.gyan.dev/ffmpeg/builds/)
  - 固定 ZIP/文件校验值：tools/ffmpeg-provenance.json。上游更新后可能导致校验失败，必须由维护者审核后更新，不能忽略失败。
  - 如果以后重新分发带 FFmpeg 的整包，需先审查对应构建及依赖的完整源码、再分发和通知义务，不能仅有 MIT 声明。
- **.NET Desktop Runtime / SDK**：Windows 便携版内置 .NET 9.0.21 的 Microsoft.NETCore.App / Microsoft.WindowsDesktop.App 运行时；使用者不需要安装 .NET。运行时包实际附带的 LICENSE / LICENSE.TXT / THIRD-PARTY-NOTICES.TXT（另附 WinForms 通知及来源） 位于发行包 Licenses 子目录，仍保留原授权。自包含版本需由维护者重新发布才能更新内置运行时。
- **Python/FastAPI/Argon2/SQLite 等**：见 server/THIRD_PARTY.md 和固定依赖列表；独立 Windows 服务端额外收集 Python 与运行依赖许可文件。

本说明不是法律意见；视频、字幕、字体和素材的权利仍由其权利人持有，不能因为用了本工具而自动获得授权。
