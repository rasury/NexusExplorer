# NexusExplorer 2.0 预览版

Windows x64 分类文件管理与媒体查看软件，使用 WPF、SQLite、EF Core、libmpv 与三区布局。

播放内核已从 LibVLC 迁移为固定构建 `mpv v0.41.0-1100-gc15296420`（Client API 2.5 / Windows x64）。视频使用原生 HWND 与 GPU 输出，音频使用 WASAPI。独立 mpv 的两个问题样本软解和 d3d11va 硬解已获得用户正常反馈；集成版本已做直接相关的原生模块、音轨、AAC、弹窗、资源释放检查，真实听感和画面仍需在本软件中复验。
界面统一采用 MaterialDesignInXaml 5.3.2 与 `MaterialDesign3.Defaults`：8dp 间距、标准侧栏 DrawerHost、ColorZone 顶栏／底栏、Card 播放器、Material 控件与水波纹。默认跟随 Windows 明暗主题，右上角“外观设置”可切换浅色、深色或跟随系统；设置保存到 `Appearance.ThemeMode`。主色蓝紫、辅助色青绿；分类文件夹仍用橙色／绿色表示整理状态。

提示、输入、冲突、重新定位预览、整理结果、外观和音轨选择全部通过异步 `DialogHost.Show` 展示；长内容滚动，操作按钮固定在底部。应用启动／恢复阶段也使用临时 DialogHost，退出会取消待处理对话框。文件和目录选择使用 Windows 系统选择器，右键菜单使用框架 Material 菜单样式。主窗口采用框架独立窗口式 DialogHost，弹窗期间暂时隐藏原生视频区域，声音继续；关闭后恢复画面，避免原生窗口遮挡或截获弹窗点击。

## 使用

- 左侧分类单击只选择操作对象；双击分类整行（名称、图标或右侧空白）打开文件列表，展开箭头独立展开。
- 文件支持 Ctrl 多选、Shift 区间选择、批量拖到分类、移除与删除；双击播放。
- 文件行悬停 500ms 后显示 Material 静态缩略图预览。图片仅缩小解码首帧；视频及音频封面使用 Windows 缩略图接口，无法提供缩略图时显示类型图标，失效文件显示提示。后台串行按需加载，内存缓存最多 32MB；滚动、拖拽、移开鼠标或切换窗口时关闭预览，不改变选择或当前播放。
- 归类只改变归属，整理才搬文件。整理默认递归整个分类子树，各文件进入自己的分类目录。
- 整理文件时，同一卷且支持文件标识的路径直接移动文件，不复制或扫描全部内容；跨卷及无法确认标识的路径保留复制校验。两种方式均记录操作日志并支持失败恢复；日志显示实际移动方式与耗时。
- 播放队列保存文件 ID。归类当前播放文件后前进一次，跳过本批成功归类项；浏览其他分类不更换队列。
- 分类可重命名、移动、拖回顶层、重新定位与迁移目录。重新定位只重绑定已有目录；迁移搬整个物理目录。
- 新建分类时，同级分类名称重复会提示分类已存在；目录已存在但未绑定分类时，可确认绑定，保留现有目录和文件，不自动登记目录内容。
- 弹窗大小受当前屏幕工作区限制，长正文可滚动，确认和取消按钮保留在底部。
- 删除分类只回收该子树所属的已登记文件，保留其他分类文件、未登记文件及非空目录。
- 分类右键“移除（保留目录和文件）”只移除该分类、子分类及其文件的软件登记；所有物理目录和文件保留原样。
- 快捷分类自动换行，可跨行拖动排序，顺序独立保存；Ctrl 点击增减选择，Shift 按显示顺序选择区间，右键取消钉住全部选中的标签。多选时归类按钮禁用，单选仍用于归类。
- 底栏子分类导航限制为一行，最多直接显示 3 个子分类；“全部子分类”提供可搜索列表，其余快捷标签保持原有换行和多选。
- 左侧分类树名称前的文件夹图标显示整理状态：橙色表示待整理，绿色表示已整理，悬停可查看说明。底栏导览和快捷分类按钮只显示名称；状态持久保存，显示时不遍历绑定文件地址。
- 视频由 mpv 原生窗口输出，控制条在画面下方。默认硬件解码，可关闭并重新打开媒体；选项保存到配置。
- 音频文件与视频音轨共用 mpv 的 WASAPI 输出与默认处理链，不再套用 VLC 的 Speex 或强制解复用参数。Disable 真正取消底层音轨选择，恢复时重新建立音频链路。
- 裸 AAC（ADTS）在后台按完整帧数与采样率计算固定时长，避免码率估算导致总时长跳动；解析期间显示“读取时长中”，未能完整解析时标明估算值。AAC 由 mpv 选择解复用器，进度拖动按稳定时长换算。播放结束且不继续下一项或循环时，显示“已播放完”并保留最终进度。
- 图片默认适合窗口，支持缩放、平移、原始尺寸与前后切换；GIF、APNG（包括以 .png 保存的动画）按帧延时及循环设置播放，支持透明局部帧。拖文件或分类进入分类树后可用滚轮滚动寻找目标。
- 软件使用用户提供的图片作为 EXE、窗口和任务栏图标，保留完整画面；原图及 Windows 多尺寸 ICO 位于 [src/NexusExplorer/Assets](src/NexusExplorer/Assets)。

