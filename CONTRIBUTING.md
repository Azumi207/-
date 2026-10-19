# 贡献指南

欢迎提交 bug、改进建议和 PR。请附系统、组件版本、复现步骤和实际测试结果，勿上传真实视频、账号密码、数据库或私密字幕。
请先看 [开发说明](docs/开发说明.md)、[使用说明](docs/使用说明.md) 与 [安全说明](SECURITY.md)。
客户端目录为 windows-client，服务端共享目录为 server；平台入口不应复制分叉出第二份业务代码。
新增源码需更新 repository-files.json；Python/Shell 使用 UTF-8/LF，Windows CMD/PS1 注意兼容 Windows PowerShell 5.1。
保持 ASS 原文、样式/说话人、版本冲突保护与 API 1 兼容；涉及格式或数据库迁移要给出可恢复方案。
不要提交生成的包或测试数据。CI 通过也不能代替真实视频、显卡、Linux 和安全测试。
