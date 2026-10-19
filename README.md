# 帧语 · Frameflow

**把注意力留给字幕，把繁琐交接留给工具。**

以 Aegisub 的使用习惯为参考的独立 Windows 字幕编辑器，以及可自行部署的字幕组网页工作台。支持 ASS/SSA，保留说话人和字幕样式；不是 Aegisub 官方项目。

本仓库包含 **Windows 客户端、Linux 服务端入口、Windows 服务端入口**，服务端共用一份源码，避免两套功能不同步。

| 你想做什么 | 去哪里 |
|---|---|
| 下载可运行的安装包 | [Releases](https://github.com/Azumi207/-/releases) |
| Windows 打轴、翻译、校对、压制 | [Windows 客户端](windows-client/README.md) |
| Linux 部署字幕组工作台 | [Linux 服务端](linux-server/README.md) |
| Windows 部署字幕组工作台 | [Windows 服务端](windows-server/README.md) |
| 从头了解日常操作 | [使用说明](docs/使用说明.md) |
| 编译、测试和贡献 | [开发说明](docs/开发说明.md) |

## ASS 精确预览 · 本地测试版

新增真实 libass 预览、缺失字体/替代记录、实际 H.264 成片对照。界面减少重复按钮，保留深浅色和原快捷键。
详见 [精确预览说明](docs/精确预览使用说明.txt)。此版尚未上传 Releases；完整预览最高 720p / 30 fps 显示，不承诺与有损成片逐像素相同。

## 字幕制作

- 视频与波形联动、仅音频播放、悬停十字坐标、时间轴与翻译编辑。
- Aegisub 风格快捷键、字幕/时间/视频/音频/视图/自动化菜单。
- 说话人、样式、可视化位置调整、疑似闪轴检测和校对问题列表。
- 保留 ASS 高级标签；无法安全处理的冲突不静默覆盖。
- 本地硬字幕压制，检测可用 NVIDIA / Intel / AMD 编码器，失败可回退 CPU。

## 团队工作流

**片源上传 → 初翻领取 → 下载打轴/翻译 → 提交版本 → 校对退回或通过 → 下载给压制。**

网页支持登录、项目大厅、成员与角色、领取任务、分块上传/续传、版本记录与校对意见。保留原始 ASS 样式与说话人，不覆盖旧版本。
网页暂不代替桌面字幕编辑器；服务器也不会自动压制和发布。一个部署实例对应一个字幕组，不是公共多租户平台。

## 快速开始

### 普通 Windows 用户

下载名称带 **Portable-win-x64** 的客户端 ZIP，右键“全部解压”，打开文件夹双击 **启动帧语.cmd**。已内置 .NET Desktop Runtime，**不需要安装 .NET，不需要管理员权限**。不要直接在压缩包里运行或只复制 EXE。
公开客户端包不捆绑 FFmpeg；首次启动会从上游下载经过固定 SHA256 校验的独立媒体组件，后续无需重复下载。详见客户端说明。
Windows 服务端 ZIP 则双击 `启动工作台.cmd`，无需自行安装 Python。

### Linux 管理员

已安装 Docker Engine 与 Compose v2 时，在仓库根目录运行：

```sh
sh linux-server/install.sh
```

设置管理员密码并确认后后台启动。默认仅服务器本机 `http://127.0.0.1:8765` 可访问；多人远程使用必须先配置 HTTPS。
无 Docker 可用 Python 3.12/3.13：`sh server/install-native.sh`，再 `sh server/start.sh --serve-only`。

## 目录

```text
windows-client/      Windows WPF 客户端源码
linux-server/        Linux 一键安装、启动、管理入口
windows-server/      Windows 服务端启动、打包入口
server/              两个平台共用的网页/API/存储源码
docs/                中文使用说明、开发说明、发布说明
scripts/             编译、媒体组件安装、源码打包、安全检查
tools/               媒体组件来源与校验信息（不含其二进制）
.github/workflows/   服务端及 Windows 客户端 CI
release-assets/     本地发行包，Git 忽略，应单独放 Releases
```

客户端版本 **0.2.0**，服务端版本 **0.2.2**，服务端 API 1 / 数据库 schema 1。版本不同不代表不兼容。
旧项目和安装包不会因整理仓库被移动、覆盖或删除。本仓库不含任何真实用户的数据库、视频、字幕、密码或令牌。

## 状态与边界

本项目是早期版本，正式使用前应先备份并用副本验证。当前 Windows 环境可做客户端构建/核心测试、服务端真实本机 HTTP 测试和发行包验证。
Linux/Docker/ARM64/systemd 的实际运行结果须以 GitHub Actions 或目标主机执行为准，不能将配置文件当成已经实测成功。
客户端预览和 libass 最终压制不保证逐像素相同；跨电脑使用相同字体，先预览再正式输出。
客户端目前使用 .NET 9；计划另行迁移到受维护的 LTS 版本，不在此次目录整理中冒进修改运行时。

## 开源与安全

原创代码使用 [MIT](LICENSE)。第三方组件各有许可证，尤其 FFmpeg 不是 MIT；见 [THIRD_PARTY.md](THIRD_PARTY.md)。
[安全说明](SECURITY.md) · [贡献指南](CONTRIBUTING.md) · [更新记录](CHANGELOG.md)。
