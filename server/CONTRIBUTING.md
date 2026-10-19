# 参与开发

感谢帮助字幕组把交接做得更简单。请先阅读 README、API.md 与 SECURITY.md。

## 开发与测试

使用 Python 3.12/3.13 创建独立虚拟环境，然后在仓库根目录运行：

```sh
python -m pip install -r requirements-test.txt
python -m pytest -q
python ci/package.py
```

完整容器验收另运行 `python ci/docker_smoke.py`，需要可用 Docker Engine 与 Compose。
它只操作随机命名的 `frameflow-ci-*` 临时项目/数据卷，结束时清理自己创建的资源，不要给它生产数据库。
CI 同样执行这些命令，但上传本仓库之前并没有已经成功的远程 CI 运行记录。

提交 PR 时提供问题、改动、实际测试命令与结果。截图去掉账号、域名、字幕和素材等私密信息。
新增字段应保留 API 1 客户端兼容性；数据库变更需要明确迁移/回退设计，不可删库重建。
上传/下载/版本提交必须保留逐请求权限校验与修订号冲突保护。不要添加默认密码或绕过身份校验的调试入口。
POSIX 脚本必须 LF，避免只在开发者机器上有效的绝对路径。第三方依赖更新应同步固定版本、许可证清单和测试。

## 发布到 GitHub

将源码包中 `frameflow-team-0.2.2/` 的**内容**放在仓库根目录（包括 `.github`、`.gitignore` 等隐藏文件），不要把整个工作区或 dist 复制进去。
仅本服务端目录采用附带 MIT 协议；源视频、字幕、字体、依赖和编辑器的授权不由此改变。
仓库设置中启用 Actions，并建议启用私密漏洞报告。首次 Actions 全部通过后再发布稳定标签与 Release。
`python ci/package.py` 生成无用户数据的 ZIP/tar.gz，含逐文件 SHA256SUMS；同名产物存在会拒绝覆盖。
生成器采用显式文件白名单，不打包 data、backups、.env、.venv、token、数据库、Windows EXE 或测试输出。
新增公开源码文件时同时更新 `ci/package.py` 白名单。版本号取 `server.py` 的 VERSION。
工作流仅有 contents:read，不上传镜像、不自动部署或推送 release。Actions 大版本标签和基础镜像标签需维护者定期审核，正式发布可进一步固定审核过的 commit/digest。
