# 第三方组件

本项目原创代码为 MIT，以下组件保留各自授权，不能统一改标 MIT。

- **MoonSharp 2.0.0**：使用 NuGet 下载；原许可全文位于 windows-client/Licenses/MoonSharp-LICENSE.txt，保留版权和再分发条款。
- **FFmpeg / ffprobe**：作为独立媒体进程使用；当前已验证的上游 Gyan 9.0.2 构建声明 GPLv3。公开客户端包不再附带其二进制，首次由使用者从上游下载，保留 LICENSE/README 与来源校验记录。
  - [FFmpeg 许可说明](https://ffmpeg.org/legal.html)
  - [上游 Windows 构建](https://www.gyan.dev/ffmpeg/builds/)
  - 固定 ZIP/文件校验值：tools/ffmpeg-provenance.json。上游更新后可能导致校验失败，必须由维护者审核后更新，不能忽略失败。
  - 如果以后重新分发带 FFmpeg 的整包，需先审查对应构建及依赖的完整源码、再分发和通知义务，不能仅有 MIT 声明。
- **.NET Desktop Runtime / SDK**：Windows 便携版内置 .NET 10.0.13 的 Microsoft.NETCore.App / Microsoft.WindowsDesktop.App 运行时；使用者不需要安装 .NET。运行时包实际附带的 LICENSE / LICENSE.TXT / THIRD-PARTY-NOTICES.TXT（另附 WinForms 通知及来源） 位于发行包 Licenses 子目录，仍保留原授权。自包含版本需由维护者重新发布才能更新内置运行时。
- **Python/FastAPI/Argon2/SQLite 等**：见 server/THIRD_PARTY.md 和固定依赖列表；独立 Windows 服务端额外收集 Python 与运行依赖许可文件。

本说明不是法律意见；视频、字幕、字体和素材的权利仍由其权利人持有，不能因为用了本工具而自动获得授权。

## 实时定位渲染器

拖动路径动态加载 libass 0.17.5（ISC），来自 MSYS2 官方 mingw64 软件包；不替换最终压制所用的独立 FFmpeg。对应包 URL、版本、SHA256、上游项目和源码包 URL 列在 `tools/native-ass-provenance.json`。构建脚本仅提取清单指定的 20 个 x64 DLL 和原始许可文件，逐一验证哈希；不安装 MSYS2、不修改 PATH。

依赖保留各自许可证（包含 FreeType、HarfBuzz、FriBidi、GLib、libiconv、gettext，以及 GCC runtime exception），许可全文保存在便携包 `NativeAss/Licenses` 和 `NativeAss/libass-LICENSE.txt`。这些库未被修改，采用独立 DLL 动态链接；可按清单和 `scripts/install-native-ass.ps1` 重建/替换，不能删去通知或禁止为修改库进行调试。项目 MIT 不覆盖这些组件。重新对外分发前，须按清单审查完整对应源码和各组件再分发义务；本地验证不等于完成开源合规审计。
