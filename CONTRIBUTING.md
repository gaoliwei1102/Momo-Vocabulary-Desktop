# 参与浮词开发

感谢你帮助改进浮词。请优先提交一个范围清晰、可以验证的修改；界面建议也欢迎通过 Issue 描述具体场景。

## 准备开发环境

在 Windows 上安装 .NET 10 SDK、Microsoft Edge WebView2 Runtime，以及 Node.js 22 或更新的受支持版本。构建、测试和打包命令见 [README](README.md#从源码开发)。依赖应通过 NuGet 和 `npm ci` 安装，不要提交下载后的依赖目录。

## 修改约定

- 原生界面保持轻巧，学习操作由用户主动触发，避免自动弹出、自动答题或自动接受官方协议。
- 官网是学习状态的来源。不要按按钮点击次数伪造完成数，不要建立未经确认的本地正式学习记录。
- 页面结构不明确、请求结果不确定时，停止并给出可恢复提示，避免重复反馈或重放学习请求。
- 账号密码只能短暂用于官方登录流程。不要增加凭据日志、Cookie 导出、硬编码账号或 Token。
- 修改 JavaScript 适配器时，为影响提交行为、页面识别和重复操作防护的变更补充有意义的 DOM 样本测试。
- 图标与界面资源应可追溯；引入第三方资源时保留许可，并更新相应说明。

## 验证改动

在仓库根目录执行：

```powershell
dotnet build .\src\WordBubble\WordBubble.csproj -c Release
dotnet run --project .\tests\WordBubble.Tests\WordBubble.Tests.csproj
Push-Location .\tests\adapter
npm ci
npm test
Pop-Location
```

修改界面后，还应运行离屏预览，检查文字是否截断、窗口大小与不同页面状态是否合适：

```powershell
dotnet run --project .\scripts\PreviewRenderer\PreviewRenderer.csproj -c Release -- artifacts/native-preview
```

预览与自动测试使用样本数据，不需要真实账号。真实登录、同步或反馈测试只使用你本人有权操作的账号，并明确区分哪些操作已经提交到墨墨。不要把真实会话带入测试样本或 CI。

小精灵分别使用 `SpriteControl.xaml`（带表情的气泡）与 `Assets/mascot-icon.xaml`（小尺寸图标）作为可编辑矢量源。修改托盘图标后，在 Windows 的 STA PowerShell 会话中执行 `./scripts/create-icon.ps1`，再重新构建；该脚本生成 16–256 像素的 9 档 `app.ico`，并在 `artifacts/icon-preview/` 输出检查用 PNG。提交矢量源和 `app.ico`，检查图片留在本地。

GitHub Actions 使用 Windows、.NET 10 和 Node.js 24，执行构建、核心逻辑、DOM 样本与原生离屏流程检查；不会连接墨墨账号或自动发布版本。

需要连接诊断时，可以在退出已运行的浮词后执行：

```powershell
dotnet run --project .\src\WordBubble\WordBubble.csproj -- --diagnostics artifacts/connection.log --test-profile artifacts/test-profile
```

`--diagnostics` 默认关闭；启用后会记录连接阶段、错误码和有限的页面结构信息，并让学习窗口显示在任务栏，便于调试。`--test-profile` 只有与诊断参数一起使用时才生效，用于隔离浏览器登录会话；偏好设置仍使用通常的数据目录。请把诊断日志和临时会话保留在忽略的本地目录中，不要公开上传原始文件。

## 提交 Pull Request

提交前检查 `git status` 和 `git diff --cached`，确认没有构建产物、个人路径、账号信息、日志、浏览器会话或真实账号截图。新增文档图片请使用离屏预览或明确的示例数据。

PR 描述建议包含：

- 解决的问题，以及什么操作会触发它。
- 修改后的行为；界面改动可附示例数据截图。
- 运行过的检查与结果，以及仍未验证的范围。

如需引入较大的功能或依赖，建议先开 Issue 讨论使用场景。贡献代码采用本项目的 [MIT License](LICENSE)；不要提交你无权再分发的代码、内容或资源。
