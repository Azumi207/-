# API v1

根地址不带路径。除了 `GET /api/health` 和 `POST /api/login` 外，所有接口要求 `Authorization: Bearer TOKEN`。JSON 使用 UTF-8；TOKEN 不能放在查询参数、URL 或日志中。成功一般返回 HTTP 200，错误 `{"detail":"原因"}`；401 登录失效、403 角色或领取权限不足、404 不存在或非项目成员、409 状态/基版本冲突、413 请求体过大、422 文件校验错误、429 登录限频、507 视频空间不足。

登录：`POST /api/login {username,password}` → `{token,expires,user:{id,username,admin}}`。`GET /api/me` 当前账号；`POST /api/logout` 撤销当前令牌。

管理员：`GET /api/users`、`POST /api/users {username,password}` 创建非管理员；`POST /api/projects {title,description}` 新建项目；`POST /api/projects/ID/members {revision,username,role}` 分配已有有效账号，role 为 source/translator/reviewer/encoder。未参与项目的非管理员不可见项目。

大厅：`GET /api/projects` 返回可见项目数组；`GET /api/projects/ID` 返回详情，含全部字幕版本元数据与最近 100 条交接。项目字段：id/title/description/stage/revision/owner_id/owner_name/my_role/media/latest_id/approved_id/submitter_id。所有状态修改后 revision 增加。

任务：`POST /api/projects/ID/claim {revision}`，允许对应角色从 translation_ready → translating 或 review_ready → reviewing；同一阶段一次只允许一个领取人。`POST /api/projects/ID/release {revision}` 由本人或管理员释放回对应待领取阶段。

片源上传：
1. `POST /api/projects/ID/uploads {name,size,sha256}`，size 为字节数，sha256 为小写十六进制。只有 source 或管理员在 source_pending 阶段可上传；返回上传 id/offset/size/expires 等。相同未过期上传可恢复。
2. `PUT /api/uploads/UPLOAD_ID`，原始字节体，每块 ≤4 MiB，请求头 `Upload-Offset: OFFSET`。只允许上传创建者；offset 必须恰好匹配已确认字节数。
3. `GET /api/uploads/UPLOAD_ID` 查询进度。`DELETE` 取消未完成上传。过期 410，需要重建。
4. `POST /api/uploads/UPLOAD_ID/finish` 服务端验证 SHA256/大小，成功固定片源并转到 translation_ready。重复 finish 幂等。已提交片源不可覆盖或用 DELETE 删除。
5. `GET /api/files/FILE_ID` 授权下载，支持 Range / If-Range。ETag 为带引号的 SHA256，另有 X-Content-SHA256；客户端仍应验证最终字节，不仅信任头。

字幕：`POST /api/projects/ID/versions {revision,base_version,kind,ass,note}`。base_version 必须等于当前 latest_id（第一版 null）；kind draft 保存不可变草稿但不交接，kind submit 仅供 translating 阶段交给 review_ready。只有当前领取人可提交。返回新项目状态。ASS 至少有 [Events] 和 Dialogue 行，服务端不执行 Lua/字幕脚本。每个项目递增 number，id 是全库唯一版本 ID。

`GET /api/projects/ID/versions/VERSION_ID` 下载某一不可变版本（UTF-8 ASS/SSA），X-Content-SHA256 可校验原始正文。版本所属项目也会检查。

校对：`POST /api/projects/ID/review {revision,version_id,action,note}`。action return 退回 translation_ready，必须有非空意见；approve 转 encoding_ready 并固定 approved_id。version_id 必须是当前最新版本；当前校对领取人不能是初翻提交者。客户端需先同步校对修改再决策。

没有自动重试写请求的承诺。超时后先读取项目/版本；revision 与基版本防止旧请求覆盖新结果，但客户端必须保留本地副本并处理“提交成功但回执丢失”。只支持单组、单进程实例，无集群租约和远程执行接口。

## 网页入口（服务端 0.2.0）

GET / 为公开登录外壳；GET /assets/{name} 仅允许 app.css、app.js、sha256.js、hash-worker.js、mark.svg。不公开目录或数据库。工作台仍使用上述 Bearer API 1；无 cookie 会话，无 URL 令牌，无匿名项目数据。客户端 0.2.0 不需要修改 API。

