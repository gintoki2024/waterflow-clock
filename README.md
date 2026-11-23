# 水流时钟 · Waterflow Clock

一个可拖动的桌面时钟，提供手写数字、系统字体、四种外观、滚动动效、12/24 小时制和登录启动。

## 下载与使用

在 [Releases](https://github.com/gintoki2024/waterflow-clock/releases) 选择对应平台的压缩包。

- **Windows**　解压后双击 `水流时钟.exe`。右键时钟或使用系统托盘菜单调整设置。
- **macOS 13 或更高版本**　解压后将 `水流时钟.app` 拖入“应用程序”并打开。通用程序支持 Apple Silicon 和 Intel。右键时钟或点击菜单栏时钟图标选择外观、字体、置顶或登录启动。

Mac 的纯净悬浮主题可根据下方窗口的明暗自动调整字色。在菜单中选择“允许读取窗口颜色…”，按 macOS 提示授权屏幕录制。取色在本机内存中完成。未授权时按系统外观选色。玻璃主题按卡片的明暗配色。

Mac 测试包使用临时签名，未经过 Apple Developer ID 公证。若 macOS 限制打开，可在核实下载来源后使用系统设置中的“隐私与安全性”查看本次打开提示。

## 从源码构建

在 Windows 的项目目录用 PowerShell 运行：

```powershell
& "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:水流时钟.exe '*.cs'
```

在已安装 Xcode 命令行工具的 Mac 上运行：

```sh
bash macOS/build.sh "$PWD/dist"
```

GitHub Actions 分别检查 Windows、Intel Mac 和 Apple Silicon Mac 的构建与运行。屏幕读取的授权体验和登录后自动启动需要在交互式桌面验证。
