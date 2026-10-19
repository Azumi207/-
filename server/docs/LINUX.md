# Linux 自托管指南 · 0.2.2

一个实例供一个字幕组使用；这是可自建的早期版本，不是现成的公共云平台。
网页负责项目与文件交接，打轴、翻译、样式编辑和压制仍在 Windows 客户端进行。

## 1. 推荐：Docker Compose

适用于能运行 Docker Engine 的 Linux x86_64 / ARM64 主机，不依赖宿主机的 Python 版本。
不是任意操作系统/CPU 都能运行：32 位系统、极老内核、受限容器面板不在目标范围。
请按 [Docker 官方安装说明](https://docs.docker.com/engine/install/) 安装 Engine 与 Compose v2 插件，并让当前账号有 Docker 使用权限。
Docker 权限等同很高的主机权限，只给可信任的管理员；本项目不自动安装 Docker、不自动提权。
Compose 需支持 `up --wait`；旧版不支持时升级插件。首次构建需要连接镜像仓库和 PyPI。

下载源码包，解压后进入同时含 `compose.yaml` 和 `install.sh` 的目录：

```sh
sh install.sh
```

此命令构建本机架构的镜像，交互创建管理员，等待健康检查通过后后台启动。
用户名默认 `admin`；密码输入两次，至少 12 字符，输入时不会显示。
无默认密码、无公开注册，重复安装不会覆盖已有管理员。初始化失败不会继续启动。
第一次以后用 `sh frameflow.sh start`，不必反复安装。Docker 开机启动后容器按重启策略恢复。

同一台电脑可打开 http://127.0.0.1:8765 。**这是服务器的本机地址，不是你电脑的地址，也不是给组员的网址。**
服务器远程初次体验，可在自己电脑上建立隧道（替换 USER、HOST）：

```sh
ssh -N -L 8765:127.0.0.1:8765 USER@HOST
```

保持 SSH 窗口开启，在自己电脑浏览器打开 http://127.0.0.1:8765 。端口冲突时把左边的 8765 改成空闲端口。
正式多人使用按下节配置 HTTPS。不能把账号密码通过公网明文 HTTP 传输。

### 可选配置

默认单视频 20 GiB、工作台视频配额 100 GiB。使用前评估磁盘、备份空间与带宽；这些配额不是整个磁盘容量管理器。
复制 `.env.example` 为 `.env`，编辑端口、配额，然后 `sh frameflow.sh start` 应用变更。
密码不放进 `.env`。端口仍只绑定 `127.0.0.1`。
镜像以非 root UID/GID 10001 运行、只读根文件系统，数据库/视频和备份在独立 named volumes。
默认项目名 `frameflow`，因此换源码目录不会换数据卷。多个字幕组必须从首次安装起分别设置固定的 `COMPOSE_PROJECT_NAME` 和端口；以后始终使用同一项目名。
不要运行 `docker compose down -v`，它会删除数据卷。不要将数据库放在 NFS/SMB 文件共享上。

## 2. 公网 HTTPS

推荐在同一台主机上由 Caddy/Nginx 反向代理到 `127.0.0.1:8765`，只让外界访问 HTTPS。
附带 `Caddyfile.example`：把 HOST 换成自己的域名，配置 DNS、证书所需的端口与 Caddy 服务。
不要把示例原样当成已配置好的证书。防火墙/域名/HTTPS 均需管理员按自己的环境配置，脚本不会开放端口。
[Caddy reverse_proxy 官方说明](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy)。

反向代理应保留 Authorization 请求头，允许至少 4 MiB 上传块（建议 8 MiB 以上）和 Range 下载，避免对大文件响应全量缓冲。
服务器拒绝公开的 X-Forwarded-* 信任；当前 IP 登录限速在反向代理下可能合并为一个地址，仍另有账号限速。
需要更大规模的限流、审计、MFA 时应先开展安全评估；不要配置宽松 CORS 或公开挂载 data 目录。
如果代理也运行于容器，代理里的 127.0.0.1 不是宿主机，请自行配置受控私有网络，而不是将 8765 暴露到公网。

## 3. 管理命令

在源码目录运行：

```sh
sh frameflow.sh status
sh frameflow.sh logs
sh frameflow.sh stop
sh frameflow.sh start
sh frameflow.sh restart
sh frameflow.sh reset-password admin
sh frameflow.sh backup
```

重置密码必须在服务器交互式终端输入两次，只影响选定账号的密码和旧会话，不删项目或字幕。
不要把真实密码粘贴到 issue、命令行参数、聊天或提交到 Git。
备份使用 SQLite 一致性快照，再复制已完成的不可变视频；清除快照里的会话和未完成上传，不改变原数据。
备份先写入独立 backups 卷，再复制到当前目录 `backups/`。看到 `BACKUP_COMPLETE.txt` 才是完整备份。
备份包含账号哈希、字幕与私有视频，需限制权限并定期复制到另一台受控机器；同机备份不是灾备。
备份卷与宿主机副本会占两份空间，需管理员制定保留策略，脚本不会自动删旧备份。

### 升级

保留原源码版本和完整备份，取得审核过的新版本到固定源码目录，不移动/替换 data 卷。
然后运行 `sh frameflow.sh upgrade`：先备份，再构建当前源码并更新容器，等待健康检查。
脚本**不会自行 git pull、不执行远程安装脚本**。构建失败保留原容器；启动失败查看日志，不宣称自动回滚成功。
当前 API 1 / schema 1，与 Windows 客户端 0.2.0 兼容。未来数据库版本升级需遵循该版本迁移说明，不能盲目降级。

### 恢复与 Windows 迁移

始终从服务端 `backup` 命令的完成快照恢复，不要仅复制正在运行的 `team.sqlite3`（可能遗漏 WAL 与视频）。
Windows 可先用 `FrameflowServer.exe --cli --data 数据目录 backup 新备份目录` 导出，再将整个完成快照传到服务器私有目录。
简单、安全的恢复验证方式是使用下节原生部署：把完成快照中的 `team.sqlite3` 与整个 `blobs/` 复制到**新的空数据目录**，设置只有运行账号可读写，指定该目录启动。
验证项目、成员、字幕和视频下载后再切换，原数据与备份保留不覆盖。恢复后所有人重新登录，未完成上传需重传。

Docker 恢复需要管理员将相同文件复制进**新的数据卷**，所有权设为容器 UID/GID 10001，权限目录 700/文件 600；使用独立项目名与端口验证后再切换代理。
不要在正在运行的卷里覆盖文件，不要先删除原卷。此版本没有“一键覆盖线上数据”的恢复按钮；不熟悉卷管理时优先用原生副本验证，并请运维协助切换。

## 4. 无 Docker：原生 Python

需要 Python 3.12 或 3.13、对应 venv/pip 支持、可写本地磁盘。其他 Python 版本未纳入本版 CI 矩阵。
发行版系统 Python 太旧时用 Docker 或自行安装合适版本；脚本不替换系统 Python、不改系统包。

```sh
sh install-native.sh
sh start.sh --serve-only
```

第一条创建 `.venv`、安装固定依赖、交互初始化；第二条前台运行，Ctrl+C 停止。
可通过 `PYTHON=/绝对路径/python3.12 sh install-native.sh` 选择解释器。
**不要从 Windows 复制 .venv 到 Linux**。缺少 venv 时按发行版说明安装匹配的 venv 包。
可先 `export FRAMEFLOW_TEAM_DATA=/绝对路径/私有数据目录`，所有安装、启动、备份、服务配置都使用同一目录。
不要在不同目录重复初始化后误以为旧数据消失。

```sh
.venv/bin/python server.py --data "$FRAMEFLOW_TEAM_DATA" reset-password admin
.venv/bin/python server.py --data "$FRAMEFLOW_TEAM_DATA" backup /绝对路径/新的备份目录
.venv/bin/python healthcheck.py
```

上面带环境变量的命令假定你已设置变量；默认数据位于源码 `data/`，也可直接填绝对路径。
`--serve-only` 在没有管理员时直接报错，不在后台等待输入密码，也不会创建缺失的数据目录。
`start.sh` 默认仅本机监听，不需要 Node.js、.NET 或桌面环境。

### 可选 systemd 用户服务

先完成原生初始化，保持源码和 `.venv` 的绝对位置不变，然后：

```sh
sh linux/install-user-service.sh
systemctl --user status frameflow.service
journalctl --user -u frameflow.service -f
```

只写入当前用户的 `~/.config/systemd/user/frameflow.service`，不安装 root 服务；已有 unit 默认拒绝覆盖。
变更配置前停服务，审核后用 `--replace` 重新生成，再 `systemctl --user restart frameflow.service`。
可传 `--port 8766`，随后用 `.venv/bin/python healthcheck.py --port 8766` 确认实际可访问；systemctl 显示 active 不等于 HTTP 已就绪。
注销后和开机未登录时也运行，需要管理员明确执行 `sudo loginctl enable-linger "$USER"`；脚本不会替你执行。
没有 systemd 用户会话的容器/发行版请用 Docker 或自己的进程管理器。单实例、单 worker，不要同时启动第二份服务指向同一数据目录。

## 5. 已验证与尚未验证

本次制作环境是 Windows：可执行 Python/真实本机 HTTP 回归、Git Bash 脚本语法与模拟命令分派测试、源码打包检查。
这里没有 Linux 内核/Docker daemon，**没有声称已在 Linux、ARM64、systemd 或真实容器实机通过**。
GitHub Actions 配置了 Ubuntu 24.04 x86_64/ARM64、Python 3.12/3.13 和 Docker 构建/健康/重启持久化测试；上传仓库并实际运行绿色后，才可引用该平台结果。
镜像基础标签跟进 Python 3.12 补丁，不是 digest 固定镜像；固定 Python 依赖版本不代表离线安装或所有构建逐字节一致。
正式开放给不信任用户前需要安全审查、容量评估、备份恢复演练与真实网络测试。
