# 沉星画笔 · SinkingStarBrush

为《沉星之序》提供游戏内画笔和标注工具，方便记录解谜思路、标出路线与关键位置。按 **F8** 即可直接在游戏画面上绘图，完成后继续游玩。

适用于 **Windows x64 / Steam 版 / DirectX 12**。当前版本以 [version.json](version.json) 为准。

## 功能介绍

- **游戏内绘图**：提供自由画笔、直线、箭头、矩形、圆圈、编号和整笔橡皮。
- **颜色与笔触**：通过“墨色”调整颜色和线条粗细。
- **编辑标注**：支持撤销、重做和清空；清空操作也可撤销。
- **保存与恢复**：将当前笔迹保存在本机，需要时从工具栏恢复。
- **随时显示或隐藏**：退出绘图后可以保留标注，按 F9 隐藏或显示工具栏及笔迹。
- **插件开关**：在工具栏“设置”中启用或关闭插件，设置自动保存。
- **安装与管理**：安装器支持自动查找 Steam 游戏目录、安装、更新、修复和卸载，以及检查 GitHub 正式版更新。

标注使用屏幕坐标，不会跟随场景或镜头移动；绘图不会暂停游戏。目前保存功能只有一个槽位，再次保存会覆盖上一份笔迹。

## 如何安装

### 1. 下载安装器

下载仓库根目录的 **[SinkingStarPainter-Setup.exe](https://github.com/Elon-Cheng/SinkingStarBrush/raw/refs/heads/main/SinkingStarPainter-Setup.exe)**。

也可以打开 [安装器文件页面](https://github.com/Elon-Cheng/SinkingStarBrush/blob/main/SinkingStarPainter-Setup.exe)，点击 **Download raw file** 下载。如果已经克隆仓库或下载并解压源码 ZIP，直接使用根目录中的同名 EXE。

安装器包含插件与所需的 .NET 运行时，下载完成后可以离线安装，无需自行编译或单独安装 .NET。

### 2. 安装到游戏目录

1. **完全退出《沉星之序》。**
2. 双击运行 `SinkingStarPainter-Setup.exe`。
3. 确认安装器自动找到的游戏目录。如果没有找到，点击“选择游戏目录”，选择包含 `sinking_star.exe` 的文件夹。
4. 点击 **“安装 / 更新 / 修复”**，等待显示安装成功。
5. 从 Steam 启动游戏，或点击安装器中的“启动游戏”。

若提示没有写入游戏目录的权限，关闭安装器后右键选择“以管理员身份运行”，再执行安装。

### 3. 开始使用

1. 进入游戏后按 **F8**，展开左侧画笔工具栏。
2. 选择画笔或图形工具，在画面上按住鼠标左键拖动绘制；通过“墨色”调整颜色和粗细。
3. 按 **Esc**、**F8** 或点击“完成”，结束绘图并继续游戏，标注仍可保留显示。
4. 需要暂时隐藏标注时按 **F9**；需要保留笔迹时点击“保存”，之后点击“恢复”读取。

## 快捷键

| 按键 | 功能 |
| --- | --- |
| F8 | 进入 / 退出绘图；插件关闭时打开设置以便重新启用 |
| F9 | 显示 / 隐藏工具栏和全部标注（插件启用时） |
| Esc | 退出绘图或关闭插件设置 |
| Ctrl + Z | 绘图时撤销 |
| Ctrl + Y | 绘图时重做 |

## 更新、停用与卸载

**更新：** 打开安装器，点击“检查更新”。有正式版本可下载时，点击“下载并校验安装器”；校验成功后打开下载目录，退出游戏，再运行新安装器并点击“安装 / 更新 / 修复”。也可以重新下载仓库中的安装器手动更新。新版本在下次启动游戏时加载。

在线更新依赖 GitHub 正式 Release 及其更新清单。检查更新失败时，仍可使用已下载的安装器离线安装。正式发布文件见 [Releases](https://github.com/Elon-Cheng/SinkingStarBrush/releases)。

**临时停用：** 在游戏内点击工具栏“设置”，取消“启用画笔插件”。工具栏和笔迹会隐藏，F9 停用；按 F8 可再次打开设置并启用。此设置会在重启后保留。

**卸载：** 完全退出游戏，运行安装器，确认游戏目录后点击“卸载插件”。卸载会保留已保存的笔迹和设置。

## 常见问题

### 按 F8 没有出现工具栏？

- 确认已运行安装器并显示安装成功。仅下载或克隆仓库不会自动安装到游戏中。
- 安装或更新后，完全退出游戏再从 Steam 启动。
- 确认安装时选择的是当前 Steam 游戏目录。
- 如果正在运行旧版 `SinkingStarAssistant.exe` 桌面助手，请先关闭，避免 F8 / F9 热键冲突。
- 仍有问题时，点击安装器“打开日志”，查看 `overlay.log`。

### 安装器提示发现其他来源的 dxgi.dll？

游戏目录可能存在其他图形插件。安装器会拒绝覆盖无法确认归属的 DLL；请先确认该文件来自哪个插件，并按照对应插件的说明处理冲突。

### 保存的笔迹在哪里？

在资源管理器地址栏输入：

```text
%LOCALAPPDATA%\SinkingStarAssistant\native
```

| 文件 | 内容 |
| --- | --- |
| `last-drawing.ssp` | 已保存的笔迹 |
| `plugin-settings.ini` | 插件启用状态 |
| `overlay.log` | 插件加载与渲染日志 |

切换关卡或移动镜头后，建议清空或隐藏旧标注。插件主要面向键盘鼠标操作；手柄输入、HDR、多显示器及其他图形插件共存等场景尚未完成系统性验证。

## 开发与构建

源码包含当前游戏内插件 `native/`、安装器 `Installer/`，以及早期桌面截图助手 `SinkingStarAssistant/`。

构建需要 Windows、Visual Studio 2022 C++ 工具链、.NET 10 SDK 和 Git。首次构建需联网获取 Dear ImGui 与 Windows SDK 依赖。

```powershell
# 构建游戏内插件，输出到 dist/SinkingStarInGame
./build-native.ps1

# 构建独立安装器，并更新根目录的 EXE
./build-installer.ps1

# 运行安装器隔离自检
Start-Process -FilePath .\SinkingStarPainter-Setup.exe -ArgumentList '--self-test' -Wait
Get-Content "$env:TEMP\SinkingStarSetup-test-result.txt"
```

自检涵盖安装、校验、更新备份、卸载和更新下载异常处理；游戏内绘图效果仍需启动游戏验证。

技术说明见 [游戏内模块文档](native/README.md)，发布和校验流程见 [RELEASE.md](RELEASE.md)。游戏内界面使用 Dear ImGui，其 MIT 许可随构建产物提供，也可在安装器“开源许可”中查看。
