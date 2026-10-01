# NexusExplorer

面向 Windows 桌面的分类文件管理与媒体查看软件。详见《NexusExplorer V1.0 开发文档.md》。

## 核心功能

- **多级分类管理**:树形分类(最多 10 层),每个分类对应真实物理目录
- **分类与移动解耦**:文件加入分类或重新归类时只改数据库,物理文件不动
- **内置媒体查看**:视频/音频(LibVLC)、图片(WPF + ImageSharp)
- **播放时快速归类**:底部导航栏逐层浏览分类,一键把当前文件归入其他分类
- **整理**:把当前分类的文件实际移动到分类对应物理目录(冲突支持替换/跳过/保留两个)
- **安全删除**:文件与分类删除均送入 Windows 回收站
- **失效检测与重新定位**:外部移动/改名文件后手动重新定位,不自动猜测

## 界面布局

```
┌──────────────┬────────────────────────────┐
│ 分类树        │                            │
│   ↓          │        播放区域             │
│ 当前分类文件  │   (未播放时纯黑)           │
├──────────────┴────────────────────────────┤
│              分类导航栏(快速归类)           │
└───────────────────────────────────────────┘
```

## 技术栈

.NET 8 · WPF · MVVM(CommunityToolkit.Mvvm) · EF Core + SQLite · LibVLCSharp · ImageSharp · Serilog · xUnit

## 构建与运行

```bash
# 构建
dotnet build

# 运行单元测试
dotnet test

# 发布便携版(自包含,x64,单文件 exe)
dotnet publish src/NexusExplorer/NexusExplorer.csproj -c Release -r win-x64 --self-contained true -o publish/NexusExplorer-win-x64
```

发布目录结构(托管 DLL 全部打包进单文件 exe):

```
NexusExplorer.exe           # 单文件,含全部托管程序集与运行时(约 66MB)
appsettings.json            # 配置
libvlc\win-x64\            # VLC 原生库与插件(必须独立于 exe 存放)
*_cor3.dll / e_sqlite3.dll  # WPF/SQLite 原生库(.NET 单文件发布 WPF 的硬约束,必须留在 exe 旁)
data\ logs\ Storage\       # 运行时自动创建
```

## 便携式目录布局

首次运行会在 exe 同目录自动创建:

```
NexusExplorer.exe
appsettings.json   # 配置(Storage.RootPath 留空 = exe旁的 Storage 目录)
data\              # SQLite 数据库
logs\              # Serilog 滚动日志
Storage\           # 分类对应的物理目录根
```

`appsettings.json` 中 `Storage.RootPath` 可指定其他根目录(如 `D:\\NexusExplorer`)。

## 拖拽

- **Explorer → 文件列表/分类树**:文件或文件夹(递归)加入当前/目标分类
- **文件 → 分类树节点**:重新归类(只改 CategoryId)
- **分类 → 分类树节点**:移动整个子分类(数据库 + 物理目录同步)