## 仓库内容

- [src/NexusExplorer](src/NexusExplorer)：应用源码、配置模板及图标。
- [tests](tests)：自动化检查及随仓库提供的测试素材。
- [scripts](scripts)：固定目录更新与固定 mpv 原生依赖还原脚本。
- `NexusExplorer.sln`、`README.md` 和 `.gitignore`：解决方案、使用说明及忽略规则。

以下内容仅保留在本地，不随 Git 仓库分发：`docs/`、`outputs/user-visible-text/`、`tools/`、`AGENTS.md`、`HANDOVER.md` 和 `NexusExplorer V1.0 开发文档.md`。重新克隆仓库不会包含这些开发资料、文案清单或诊断工具；需要时单独保存、传递。运行数据、日志、旧程序和发布产物同样不提交到仓库，具体规则见 [.gitignore](.gitignore)。

## 构建、测试和发布

需要 Windows x64 与 .NET 8 SDK。Visual Studio 解决方案使用 x64 配置。

```powershell
dotnet restore NexusExplorer.sln -r win-x64
dotnet build NexusExplorer.sln -c Release --no-restore
# 只运行本次修改相关的测试；以下是分类图标的示例过滤条件。
dotnet test NexusExplorer.sln -c Release --no-restore --filter "FullyQualifiedName~LiveTreeFolderIconAndTextOnlyNavigation|FullyQualifiedName~ResourceDictionaryTests"
.\scripts\Update-Preview.ps1
```

后续固定沿用 `artifacts/NexusExplorer-2.0.4-preview-win-x64`，目录名不随版本变化。[更新脚本](scripts/Update-Preview.ps1)先发布到固定临时目录 `artifacts/publish-staging`，检查程序是否关闭及文件是否可替换，再只复制 EXE、依赖库、发布元数据和本地存在的说明文档。文档和实验工具不进入便携运行包。保留原位置的 Storage、数据库（含 WAL/SHM/备份）、界面状态、appsettings.json 和日志；也识别配置中显式指定的数据位置。不会复制、搬迁、重绑定或清空用户数据，不再逐次创建版本目录。便携包含自包含 EXE、WPF/SQLite 原生 DLL 与固定版本 native/mpv 运行目录、必要许可与对应项目源码，须保留完整程序文件。

构建时 [Restore-Mpv.ps1](scripts/Restore-Mpv.ps1) 从固定发布下载开发包，核对 archive 与 DLL 的 SHA256，缓存于被忽略的 native/ 目录；运行时也核对固定 DLL。首次构建需要网络，以及支持 7z 的 Windows tar.exe；以后复用已校验缓存。完整版本、下载源和哈希记录在 [mpv-runtime.json](scripts/mpv-runtime.json)。

更新脚本的最小验证：`.\tests\Update-Preview.Tests.ps1`。已有发布产物可通过 `-PublishedDirectory` 复用。更新前先关闭固定目录中的程序；脚本不会自动结束用户进程。

```powershell
# 此命令会打开数据库并执行必要的迁移；数据库兼容验收只对独立副本执行。
.\NexusExplorer.exe --verify-startup
```

结果写入 startup-verification.json，失败返回退出码 1。首次运行在 EXE 旁创建 data、logs、Storage，配置允许显式指定其他位置。

## 许可

本项目采用 GPL-2.0-or-later，见 [LICENSE](LICENSE)。便携包 licenses 目录附项目对应源码及原生组件许可、构建和源码获取说明。原生构建的 client API 头文件许可不替代完整 core 和依赖的许可，详见 [SOURCE.txt](licenses/SOURCE.txt)。
