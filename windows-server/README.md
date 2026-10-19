# Windows 服务端

用于给字幕组提供网页工作台，不是打轴客户端。当前服务端 0.2.2，共享源码见 [server](../server)。

## 推荐：下载独立包

从 Releases 下载 `Frameflow-Team-Web-Windows-0.2.2.zip`，完整解压，双击 `启动工作台.cmd`。
无需 Python/Node.js。首次创建管理员并输入两遍密码，启动后浏览器打开本机工作台。保持进程运行。
忘记密码由服务器管理员双击 `重置密码.cmd`，核对数据位置后本机重置；不删项目。

## 运行源码 / 自行打包

- 已安装 Python 3.12/3.13 时，双击本目录 `start.cmd`。首次安装依赖到共享 server/.venv；数据默认 server/data。
- 双击 `build.cmd`，把共享源码生成新 Windows 独立包。需要 Python 与网络，不覆盖已有输出目录。
- 可传 PowerShell 参数 `-Python 完整解释器路径 -Output 新目录`。程序退出不等于删数据。

默认只监听 127.0.0.1:8765；远程协作需 HTTPS 反向代理，不能把本机地址发给组员。
跨版本数据迁移必须先备份，再显式指定原 data 目录，不要复制覆盖正在使用的数据库。
项目大厅操作见 [使用说明](../docs/使用说明.md)，API 见 [API.md](../server/API.md)。
