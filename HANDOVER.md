# NexusExplorer 重构移交说明

更新日期：2026-10-03。审查基线：main 的 0a3c02f。实施分支：codex/refactor-nexus-explorer。远端 main 在用户验收通过后才更新。

本说明替代原仓库内的移交结论。用户提供的本地移交文档原件保留在根目录；同名本地开发文档原件保存在 local-documents 中，没有改写。

## 当前职责

| 层 | 实现 |
|---|---|
| 领域模型 | Models：稳定分类 ID、独立 DirectoryLocation、文件位置与逻辑归属、操作日志 |
| 应用协调 | Application：IPlaybackEngine、播放快照、PlaybackSession、ImportCoordinator；Services：分类、文件与整理用例；MainViewModel 协调播放与刷新 |
| 基础设施 | Data：AppDbContext、版本升级；FileOperationExecutor：复制、校验、提交、补偿与恢复；MediaPlayerService：串行 VLC 调用；Infrastructure：配置、日志、界面状态 |
| WPF | ViewModels/Views：绑定、命中测试、多选、拖放、对话框、原生窗口与图片显示 |

这些职责保留在一个程序集内。数据库相关用例仍通过注入的 DbContextFactory 操作 EF Core，没有引入额外的仓储框架或新的运行服务。

## 数据契约

SchemaVersion=2，记录于 SQLite user_version。新库按完整模型建立；可识别的旧库使用 SQLite BackupDatabase 备份（包含 WAL），在独立副本上迁移，成功后写回。未知结构、循环、缺失父级、重复旧路径或多个分类共用同一物理目录停止升级，原库保持可恢复。

CategoryId 表示逻辑归属，DirectoryLocationId 表示实际目录身份；二者不得互相推导。目录保存显式根路径或父目录 ID＋路径片段。文件保存目录 ID＋相对路径，或外部绝对路径。AbsolutePath/PhysicalPath 是兼容查询快照；读取由 LocationService 解析，位置变更提交时刷新所有相关快照。

删除逻辑分类后，其他文件需要的位置记录保留；重新创建同一路径复用已有位置身份。路径比较使用 Windows 语义的 Unicode 大小写比较，SQLite NOCASE 在每个应用连接中注册相同规则。

不得按文件名自动猜测失效文件。分类重新定位展示完整映射，由用户确认旧、新位置；单文件重新定位也是用户明确选择。外部目录中的独立子分类不随父目录改名或迁移。

## 文件操作与退出

MutationGate 串行修改；未恢复日志阻止继续修改。同卷分类改名和树内移动使用 DirectoryRename 日志保护的原子目录移动，提交失败移回，启动时按提交状态恢复。显式目录迁移、跨盘及文件整理仍先复制到暂存位置、SHA-256 验证、提交位置、再清理源。既有冲突目标暂存为备份，提交成功后才回收。实际跨盘设备仍待验收。

状态包括 Prepared、Promoting、Committed、Completed、Failed、RecoveryRequired。崩溃时已完成项保持登记一致；启动回滚未提交项或清理已提交项。DirectoryRename 源和目标都存在时保留两者并要求检查，不自动删除。不明内容变化保留现场并提示人工检查。不得把未恢复状态改成 Completed 或删除日志来绕过检查。

2.0.1 中“跳过”使用未登记的已有目标并保留外部源；已登记目标拒绝改绑，双方记录保留。参见 docs/FEEDBACK-2.0.1.md。

取消在每个文件完成后停止后续项，复制提交中的单项先完成一致性处理。退出取消整理并等待当前修改结束、保存界面状态、等待 StopAndReleaseAsync，再由容器释放播放器。

替换受另一条文件记录管理的目标会拒绝，提示保留两个文件，避免覆盖另一分类的文件。分类删除逐项回收子树所属文件，非空物理目录留在原处并提示；部分失败会保留剩余记录并刷新界面。

## 媒体

已移除 WPF 像素缓冲回调。一个 VLC 原生播放器串行执行 Play/Stop/Seek/初始化/退出，Stop 在工作线程同步完成；VLC 回调只向 Dispatcher 投递，旧会话事件按版本丢弃。默认音量 100、速率 1，没有主动启用均衡器或音效。

音轨按钮可查看、选择实际音轨并记录音轨、声道、采样率、码率与设备。将 Logging.MinimumLevel 改为 Debug 可记录 VLC 模块日志。问题 1～3 的最终根因和实际音质不能由合成素材测试证明，需同文件、同轨、同音量与官方 VLC 比较。

## 验证与发布

构建和测试命令见 README。回归包含位置独立性、安全删除、递归整理、取消、队列、真实 SQLite 提交失败、文件锁、复制与提交阶段故障、重启恢复、真实 WPF 资源和布局、原生 AVI/WAV 播放。

媒体测试自行生成素材，缺素材不得静默返回。覆盖 64×48、320×240、640×360 原始视频、PCM WAV，以及已入库的 320×180 H.264/AAC 测试图案。真实 oldtest 的 1080p H.264/AAC 长视频分别执行软件与 D3D11VA 硬件解码跳转，MP3 解码输出与独立解码器比较。实际听画同步、多设备音质、H.265 等更多格式仍需验收。

每次发布使用新版本目录。用户数据不进入 Git，不清空旧运行目录。oldtest 原件保留，在线 SQLite 备份与完整 Storage 副本在 artifacts/old-data-validation；副本明确重映射后升级，未按文件名猜测历史路径。验收通过后再用中文提交更新 main。

真实样本暴露了 VLC 3 内置 MP4 demux 的跳转问题：软件和硬件解码均花屏并丢弃过期音频缓冲。MP4/MOV/M4V 明确选择随包已有的 avformat 解复用，保留原生解码与输出；本轮真实视频截图回归通过。证据、边界及音频对比见 docs/VERIFICATION.md。
