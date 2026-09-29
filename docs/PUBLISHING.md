# 整理仓库与发布版本

## 上传源码

源码仓库应保留 `src/`、`tests/`、`scripts/`、`docs/` 中的公共说明与示例图片、`licenses/`、`.github/` 及根目录配置和文档。

`.gitignore` 排除了本地 SDK / npm 缓存、编译目录、`dist/`、`artifacts/`、`.local/`、浏览器 profile 和诊断日志。旧的个人调试记录与下载的参考文件可以保存在 `.local/notes/`，不要将其移动回公开文档目录。

首次建立 Git 仓库，可以在根目录执行：

```powershell
git init -b main
git add .
git status --short
git diff --cached --stat
```

检查暂存文件后再提交，并连接你自己创建的 GitHub 仓库。账号、Cookie、Token、真实用户截图、临时 profile 和完整诊断日志不应出现在提交中。`.gitignore` 不会删除已经跟踪的文件；若它们以前提交过，需要单独处理。

使用 GitHub 网页上传时，网页不会替你应用 `.gitignore`。请使用整理后的源码文件或干净源码包，避免直接拖入整个开发目录。不要把 Windows 运行包当作源码仓库上传。

## 发布 Windows 运行包

1. 更新项目版本、设置窗口版本文字、README 版本和发布脚本的默认输出名称。
2. 按 CONTRIBUTING 运行适当的检查；图标变化后重新生成 `app.ico`，并更新示例预览图。
3. 运行 `./scripts/publish.ps1`。脚本生成自包含的 Windows x64 目录和 ZIP，并收集实际依赖的许可证。输出目录必须为空；重复发布时使用新的 `-OutputName`，或先自行归档旧的生成目录。
4. 核对 ZIP 的程序版本、README、LICENSE、第三方通知和 `licenses/`。完整 ZIP 作为 GitHub **Release 附件**上传，不提交到 Git 源码历史。

每次发布请说明修复与已验证范围。源码里的构建工作流只运行本地样本和离屏检查；真实登录、发音、同步和桌面行为仍需要有权使用的账号与 Windows 环境验证。
