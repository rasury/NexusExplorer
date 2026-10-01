# NexusExplorer 移交文档

> 交接日期:2026-10-02
> 仓库:https://github.com/rasury/NexusExplorer(main 分支,与本目录同步)
> 上一任:ZCode 助手(在用户 rasury 指导下开发)

---

## 1. 项目是什么

Windows 桌面的**分类文件管理与媒体查看软件**(.NET 8 WPF):
分类树对应真实物理目录;文件加入分类只写数据库不动文件("整理"时才移动);
内置视频/音频播放(LibVLC 回调渲染)与图片查看;底栏双层数据(导航+钉住的快捷分类)
支持播放中一键归类。

需求全文见《NexusExplorer V1.0 开发文档.md》(根目录,权威需求文档)。

## 2. 环境与构建

| 项 | 说明 |
|---|---|
| SDK | .NET 8.0.425(winget 安装;Git Bash 里需 `export PATH="/c/Program Files/dotnet:$PATH"`) |
| IDE | 无绑定,CLI 即可:构建 `dotnet build`,测试 `dotnet test`(57/57) |
| 发布 | `dotnet publish src/NexusExplorer -c Release -r win-x64 --self-contained true -o publish/NexusExplorer-win-x64`(单文件 exe,约 175MB) |
| 网络 | 直连 GitHub 不通,git 走本地代理 `127.0.0.1:7890`(已配在仓库 local config) |

### 发布红线(必读)

**publish/NexusExplorer-win-x64/ 里的 `data/`(用户分类数据库)、`logs/`、`Storage/` 是用户数据。**
重新发布前必须先 `mv publish/NexusExplorer-win-x64/data /tmp/bak` 再 `rm -rf publish/...` 发布,
发完 `mv` 回来。曾因整目录替换覆盖过用户数据。

## 3. 架构速览

```
View (XAML + code-behind)
 └─ ViewModel (CommunityToolkit.Mvvm)
     └─ Service
         └─ EF Core + SQLite (data/nexus.db)
```

