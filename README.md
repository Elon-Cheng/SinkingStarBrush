# 沉星画笔 · 游戏内版本

版本统一定义在 [version.json](version.json)。发布与双渠道校验流程见 [RELEASE.md](RELEASE.md)。

安装器支持“检查更新”和下载校验，更新源为 `Elon-Cheng/SinkingStarBrush` 的 GitHub 正式 Release。百度网盘入口在发布清单提供有效链接后启用。网络不可用时可继续离线安装。

## 安装与使用

1. 完全退出《沉星之序》。
2. 双击根目录的 `SinkingStarPainter-Setup.exe`，确认自动找到的游戏目录，点击“安装 / 更新 / 修复”。
3. 从 Steam 启动游戏，左上角 F8 打开画笔，点击工具栏齿轮打开“插件设置”。

安装器是 Windows x64 独立离线 EXE，内含插件和 .NET 运行时，可以单独复制分发，无需网页或额外下载依赖。支持目录选择、更新备份和卸载；游戏运行时禁止安装或卸载。

“插件设置”是游戏内独立面板，尚未加入游戏原生设置菜单。关闭“启用画笔插件”立即隐藏工具栏和笔迹，并停用 F9；禁用时按 F8 可打开设置以重新启用。状态保存在 `%LOCALAPPDATA%\SinkingStarAssistant\native\plugin-settings.ini`，重启仍有效。已保存的笔迹不会删除。

构建安装器：`./build-installer.ps1`。安装器隔离自测：`SinkingStarPainter-Setup.exe --self-test`，结果写入 `%TEMP%\SinkingStarSetup-test-result.txt`。

**当前使用游戏内版本，请阅读 [游戏内画笔使用说明](native/README.md)。**

本机游戏目录已安装原生 DX12 模块。从 Steam 正常启动游戏，按 **F8** 直接在游戏画面上绘图，按 **Esc** 返回游玩，按 **F9** 显示或隐藏标注。不要同时运行旧桌面助手，以免 F8/F9 快捷键冲突。

发布包：`dist/SinkingStarInGame`。原生源码：`native/overlay.cpp`。重建：`./build-native.ps1`。

下文保留的是旧版截图工作台说明，已不是当前主要使用方式。

---

# 旧版截图工作台 · Preview 0.1

面向《沉星之序》的 Windows 解谜辅助工具。当前是独立桌面程序：悬浮入口 + 截帧分析 + 可编辑标注记录，不修改游戏文件。

## 运行

打开 `dist/SinkingStarAssistant/SinkingStarAssistant.exe`。这是包含 .NET 运行时的 Windows x64 便携版，必须保留整个文件夹，不能只复制 exe。暂未制作安装器。

1. 启动游戏，设置为窗口化或无边框窗口。
2. 在助手中刷新窗口列表，选择游戏窗口。
3. 点击“悬浮工具栏”，可拖动“⠿ 沉星”移动浮条。
4. 回到游戏按 **F8**，或点击浮条的“截帧 / 画笔”。助手会隐藏自身窗口，切到目标窗口后截取客户区。
5. 在定格图片上选择画笔、直线、箭头、矩形、圆圈、编号或橡皮。橡皮按整个标记擦除。
6. 填写下方名称和备注，按 **Ctrl+S** 保存。按 **Esc** 返回；未保存时会提示。
7. 在工作台双击记录可继续编辑；“另存为新方案”保留原记录并创建副本。“前后对照”支持原图与标注、两条记录之间比较。

不运行游戏也能用“导入图片”试用完整标注流程。

## 快捷键

| 操作 | 默认键 |
| --- | --- |
| 截帧 | F8 |
| 显示 / 隐藏浮条 | F9 |
| 保存 | Ctrl+S |
| 撤销 / 重做 | Ctrl+Z / Ctrl+Y |
| 关闭分析窗口 | Esc |

全局快捷键可在工作台选择 F6～F11，点击“应用热键”；冲突时会显示提示。关闭助手后会释放热键。浮条收起后可用 F9 或主窗口重新显示。

## 数据与导出

数据位于 `%LOCALAPPDATA%/SinkingStarAssistant/`：

- `records/<id>/original.png`：原始图片。
- `records/<id>/annotated.png`：带标注的预览与导出图。
- `records/<id>/note.json`：名称、备注、时间及原始像素坐标的可编辑标记。
- `settings.json`：热键设置。
- `error.log`：操作异常记录（若产生）。

“打开记录目录”可直接访问。备份时复制整个数据目录。记录只在本地保存，没有上传功能。缩放显示不改变导出图片尺寸。

## 本版边界

- “定格”冻结的是捕获图片，**不会暂停游戏逻辑**。没有游戏单帧推进、精准输入、短视频回看。
- 画笔工作在截图编辑器中，尚未实现直接在实时游戏画面上绘制的透明叠层。
- 捕获使用可见桌面像素。游戏必须完整可见；被其他置顶窗口覆盖时，覆盖物可能进入截图。独占全屏、HDR、部分 GPU 呈现模式可能截图异常，需实机验证；可改用无边框模式或导入游戏截图。
- 不自动判断关卡、跟踪棋盘或吸附网格。关卡名和方案名手动填写。
- 当前界面与本地数据流程已验证；尚未在运行中的《沉星之序》上完成兼容性测试。
- 当前提供便携文件夹，尚无自动更新或安装器。

## 开发与验证

需要 Windows 和 .NET 10 SDK，无第三方 NuGet 依赖。

```powershell
dotnet build SinkingStarAssistant/SinkingStarAssistant.csproj -c Release
dotnet run --project SinkingStarAssistant -- --self-test
dotnet publish SinkingStarAssistant/SinkingStarAssistant.csproj -c Release -r win-x64 --self-contained true -o dist/SinkingStarAssistant
```

`--self-test` 在临时隔离目录测试标注数据往返、PNG 输出、标注渲染、撤销重做、原图保留、设置保存及损坏记录隔离。结果写到程序目录 `self-test.txt`，失败返回非零退出码；测试不改动实际记录。

技术参考：[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[Windows GDI 捕获示例](https://github.com/MicrosoftDocs/win32/blob/docs/desktop-src/gdi/capturing-an-image.md)。

## 后续优先级

1. 在游戏内验证截帧、焦点恢复、多显示器 DPI 和全屏兼容性；视结果升级捕获后端。
2. 直接在游戏画面上绘制、穿透切换和临时标注层。
3. 手动网格校准、路径吸附、记录缩略图与搜索。
4. 短时循环录制和逐帧回看。
