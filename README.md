# NexusExplorer

Windows 文件分类管理与媒体查看工具。

## 功能

- 创建和管理分类，批量归类、整理、重命名、移除或删除文件。
- 播放视频和音频，查看图片，支持 GIF、APNG 和 JFIF。
- 预览 TXT、Markdown 和 PDF；Markdown 可切换排版与原文，PDF 支持单页和连续阅读。
- 支持文件悬停缩略图、快捷分类、浅色／深色及跟随系统主题。

## 使用

1. 解压便携包，运行 `NexusExplorer.exe`。
2. 点击标题旁的“＋”新建分类；右键分类选择“导入文件”或“导入文件夹”，也可以从资源管理器直接拖入文件。
3. 双击分类查看文件列表，双击文件播放或预览。按住 Ctrl 多选，按住 Shift 选择连续范围。
4. 把文件拖到目标分类，或在底栏选择分类后点击绿色“√”归类。归类只改变软件内的归属；右键分类选择“整理”才会把文件搬入对应目录，并包含子分类。
5. 右键分类可重命名、移动、重新定位和钉住；钉住后可通过底栏快捷分类操作，快捷标签支持拖动排序。橙色文件夹表示待整理，绿色表示已整理。
6. 播放时使用下方控件调节进度、音量和音轨；悬停喇叭展开音量条，点击喇叭静音／恢复音量。
7. 图片可缩放和平移；文字乱码时切换编码；PDF 点击“单页／连续阅读”切换显示方式，输入页码并按 Enter 跳页，Ctrl＋滚轮缩放。

“移除”保留物理文件；“删除”会将相应文件送入回收站。右上角“外观设置”可切换主题。

## 仓库内容

- [src/NexusExplorer](src/NexusExplorer)：应用源码、配置模板及图标。
- [tests](tests)：自动化检查及随仓库提供的测试素材。
- [scripts](scripts)：便携包发布、解压目录安全更新与固定 mpv 原生依赖还原脚本。
- `NexusExplorer.sln`、`README.md` 和 `.gitignore`：解决方案、使用说明及忽略规则。

以下内容仅保留在本地，不随 Git 仓库分发：`docs/`、`outputs/user-visible-text/`、`tools/`、`AGENTS.md`、`HANDOVER.md` 和 `NexusExplorer V1.0 开发文档.md`。重新克隆仓库不会包含这些开发资料、文案清单或诊断工具；需要时单独保存、传递。运行数据、日志、旧程序和发布产物同样不提交到仓库，具体规则见 [.gitignore](.gitignore)。

## 构建、测试和发布

需要 Windows 10 2004（19041）及以上／Windows 11 x64 与 .NET 8 SDK；便携打包另需 Python 3.10 以上（只使用标准库）。Visual Studio 解决方案使用 x64 配置。项目目标为 `net8.0-windows10.0.19041.0`，构建还原时获取 Windows SDK 引用；便携版不要求安装开发 SDK、浏览器 PDF 插件或第三方 PDF 阅读器。

```powershell
dotnet restore NexusExplorer.sln -r win-x64
dotnet build NexusExplorer.sln -c Release --no-restore
# 只运行本次修改相关的测试；以下是分类图标的示例过滤条件。
dotnet test NexusExplorer.sln -c Release --no-restore --filter "FullyQualifiedName~LiveTreeFolderIconAndTextOnlyNavigation|FullyQualifiedName~ResourceDictionaryTests"
.\scripts\Update-Preview.ps1
```

每次提交后执行 [Update-Preview.ps1](scripts/Update-Preview.ps1)：先构建并生成 `artifacts/NexusExplorer_new.zip`，校验完整性后覆盖同名包；再从 ZIP 解压得到的文件更新 `artifacts/NexusExplorer_new/`。ZIP 内直接包含 EXE、native 和 licenses，不额外套一层目录。发布与解压校验的中间副本在结束后自动清理，不积累历史版本目录；清空 artifacts 后也可重新生成这两个产物。

[package_portable.py](scripts/package_portable.py)校验便携配置、必要文件、ZIP CRC、原生库哈希和解压文件哈希。包内包含自包含 EXE、WPF/SQLite 原生 DLL、固定 native/mpv 运行目录及必要许可和对应项目源码；不包含运行数据库、Storage、日志、普通文档或测试程序。

[Install-Portable.ps1](scripts/Install-Portable.ps1)和[validate_fixed_update.ps1](scripts/validate_fixed_update.ps1)保留解压目录中已有的 Storage、数据库（含 WAL/SHM/备份）、界面状态、appsettings.json 和日志，也保护配置显式指定的位置；新目录首次安装才写入默认配置。不会搬迁、重绑定或清空用户数据。发布前关闭该目录中的程序，脚本不会自动结束进程。

构建时 [Restore-Mpv.ps1](scripts/Restore-Mpv.ps1) 从固定发布下载开发包，核对 archive 与 DLL 的 SHA256，缓存于被忽略的 native/ 目录；运行时也核对固定 DLL。首次构建需要网络，以及支持 7z 的 Windows tar.exe；以后复用已校验缓存。完整版本、下载源和哈希记录在 [mpv-runtime.json](scripts/mpv-runtime.json)。

发布相关最小验证：`python -B .\tests\Package-Portable.Tests.py`、`.\tests\Update-Preview.Tests.ps1` 和 `.\tests\Publish-Portable.Tests.ps1`。已有发布产物可通过 `-PublishedDirectory` 复用；Python 路径可通过 `-PythonExecutable` 指定。原生下载归档缓存于 `native/mpv/cache`，不占 artifacts 的发布产物目录。

```powershell
# 此命令会打开数据库并执行必要的迁移；数据库兼容验收只对独立副本执行。
.\NexusExplorer.exe --verify-startup
```

结果写入 startup-verification.json，失败返回退出码 1。首次运行在 EXE 旁创建 data、logs、Storage，配置允许显式指定其他位置。

## 许可

本项目采用 GPL-2.0-or-later，见 [LICENSE](LICENSE)。便携包 licenses 目录附项目对应源码及原生组件许可、构建和源码获取说明。原生构建的 client API 头文件许可不替代完整 core 和依赖的许可，详见 [SOURCE.txt](licenses/SOURCE.txt)。
