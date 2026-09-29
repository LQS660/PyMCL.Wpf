# PyMCL.Wpf

[PyMCL](https://github.com/LQS660/PyMCL) 启动器的 **WPF 前端**（net48）。

界面与 Qt 版（主仓 `app/`）同构：同一套侧栏编排、同一份 `config.json`、同一批
`L("中文")` 词表（`mclauncher/locales/*.json`），两边切语言一起变。功能通过桥
（C 桥 `pymcl-bridge.exe` 或 Python 桥 `bridge/server.py`）的 HTTP JSON-RPC +
SSE 事件流完成，本仓只做界面，不含后端。

## 与主仓的关系

WPF 前端原先住在主仓的 `wpf/` 下，2026-09-29 拆成独立仓（历史用 `git subtree split`
抽取，树哈希与迁出前一致）。**两个仓需要并排克隆**——界面在运行时靠向上查找定位
启动器根目录（`mclauncher/`、`bridge/server.py`、`native/build/pymcl-bridge.exe`）：

```
PyMCL-main/     ← 主仓：后端、桥、Qt 前端
PyMCL.Wpf/      ← 本仓：WPF 前端
```

主仓那边的跨端一致性测试（i18n 词表 / 反馈页文案 / 侧栏键表）会自动找到同级目录的
本仓；也可以用 `PYMCL_WPF_SRC` 显式指定源码位置。

## 构建

需要 .NET SDK（`net48` 目标框架，Windows）。

```bat
dotnet build PyMCL.Wpf/PyMCL.Wpf.csproj -c Release
```

打包成单文件（在主仓执行，本仓的 publish 产物是它的输入）：

```bat
dotnet publish PyMCL.Wpf/PyMCL.Wpf.csproj -c Release -f net48 -o PyMCL.Wpf/bin/Release/net48/publish
python _pack_net48.py
```

运行时只依赖系统自带的 .NET Framework 4.8；唯一外部依赖 `System.Text.Json`
（8.0.5）随包分发。

## 自检开关

编出来的 exe 带几个无界面自检入口，主仓的测试与冒烟脚本会调它们：

| 开关 | 作用 |
|---|---|
| `--nav-dump` | 打印侧栏编排结果，供与 Qt / 网页版逐条对拍 |
| `--i18n-check` | 扫源码与词表，报告没包 `L()` 的中文字面量 |
| `--consent-check` | 跑反馈同意提示的状态机真值表 |
| `--ime-check` | 输入法守卫自检 |
| `--smoke` | 冒烟模式（跳过等人点的弹窗） |
| `--lang xx` | 指定语言，优先级高于环境变量与 `config.json` |

环境变量：`PYMCL_HOME`（数据目录）、`PYMCL_BRIDGE` / `PYMCL_BRIDGE_EXE`（桥位置）、
`PYMCL_PYTHON`、`PYMCL_LANG`、`PYMCL_WPF_DEBUG`。

## 目录

```
PyMCL.Wpf/
├── App.xaml.cs          入口与自检开关分发
├── MainWindow.xaml.cs   主窗口、页面宿主
├── Pages/               各页面（启动 / 实例 / 下载 / AI / 设置 …）
├── Services/            桥客户端、i18n、动画、缩略图、拖放等
├── Shell/               侧栏模型、冒烟、i18n 检查、导航导出
└── Themes/              配色与控件样式
```

## 授权

与主仓一致，见 [PyMCL](https://github.com/LQS660/PyMCL)。
