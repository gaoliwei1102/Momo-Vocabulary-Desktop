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

## 发布 Windows 安装包和便携版

1. 更新项目版本、设置窗口版本文字、README 版本和发布脚本的默认输出名称。
2. 按 CONTRIBUTING 运行适当的检查；图标变化后重新生成 `app.ico`，并更新示例预览图。
3. 运行 `./scripts/publish.ps1`。脚本生成自包含的 Windows x64 目录和 ZIP，并收集实际依赖的许可证。输出目录必须为空；重复发布时使用新的 `-OutputName`，或先自行归档旧的生成目录。
4. 安装 [Inno Setup](https://jrsoftware.org/isinfo.php)，运行下面的命令制作 EXE 安装包。`-IsccPath` 指向本机的 Inno Setup 编译器；可用 `-WebView2BootstrapperPath` 指定预先下载的微软 WebView2 Evergreen Bootstrapper，省略时由脚本下载。

   ```powershell
   .\scripts\build-installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

   # 已准备好微软引导程序时
   .\scripts\build-installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -WebView2BootstrapperPath 'C:\Downloads\MicrosoftEdgeWebview2Setup.exe'
   ```

5. 核对程序版本、README、LICENSE、第三方通知和 `licenses/`，确认发布目录只包含程序及公开文档。当前输出为 `dist/WordBubble-0.3.6-Setup-x64.exe` 和 `dist/WordBubble-0.3.6-win-x64.zip`。
6. 在 Windows 测试安装、启动、完全退出、覆盖更新和卸载。检查当前用户安装目录及桌面快捷方式；WebView2 缺失时应由微软引导程序联网安装，已安装时应直接继续。更新和卸载应保留 `%LOCALAPPDATA%\WordBubble\` 下的登录会话和设置。使用隔离测试账号或目录，避免误操作自己的学习数据。
7. 提交并推送对应源码，创建指向该提交的版本标签（当前为 `v0.3.6`）。在 GitHub Releases 创建该标签的版本，将 EXE 安装包和完整便携 ZIP 作为 **Release 附件**上传，不提交到 Git 源码历史。
8. 发布说明优先链接 `WordBubble-0.3.6-Setup-x64.exe`，注明 Windows 10 / 11 x64、需要联网和自己的墨墨账号，并给出便携 ZIP 作为替代。源码下载项不供直接运行。发布后检查附件可以下载，README 的版本链接与实际附件名一致。

安装器会将程序安装到当前 Windows 用户目录并创建桌面快捷方式；程序自带 .NET 运行时，无需让用户安装开发 SDK。缺少 WebView2 Runtime 时仍需联网下载运行时。安装包构建脚本不会进行代码签名，Windows 可能显示未知发布者或 SmartScreen 提示；若另外签名，发布前核验最终上传文件的签名，并如实说明。

用户更新时先从托盘退出，再运行新安装包并安装到原位置；Windows“设置 → 应用”提供卸载入口。便携版用户完整解压后启动，卸载时退出并删除程序目录。两种方式均保留独立的用户数据目录，只有用户主动清除该目录才会删除保存的会话和偏好。

每次发布请说明修复与已验证范围。源码里的构建工作流只运行本地样本和离屏检查；真实登录、发音、同步和桌面行为仍需要有权使用的账号与 Windows 环境验证。
