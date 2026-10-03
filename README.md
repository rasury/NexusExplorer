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
- 分类树和快捷分类显示整理状态：橙色“↗ 待整理”表示尚未确认或数据已变动，绿色“✓ 已整理”表示上次完整整理成功。状态持久保存，显示时不遍历绑定文件地址。
- 视频由 VLC 原生窗口输出，控制条在画面下方。默认硬件解码，可关闭并重新打开媒体；选项保存到配置。
- 图片默认适合窗口，支持缩放、平移、原始尺寸与前后切换。

## 构建、测试和发布

需要 Windows x64 与 .NET 8 SDK。Visual Studio 解决方案使用 x64 配置。

```powershell
dotnet restore NexusExplorer.sln -r win-x64
dotnet build NexusExplorer.sln -c Release --no-restore
dotnet test NexusExplorer.sln -c Release --no-restore
dotnet publish src/NexusExplorer/NexusExplorer.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/NexusExplorer-2.0.2-preview-win-x64
```

发布到新版本目录，保留旧软件的配置、数据库、日志与 Storage。禁止清空或覆盖旧运行目录。便携包含自包含 EXE、WPF/SQLite 原生 DLL 与完整 libvlc/win-x64 插件目录，须保留整个目录。

```powershell
# 仅在新发布的独立验收目录执行；检查数据库、实际 WPF 资源与 VLC 加载后退出。
.\NexusExplorer.exe --verify-startup
```

结果写入 startup-verification.json，失败返回退出码 1。首次运行在 EXE 旁创建 data、logs、Storage，配置允许显式指定其他位置。

## 资料

- [重构评估与实现说明](docs/REFACTOR.md)
- [18 项验收步骤](docs/ACCEPTANCE.md)
- [2.0.1 第一轮反馈修正与验证](docs/FEEDBACK-2.0.1.md)
- [2.0.2 分类整理状态与验收](docs/ORGANIZATION-STATUS-2.0.2.md)
- [构建、旧库与真实媒体验证记录](docs/VERIFICATION.md)
- [当前移交说明](HANDOVER.md)
- 第一版开发文档保留为历史需求；本次明确要求和已确认行为优先。
