# 帧语工作台 · Frameflow Team

**让每一句字幕，顺利抵达观众。**

为字幕组提供独立自托管的中文网页工作流：

**登录 → 项目大厅 → 领取任务 → 下载编辑 → 提交版本 → 校对退回 / 通过。**

服务端 0.2.2 · MIT · API 1 · 兼容帧语 Windows 客户端 0.2.0。

## 一条命令启动 Linux 服务端

先安装 [Docker Engine 与 Compose v2 插件](https://docs.docker.com/engine/install/)，下载并解压本仓库，进入包含 compose.yaml 的目录：

```sh
sh install.sh
```

按提示设置管理员密码并再次确认。安装完成后后台运行，无默认账号密码，不会自动开放公网。
首次需要联网下载镜像和固定版本依赖；后续 `sh frameflow.sh start` 即可。

- 本机访问：<http://127.0.0.1:8765>。
- 远程服务器：先用 SSH 隧道体验，给组员使用前部署 HTTPS。
- 不想用 Docker：Python 3.12/3.13 + `sh install-native.sh`，再 `sh start.sh --serve-only`。
- 详细步骤、备份、升级、Windows 数据迁移和 systemd：[Linux 部署指南](docs/LINUX.md)。

> 通用部署不等于零依赖，也不等于所有平台已经实测。目标是 Linux x86_64 / ARM64；制作环境为 Windows，Linux/Docker/systemd 的真实运行仍需执行随包 CI 或目标主机验收。没有预先构建/发布的容器镜像。

## 已有功能

- 项目大厅、搜索/筛选、我的任务、校对队列与交接历史。
- 管理员创建成员、分配片源 / 初翻 / 校对 / 压制角色。
- 视频分块上传、续传、SHA256 校验和鉴权下载。
- ASS/SSA 原文不可覆盖版本，保留样式、说话人和覆盖标签。
- 任务领取与释放、提交初翻、校对意见、退回或绑定具体版本通过。
- 冲突时拒绝覆盖；需要下载新副本并人工合并。
- 密码确认、限速、私有存储、备份、健康检查；账号由管理员创建，不开放公共注册。

网页是工作流入口，**不是完整在线字幕编辑器**。打轴与翻译在帧语 Windows 客户端或其他 ASS 编辑器完成；服务器不会自动压制或发布。
客户端的显卡压制功能是独立功能，不要求服务器有显卡。字体/素材/逐行校对 sidecar 暂不随字幕上传，跨机器排版仍需相同字体。
每个部署实例对应一个字幕组，不是公共多租户 SaaS。

## 运维

```sh
sh frameflow.sh status
sh frameflow.sh logs
sh frameflow.sh reset-password admin
sh frameflow.sh backup
sh frameflow.sh stop
sh frameflow.sh start
```

数据保存在独立卷中；停止不会删除数据。禁止随意执行 `docker compose down -v`。
源码包不含用户账户、数据库、视频、密钥或 Windows 二进制。不要把自己运行后的 data、backups、.env 提交到 GitHub。
旧 Windows 使用说明保留在 [docs/WINDOWS.md](docs/WINDOWS.md)，API 见 [API.md](API.md)。

## 开发、验证与开源

```sh
python -m pip install -r requirements-test.txt
python -m pytest -q
python ci/package.py
```

容器实测：`python ci/docker_smoke.py`，需要 Docker；仅使用随机临时项目。
仓库包含 Ubuntu x86_64/ARM64 的 Python 和 Docker CI。**配置了测试不代表它已运行成功**，请查看自己仓库的 Actions 结果。
生成器产出可上传 GitHub / Releases 的白名单源码 ZIP 和 Linux tar.gz，脚本保持 LF 与执行权限，包含逐文件 SHA256SUMS。

[贡献与发布指南](CONTRIBUTING.md) · [安全说明](SECURITY.md) · [更新记录](CHANGELOG.md) · [第三方依赖](THIRD_PARTY.md)

本服务端原创代码采用 [MIT](LICENSE)。第三方库保留各自许可证；字幕、视频、字体等内容授权不受此协议替代。本版仍需公开部署前安全审查和负载测试。
