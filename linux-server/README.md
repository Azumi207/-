# Linux 服务端

目标：Linux x86_64 / ARM64 自托管。共享实现位于 [server](../server)，不会维护两套不同的 API。

已安装 Docker Engine + Compose v2 后，从仓库根执行：

```sh
sh linux-server/install.sh
sh linux-server/manage.sh status
sh linux-server/manage.sh logs
sh linux-server/manage.sh backup
sh linux-server/start.sh
```

首次安装会交互设置并确认管理员密码，无默认密码。默认只监听服务器本机，给其他人访问前配置 HTTPS。
管理入口还支持 stop/restart/upgrade/reset-password。升级先备份；不会自动 git pull、开放防火墙或删除数据卷。

也可下载单独 Linux tar.gz，解压后直接 `sh install.sh`；这是独立包，目录结构与完整仓库不同。
Python 原生安装、systemd、SSH 隧道、HTTPS、备份和恢复见 [完整 Linux 指南](../server/docs/LINUX.md)。
制作环境没有 Linux/Docker，实机兼容性仍需目标主机或 CI 验证；不是“任意机器无依赖可运行”。
