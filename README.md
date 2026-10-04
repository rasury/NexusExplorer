# NexusExplorer 2.0 预览版

Windows x64 分类文件管理与媒体查看软件，使用 WPF、SQLite、EF Core、LibVLC 与三区布局。当前为预览版；本次音频播放与音轨切换修正已通过用户验收。

## 使用

- 左侧分类单击只选择操作对象；双击标题打开文件列表，展开箭头独立展开。
- 文件支持 Ctrl 多选、Shift 区间选择、批量拖到分类、移除与删除；双击播放。
- 归类只改变归属，整理才搬文件。整理默认递归整个分类子树，各文件进入自己的分类目录。
- 整理文件时，同一卷且支持文件标识的路径直接移动文件，不复制或扫描全部内容；跨卷及无法确认标识的路径保留复制校验。两种方式均记录操作日志并支持失败恢复；日志显示实际移动方式与耗时。
- 播放队列保存文件 ID。归类当前播放文件后前进一次，跳过本批成功归类项；浏览其他分类不更换队列。
- 分类可重命名、移动、拖回顶层、重新定位与迁移目录。重新定位只重绑定已有目录；迁移搬整个物理目录。
- 删除分类只回收该子树所属的已登记文件，保留其他分类文件、未登记文件及非空目录。
- 分类右键“移除（保留目录和文件）”只移除该分类、子分类及其文件的软件登记；所有物理目录和文件保留原样。
- 快捷分类自动换行，可跨行拖动排序，顺序独立保存。
- 左侧分类树名称前的文件夹图标显示整理状态：橙色表示待整理，绿色表示已整理，悬停可查看说明。底栏导览和快捷分类按钮只显示名称；状态持久保存，显示时不遍历绑定文件地址。
- 视频由 VLC 原生窗口输出，控制条在画面下方。默认硬件解码，可关闭并重新打开媒体；选项保存到配置。
- 音频文件与视频音轨统一使用 VLC 的 MMDevice／WASAPI 输出及 Speex 重采样。Disable 真正取消底层音轨选择，恢复时重新建立音频链路。
- 图片默认适合窗口，支持缩放、平移、原始尺寸与前后切换；GIF、APNG（包括以 .png 保存的动画）按帧延时及循环设置播放，支持透明局部帧。拖文件或分类进入分类树后可用滚轮滚动寻找目标。
- 软件使用用户提供的图片作为 EXE、窗口和任务栏图标，保留完整画面；原图及 Windows 多尺寸 ICO 位于 [src/NexusExplorer/Assets](src/NexusExplorer/Assets)。

## 仓库内容

- [src/NexusExplorer](src/NexusExplorer)：应用源码、配置模板及图标。
- [tests](tests)：自动化检查及随仓库提供的测试素材。
- [scripts](scripts)：固定目录更新与 VLC 插件缓存生成脚本，发布时需要保留。
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

后续固定沿用 `artifacts/NexusExplorer-2.0.4-preview-win-x64`，目录名不随版本变化。[更新脚本](scripts/Update-Preview.ps1)先发布到固定临时目录 `artifacts/publish-staging`，检查程序是否关闭及文件是否可替换，再只复制 EXE、依赖库、发布元数据和本地存在的说明文档。已忽略的文档不存在时跳过，不影响更新。保留原位置的 Storage、数据库（含 WAL/SHM/备份）、界面状态、appsettings.json 和日志；也识别配置中显式指定的数据位置。不会复制、搬迁、重绑定或清空用户数据，不再逐次创建版本目录。便携包含自包含 EXE、WPF/SQLite 原生 DLL 与完整 libvlc/win-x64 插件目录，须保留整个目录。

发布流程会调用 [Prepare-VlcCache.ps1](scripts/Prepare-VlcCache.ps1)，从本次发布的 VLC 原生库生成插件缓存，并在独立进程验证缓存可用，以减少首次播放的初始化等待。

更新脚本的最小验证：`.\tests\Update-Preview.Tests.ps1`。已有发布产物可通过 `-PublishedDirectory` 复用。更新前先关闭固定目录中的程序；脚本不会自动结束用户进程。

```powershell
# 此命令会打开数据库并执行必要的迁移；数据库兼容验收只对独立副本执行。
.\NexusExplorer.exe --verify-startup
```

结果写入 startup-verification.json，失败返回退出码 1。首次运行在 EXE 旁创建 data、logs、Storage，配置允许显式指定其他位置。
