# 第三方组件与服务

浮词自己的代码与原创小精灵图标采用 [MIT License](LICENSE)。第三方组件分别遵循其自身条款，根目录的 MIT 许可不替代这些条款。

## Windows 发布包

| 组件 | 用途 | 原始许可与通知 |
| --- | --- | --- |
| [.NET Runtime](https://github.com/dotnet/runtime) | 自包含程序运行时 | [LICENSE.TXT](licenses/net-runtime/LICENSE.TXT)、[THIRD-PARTY-NOTICES.TXT](licenses/net-runtime/THIRD-PARTY-NOTICES.TXT) |
| [Windows Desktop Runtime](https://github.com/dotnet/wpf) | WPF 界面与 Windows Forms 托盘 | [LICENSE](licenses/windows-desktop/LICENSE) |
| [Microsoft WebView2 SDK](https://www.nuget.org/packages/Microsoft.Web.WebView2) | 连接官方学习页面 | [LICENSE.txt](licenses/webview2/LICENSE.txt)、[NOTICE.txt](licenses/webview2/NOTICE.txt) |

仓库中的原文来自 0.3.3 验证时使用的 NuGet 包：.NET / Windows Desktop Runtime 10.0.12、WebView2 SDK 1.0.4191.47。`scripts/publish.ps1` 会按实际发布依赖从 NuGet 缓存复制对应原文，并在安装包的 `licenses/versions.json` 记录版本。更新依赖时也应更新仓库中的原文副本。

WebView2 Evergreen 浏览器运行环境使用系统安装版本，未打包在浮词 ZIP 内。其安装和使用适用 [Microsoft WebView2 分发页面](https://developer.microsoft.com/microsoft-edge/webview2/) 的条款。

## 开发依赖

[jsdom](https://github.com/jsdom/jsdom) 及其依赖仅用于合成 DOM 测试，不随 Windows 应用分发；版本由 `tests/adapter/package-lock.json` 锁定，各依赖的许可保留在安装的 npm 包中。GitHub Actions 所引用的官方构建工具使用各自仓库提供的许可。

## 墨墨服务

墨墨背单词的名称、账号服务、网站和学习内容归相应权利人所有。本项目是独立客户端，不代表官方；不在仓库或安装包中分发墨墨词库、网站脚本或下载的 API 规范。官方内容在使用时由墨墨服务提供，账号使用仍适用其 [用户协议](https://www.maimemo.com/terms) 和 [隐私政策](https://www.maimemo.com/privcay)。