- **Models/**:`Category`(树,含 IsPinned)、`FileItem`(绝对路径)
- **Data/AppDbContext**:EnsureCreated 建库;老库升级用**手工 ALTER**(见 App.xaml.cs `MigrateDatabase`,加列要在这里补)
- **Services/**:CategoryService(树/钉/排序/移动)、FileService(增删改查/移除vs删除/镜像导入)、
  OrganizationService(整理+冲突)、RecycleBinService、ExplorerService、MediaPlayerService
- **Views/**:MainWindow(三区布局:左分类+文件/右播放/底导航) + CategoryFilePanel + PlayerPanel +
  NavigationBar(双层:导航+绿勾 | 快捷分类)+ Dialogs/
- **tests/**:57 个 xUnit;`TestHost` 提供临时 SQLite+Storage;
  `VideoPlaybackSmokeTests` 用 ffmpeg 生成的真实视频验证帧像素写入

## 4. 核心设计决策(为什么这么做)

### 视频:LibVLC 回调渲染,不用 VideoView
VideoView(HwndHost 原生窗口)有 airspace 问题:浮在所有 WPF 内容之上,
遮挡左侧面板和控制条、点按钮无反应。改为 `SetVideoFormatCallbacks`+`SetVideoCallbacks`
把帧写进 WriteableBitmap,视频成为普通 WPF 元素。代价:软件解码
(`EnableHardwareDecoding=false`)、有约 1-2 帧延迟。**关键坑**(都踩过,别再踩):
- 位图**不能 Freeze**,`WritePixels(IntPtr 重载)` 对 Frozen 位图抛异常(黑屏)
- chroma 四字节必须逐字节写 'R','V','3','2'(十六进制值算错过一次→反复重协商+黑屏)
- format 回调返回**平面数 1**(返回 height 会反复重协商)
- VLC 事件全在后台线程,Dispatcher 必须**显式传入**(Application.Current 在多线程场景可能拿错)
- LibVLC 初始化冷启动可达 30 秒(几百个插件):Lazy + 后台预热,窗口先显示

### 交互模型:底栏"浏览+确认"
点击分类=纯导航(面包屑下钻);**绿色✔=把当前文件归入选中分类**;
文件已属于选中分类时✔置灰。第二层是钉住的快捷分类(左树右键钉/快捷按钮右键取消)。
用户明确否决过"点击叶子分类立即归类"的旧模型。

### 其他不变式
- 文件只属于一个分类(单分类约束,需求文档红线)
- 加入/归类绝不移动文件,整理才移动;移动失败绝不更新数据库
- 删除=回收站;移除=只删记录
- 拖拽必须用距离阈值(`SystemParameters.MinimumDragDistance`),否则 DoDragDrop
  劫持双击第二下(曾导致"双击分类文件列表不更新"+ NullRef)
- WPF Slider:MoveToPoint 会把 PreviewDown 标记已处理,事件必须
  `AddHandler(handledEventsToo:true)`;模板轨道 RepeatButton 不得绑命令
- `async void` 事件处理器必须 try-catch+Serilog,否则异常不可见

## 5. 已知未解决问题(接手先看这里)

### P1:图片默认 Fit 不生效(用户最新反馈,未修)
现象:打开图片仍显示原始大小(很大),需要滚动,没有缩到窗口内。
代码现状:[PlayerPanel.xaml.cs](src/NexusExplorer/Views/PlayerPanel.xaml.cs) `ApplyImageZoom()`
里 `ImageScale<0.001 → Stretch.Uniform + LayoutTransform=null`,理论上是 Fit;
XAML 里 ImageScroll 是普通 ScrollViewer。
**怀疑方向**:ScrollViewer 的内容在 Uniform 下按原始 DesiredSize 计量,
Image 的 Measure 未被约束(ViewportWidth 影响)——
建议试:`ImageDisplay` 外面套一层 `Grid Width={Binding ViewportWidth, ElementName=ImageScroll}`,
或改用 `Viewbox`/手动计算 `MaxWidth/MaxHeight`(最稳,布局自己算,不依赖 Stretch 语义)。
图片跨线程 Freeze 的修复已生效(旧 bug:后台线程解码后 UI 线程读 IsFrozen 也抛异常,已全部挪进 Task.Run)。

### P1:音频音质(用户反馈仍有损,多轮修复未解决,用户暂搁置)
已做:默认音量 100(VLC 音量是软件衰减,<100 量化损失)、纯音频 SetRole(Music)、
`--audio-resampler=soxr`。用户仍觉有损。
**下一步建议**(按性价比排序):
1. 让用户做 AB 盲测(同文件:本应用 vs VLC 桌面版 vs foobar),确认差异真实存在
2. 探针对比 LibVLCSharp vs VLC 桌面版的完整模块链:`libvlc_audio_output_callbacks`
   抓 PCM dump,与 VLC 桌面版 `--sout` 导出的 PCM 做二进制 diff——定位差异在解码后哪一环
3. 检查文件本身:M4A(那个测试文件 audioTrackCount=2,双音轨,查声道/轨选择是否正确)
4. 若一切相同,可能是用户系统音频链(独占/共享模式)问题,不是应用 bug

### P2(小)
- 视频软解 4K 高码率可能吃力(回调模式代价);需要时研究 format 回调改写输出尺寸让 VLC 端缩放
- 单文件 exe 冷启动首次自解压约 5 秒(可接受)

## 6. 测试约定

- 单元测试:接手者跑(`dotnet test`,57/57 基线)
- **GUI 功能:用户人工测试**(用户明确要求)。改完代码→构建发布→
  给用户列测试步骤(具体操作+预期)→等反馈。别自己做 UIAutomation(试过,窗口置前/坐标/菜单匹配全是坑,用户看着费劲)
- 用户测试通过前别急着宣称修好(有过两次"已修复"被打脸的教训)

## 7. Git 流程

用户已授权:每轮改完(构建+测试通过)由助手直接 `git add -A && git commit -m "<中文描述>" && git push`。
提交信息中文,概括本轮改动。发布后同步更新 publish 目录(注意第 2 节红线)。

## 8. 文件地图(快速定位)

| 要改什么 | 看哪里 |
|---|---|
| 分类树/右键菜单/拖拽 | Views/CategoryFilePanel.xaml(.cs)、ViewModels/CategoryViewModel.cs |
| 底栏导航/快捷分类/绿勾 | Views/NavigationBar.xaml(.cs)、ViewModels/NavigationViewModel.cs |
| 播放/图片/控制条 | Views/PlayerPanel.xaml(.cs)、ViewModels/PlayerViewModel.cs |
| VLC 回调/播放列表 | Services/MediaPlayerService.cs |
| 整理(移动文件+冲突) | Services/OrganizationService.cs |
| 文件夹镜像导入 | Services/FileService.cs `ImportDirectoryAsync` |
| 钉住持久化 | Category.IsPinned + App.xaml.cs `MigrateDatabase` |
| 主题配色(米白/橙/蓝/黑) | Resources/Theme.xaml、Controls.xaml |

## 9. 用户偏好(合作备忘)

- 中文交流;直接、要结果;烦"表面修复"——改完必须真实可用
- 反馈格式:"问题N:现象"——按编号逐个处理,修一个报告一个
| 用户原话(2026-10-02):"你真是太笨了"——多轮图片/音频修复未达预期是本次移交主因
- 数据安全极敏感:分类数据库丢了会真生气
- 用户自己测 GUI;测试步骤要具体(操作+预期)
