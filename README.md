# NexusExplorer 2.0 预览版

Windows x64 分类文件管理与媒体查看软件，保留 WPF、SQLite、EF Core、LibVLC 与三区布局。此版本正在验收，已完成 oldtest 旧库副本升级与真实异常视频跳转回归；实际试听与完整鼠标交互仍待用户验收。

## 使用

- 左侧分类单击只选择操作对象；双击标题打开文件列表，展开箭头独立展开。
- 文件支持 Ctrl 多选、Shift 区间选择、批量拖到分类、移除与删除；双击播放。
- 归类只改变归属，整理才搬文件。整理默认递归整个分类子树，各文件进入自己的分类目录。
- 播放队列保存文件 ID。归类当前播放文件后前进一次，跳过本批成功归类项；浏览其他分类不更换队列。
- 分类可重命名、移动、拖回顶层、重新定位与迁移目录。重新定位只重绑定已有目录；迁移搬整个物理目录。
- 删除分类只回收该子树所属的已登记文件，保留其他分类文件、未登记文件及非空目录。
- 快捷分类自动换行，可跨行拖动排序，顺序独立保存。
- 左侧分类树名称前的文件夹图标显示整理状态：橙色表示待整理，绿色表示已整理，悬停可查看说明。底栏导览和快捷分类按钮只显示名称；状态持久保存，显示时不遍历绑定文件地址。
- 视频由 VLC 原生窗口输出，控制条在画面下方。默认硬件解码，可关闭并重新打开媒体；选项保存到配置。
- 图片默认适合窗口，支持缩放、平移、原始尺寸与前后切换。
- 软件使用用户提供的图片作为 EXE、窗口和任务栏图标，保留完整画面；原图及 Windows 多尺寸 ICO 位于 src/NexusExplorer/Assets。

## 构建、测试和发布

需要 Windows x64 与 .NET 8 SDK。Visual Studio 解决方案使用 x64 配置。

```powershell
dotnet restore NexusExplorer.sln -r win-x64
dotnet build NexusExplorer.sln -c Release --no-restore
# 只运行本次修改相关的测试；以下是分类图标的示例过滤条件。
dotnet test NexusExplorer.sln -c Release --no-restore --filter "FullyQualifiedName~LiveTreeFolderIconAndTextOnlyNavigation|FullyQualifiedName~ResourceDictionaryTests"
.\scripts\Update-Preview.ps1
```

后续固定沿用 `artifacts/NexusExplorer-2.0.4-preview-win-x64`，目录名不随版本变化。更新脚本先发布到固定临时目录 `artifacts/publish-staging`，检查程序是否关闭及文件是否可替换，再只复制 EXE、依赖库、发布元数据和文档。保留原位置的 Storage、数据库（含 WAL/SHM/备份）、界面状态、appsettings.json 和日志；也识别配置中显式指定的数据位置。不会复制、搬迁、重绑定或清空用户数据，不再逐次创建版本目录。便携包含自包含 EXE、WPF/SQLite 原生 DLL 与完整 libvlc/win-x64 插件目录，须保留整个目录。

更新脚本的最小验证：`.\tests\Update-Preview.Tests.ps1`。已有发布产物可通过 `-PublishedDirectory` 复用。更新前先关闭固定目录中的程序；脚本不会自动结束用户进程。

```powershell
# 此命令会打开数据库并执行必要的迁移；数据库兼容验收只对独立副本执行。
.\NexusExplorer.exe --verify-startup
```

结果写入 startup-verification.json，失败返回退出码 1。首次运行在 EXE 旁创建 data、logs、Storage，配置允许显式指定其他位置。

## 资料

- [重构评估与实现说明](docs/REFACTOR.md)
- [18 项验收步骤](docs/ACCEPTANCE.md)
- [2.0.1 第一轮反馈修正与验证](docs/FEEDBACK-2.0.1.md)
- [2.0.2 分类整理状态与验收](docs/ORGANIZATION-STATUS-2.0.2.md)
- [构建、旧库与真实媒体验证记录](docs/VERIFICATION.md)
- [可编辑 Python 音频实验脚本（当前测试方式）](tools/AudioLab/README.md)
- [独立音频诊断与 A/B 对比](tools/AudioDiagnostic/README.md)
- [当前移交说明](HANDOVER.md)
- 第一版开发文档保留为历史需求；本次明确要求和已确认行为优先。
