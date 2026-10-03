# NexusExplorer 用户可见文案清单

整理日期：2026-10-04。源码基线：`6c4770b`。

只列用户在运行软件时可能看到的文字，包括界面、菜单、悬停说明、弹窗、错误、数据绑定显示和图片文字。日志、源码注释、开发文档、内部标识及未接入当前界面的辅助方法不列入。

共 291 条使用位置记录：界面与弹窗 139 条，错误与警告 125 条，动态与系统文字 27 条。重复文案保留不同使用位置，条数不是去重后的句子数。

## 怎么修改

优先在 Excel 的黄色“新文案”列填写；不填表示保留当前文字。若希望显示为空，请填写 `【清空】`；若希望移除整个控件，请填写 `【移除控件】`。不要修改编号和当前文字，方便之后准确回填。

花括号中的表达式是变量，不是固定显示文字，例如 `{category.Name}` 表示分类名称。保持变量和必要换行；Excel 保留真实换行，本页用 `<br>` 显示。长模板可以整体改写，但不要把实际文件名、路径和数量改成固定字。

系统和 SDK 返回的文字随 Windows 语言、文件及错误情况变化，不能枚举所有可能结果；在“动态与系统文字”中列出来源和例子。其固定文案之外的内容若要统一，需增加翻译映射或自定义控件。图标文字需要编辑图片。

Excel 文件：[NexusExplorer-用户可见文案清单.xlsx](../outputs/user-visible-text/NexusExplorer-用户可见文案清单.xlsx)。修改清单本身不会立即改变软件；后续按编号实施替换。

## 界面与弹窗

| 编号 | 使用位置 | 类型 | 当前文字 | 新文案 | 源码定位 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| UI-001 | 主窗口 | 窗口标题 | NexusExplorer 2.0.4 预览版 |  | [src/NexusExplorer/Views/MainWindow.xaml:1](../src/NexusExplorer/Views/MainWindow.xaml#L1) | Window.Title |
| UI-002 | 主窗口 | 界面文字 | NexusExplorer |  | [src/NexusExplorer/Views/MainWindow.xaml:29](../src/NexusExplorer/Views/MainWindow.xaml#L29) | TextBlock.Text |
| UI-003 | 主窗口 | 界面文字 | 分类 · 播放 · 整理 |  | [src/NexusExplorer/Views/MainWindow.xaml:33](../src/NexusExplorer/Views/MainWindow.xaml#L33) | TextBlock.Text |
| UI-004 | 分类名称输入弹窗 | 固定文字 | 确定 |  | [src/NexusExplorer/Views/Dialogs/InputDialog.cs:31](../src/NexusExplorer/Views/Dialogs/InputDialog.cs#L31) |  |
| UI-005 | 分类名称输入弹窗 | 固定文字 | 取消 |  | [src/NexusExplorer/Views/Dialogs/InputDialog.cs:43](../src/NexusExplorer/Views/Dialogs/InputDialog.cs#L43) |  |
| UI-006 | 分类操作 | 动态文字模板 | {c.Name}: {c.PhysicalPath} → {Path.Combine(newDirectory, Path.GetRelativePath(category.PhysicalPath, c.PhysicalPath))} |  | [src/NexusExplorer/Services/CategoryService.cs:153](../src/NexusExplorer/Services/CategoryService.cs#L153) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-007 | 分类操作 | 动态文字模板 | {f.FileName}: {path} ({(File.Exists(path) ? "存在" : "失效")}) |  | [src/NexusExplorer/Services/CategoryService.cs:155](../src/NexusExplorer/Services/CategoryService.cs#L155) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-008 | 分类操作 | 固定文字 | 失效 |  | [src/NexusExplorer/Services/CategoryService.cs:155](../src/NexusExplorer/Services/CategoryService.cs#L155) | 这是外层模板中的可选词，单独列出便于替换。 |
| UI-009 | 分类操作 | 固定文字 | 存在 |  | [src/NexusExplorer/Services/CategoryService.cs:155](../src/NexusExplorer/Services/CategoryService.cs#L155) | 这是外层模板中的可选词，单独列出便于替换。 |
| UI-010 | 分类操作 | 动态文字模板 | 保留非空目录: {c.PhysicalPath} |  | [src/NexusExplorer/Services/CategoryService.cs:285](../src/NexusExplorer/Services/CategoryService.cs#L285) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-011 | 分类操作 | 固定文字 | 新建分类 |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:61](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L61) |  |
| UI-012 | 分类操作 | 动态文字模板 | 在「{parent.Name}」下新建分类 |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:68](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L68) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-013 | 分类操作 | 动态文字模板 | 重命名「{category.Name}」 |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:92](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L92) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-014 | 分类操作 | 动态文字模板 | 确定移除分类「{category.Name}」？<br><br>将移除该分类、{childCount} 个子分类及 {fileCount} 个文件的软件登记。<br>所有物理目录和文件保留原样，不移动、不删除，也不进入回收站。 |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:115](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L115) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-015 | 分类操作 | 动态文字模板 | 确定删除分类「{category.Name}」?<br><br>包含 {childCount} 个子分类、{fileCount} 个已登记文件。<br>仅回收归属这些分类的文件；其他分类文件与未登记文件保留，非空目录保留。 |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:131](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L131) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-016 | 分类操作 | 动态文字模板 | 确定删除分类「{category.Name}」?<br>对应的物理目录将进入回收站。 |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:132](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L132) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-017 | 分类整理状态说明 | 悬停说明 | 已整理：上次整理全部成功，本分类绑定文件已位于分类目录。软件外部的移动或删除不自动检测。 |  | [src/NexusExplorer/Models/Category.cs:40](../src/NexusExplorer/Models/Category.cs#L40) |  |
| UI-018 | 分类整理状态说明 | 悬停说明 | 待整理：尚未确认，或绑定文件、分类目录已变动；可能有文件位于其他目录。完整整理成功后更新标识。 |  | [src/NexusExplorer/Models/Category.cs:41](../src/NexusExplorer/Models/Category.cs#L41) |  |
| UI-019 | 分类树与文件区 | 按钮或控件文字 | ＋ 新建分类 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:56](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L56) | Button.Content |
| UI-020 | 分类树与文件区 | 按钮或控件文字 | 添加文件 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:61](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L61) | Button.Content |
| UI-021 | 分类树与文件区 | 按钮或控件文字 | 添加文件夹 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:66](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L66) | Button.Content |
| UI-022 | 分类树与文件区 | 按钮或控件文字 | ⚡ 整理 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:71](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L71) | Button.Content |
| UI-023 | 分类树与文件区 | 悬停说明 | 整理当前打开分类及全部子分类 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:71](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L71) | Button.ToolTip |
| UI-024 | 分类树与文件区 | 按钮或控件文字 | 取消整理 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:79](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L79) | Button.Content |
| UI-025 | 分类树与文件区 | 界面文字 | ⌂ 顶层（将分类拖到这里移回顶层） |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:89](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L89) | TextBlock.Text |
| UI-026 | 分类树与文件区 | 右键菜单 | 在资源管理器中打开 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:104](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L104) | MenuItem.Header |
| UI-027 | 分类树与文件区 | 右键菜单 | 📌 钉到底栏快捷分类 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:105](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L105) | MenuItem.Header |
| UI-028 | 分类树与文件区 | 右键菜单 | 新建子分类 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:106](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L106) | MenuItem.Header |
| UI-029 | 分类树与文件区 | 右键菜单 | 重命名 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:108](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L108) | MenuItem.Header |
| UI-030 | 分类树与文件区 | 右键菜单 | 重新定位目录… |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:109](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L109) | MenuItem.Header |
| UI-031 | 分类树与文件区 | 右键菜单 | 迁移目录… |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:110](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L110) | MenuItem.Header |
| UI-032 | 分类树与文件区 | 右键菜单 | 移到顶层 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:111](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L111) | MenuItem.Header |
| UI-033 | 分类树与文件区 | 右键菜单 | 上移 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:112](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L112) | MenuItem.Header |
| UI-034 | 分类树与文件区 | 右键菜单 | 下移 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:113](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L113) | MenuItem.Header |
| UI-035 | 分类树与文件区 | 右键菜单 | 移除（保留目录和文件） |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:115](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L115) | MenuItem.Header |
| UI-036 | 分类树与文件区 | 右键菜单 | 删除 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:116](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L116) | MenuItem.Header |
| UI-037 | 分类树与文件区 | 右键菜单 | 在资源管理器中打开 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:144](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L144) | MenuItem.Header |
| UI-038 | 分类树与文件区 | 右键菜单 | 重新定位… |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:145](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L145) | MenuItem.Header |
| UI-039 | 分类树与文件区 | 右键菜单 | 从分类移除(保留源文件) |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:147](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L147) | MenuItem.Header |
| UI-040 | 分类树与文件区 | 右键菜单 | 删除(源文件进回收站) |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:148](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L148) | MenuItem.Header |
| UI-041 | 分类树与文件区 | 固定文字 | 分类名称: |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:45](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L45) |  |
| UI-042 | 分类树与文件区 | 固定文字 | 选择文件 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:52](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L52) |  |
| UI-043 | 分类树与文件区 | 固定文字 | 选择导入文件夹 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:55](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L55) |  |
| UI-044 | 分类树与文件区 | 动态文字模板 | 重新定位「{name}」 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:58](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L58) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-045 | 分类树与文件区 | 固定文字 | 确认 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:65](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L65) |  |
| UI-046 | 分类树与文件区 | 固定文字 | 错误 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:66](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L66) |  |
| UI-047 | 分类树与文件区 | 固定文字 | 选择分类的新位置（不搬文件） |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:185](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L185) |  |
| UI-048 | 分类树与文件区 | 固定文字 | 选择迁移目标父目录 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:192](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L192) |  |
| UI-049 | 分类树与文件区 | 动态文字模板 | 迁移完整物理目录：<br>{c.PhysicalPath}<br>→ {Path.Combine(parent, Path.GetFileName(c.PhysicalPath))}<br>包含未登记文件。继续？ |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:193](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L193) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-050 | 分类树与文件区 | 动态文字模板 | 整理 {p.Completed}/{p.Total}: {p.FileName} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:214](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L214) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-051 | 分类树与文件区 | 动态文字模板 | 整理完成，已处理 {result.Count} 项 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:224](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L224) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-052 | 分类树与文件区 | 动态文字模板 | 整理已取消，已处理 {result.Count} 项 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:224](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L224) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-053 | 分类树与文件区 | 固定文字 | 请先双击打开分类，再拖入文件。 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:242](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L242) |  |
| UI-054 | 分类树与文件区 | 动态文字模板 | 正在导入到「{target.Name}」… |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:250](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L250) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-055 | 同名冲突弹窗 | 固定文字 | 文件冲突 |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:20](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L20) |  |
| UI-056 | 同名冲突弹窗 | 固定文字 | 目标目录已存在同名文件: |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:33](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L33) |  |
| UI-057 | 同名冲突弹窗 | 固定文字 | 跳过：使用目标目录已有文件，保留外部源文件；若目标已有分类记录则提示冲突。 |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:52](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L52) |  |
| UI-058 | 同名冲突弹窗 | 固定文字 | 替换 |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:78](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L78) |  |
| UI-059 | 同名冲突弹窗 | 固定文字 | 保留两个 |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:79](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L79) |  |
| UI-060 | 同名冲突弹窗 | 固定文字 | 跳过 |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:80](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L80) |  |
| UI-061 | 同名冲突弹窗 | 固定文字 | 取消整理 |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:81](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L81) |  |
| UI-062 | 同名冲突弹窗 | 固定文字 | 应用到全部后续冲突 |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:94](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L94) |  |
| UI-063 | 启动与全局错误 | 固定文字 | 以下操作需要手动检查，已保留原件：<br> |  | [src/NexusExplorer/App.xaml.cs:64](../src/NexusExplorer/App.xaml.cs#L64) | 后面拼接操作恢复明细或异常信息。 |
| UI-064 | 启动与全局错误 | 固定文字 | 操作恢复 |  | [src/NexusExplorer/App.xaml.cs:64](../src/NexusExplorer/App.xaml.cs#L64) | 后面拼接操作恢复明细或异常信息。 |
| UI-065 | 启动与全局错误 | 固定文字 | NexusExplorer |  | [src/NexusExplorer/App.xaml.cs:93](../src/NexusExplorer/App.xaml.cs#L93) | 后面拼接操作恢复明细或异常信息。 |
| UI-066 | 启动与全局错误 | 固定文字 | NexusExplorer |  | [src/NexusExplorer/App.xaml.cs:127](../src/NexusExplorer/App.xaml.cs#L127) |  |
| UI-067 | 底栏导览与快捷分类 | 固定文字 | 未分类 |  | [src/NexusExplorer/ViewModels/NavigationViewModel.cs:22](../src/NexusExplorer/ViewModels/NavigationViewModel.cs#L22) |  |
| UI-068 | 底栏导览与快捷分类 | 固定文字 | 未分类 |  | [src/NexusExplorer/ViewModels/NavigationViewModel.cs:68](../src/NexusExplorer/ViewModels/NavigationViewModel.cs#L68) |  |
| UI-069 | 底栏导览与快捷分类 | 界面文字 | 当前分类: |  | [src/NexusExplorer/Views/NavigationBar.xaml:118](../src/NexusExplorer/Views/NavigationBar.xaml#L118) | TextBlock.Text |
| UI-070 | 底栏导览与快捷分类 | 界面文字 | › |  | [src/NexusExplorer/Views/NavigationBar.xaml:148](../src/NexusExplorer/Views/NavigationBar.xaml#L148) | TextBlock.Text |
| UI-071 | 底栏导览与快捷分类 | 按钮或控件文字 | ⌂ 顶层 |  | [src/NexusExplorer/Views/NavigationBar.xaml:157](../src/NexusExplorer/Views/NavigationBar.xaml#L157) | Button.Content |
| UI-072 | 底栏导览与快捷分类 | 按钮或控件文字 | ✔ |  | [src/NexusExplorer/Views/NavigationBar.xaml:184](../src/NexusExplorer/Views/NavigationBar.xaml#L184) | Button.Content |
| UI-073 | 底栏导览与快捷分类 | 界面文字 | 快捷: |  | [src/NexusExplorer/Views/NavigationBar.xaml:200](../src/NexusExplorer/Views/NavigationBar.xaml#L200) | TextBlock.Text |
| UI-074 | 底栏导览与快捷分类 | 悬停说明 | 点击选中此分类,然后按 ✔ 归类 |  | [src/NexusExplorer/Views/NavigationBar.xaml:221](../src/NexusExplorer/Views/NavigationBar.xaml#L221) | Button.ToolTip |
| UI-075 | 底栏导览与快捷分类 | 右键菜单 | ✖ 取消钉住 |  | [src/NexusExplorer/Views/NavigationBar.xaml:230](../src/NexusExplorer/Views/NavigationBar.xaml#L230) | MenuItem.Header |
| UI-076 | 底栏导览与快捷分类 | 界面文字 | 在左侧分类树上右键可钉到这里 |  | [src/NexusExplorer/Views/NavigationBar.xaml:240](../src/NexusExplorer/Views/NavigationBar.xaml#L240) | TextBlock.Text |
| UI-077 | 底栏导览与快捷分类 | 固定文字 | 文件已在此分类中 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:132](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L132) |  |
| UI-078 | 底栏导览与快捷分类 | 固定文字 | 先点击选择一个分类 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:134](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L134) |  |
| UI-079 | 底栏导览与快捷分类 | 动态文字模板 | 把文件归入「{Vm.SelectedCategory.Name}」 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:135](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L135) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-080 | 底栏导览与快捷分类 | 固定文字 | 分类导航 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:152](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L152) |  |
| UI-081 | 底栏导览与快捷分类 | 固定文字 | 分类导航 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:192](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L192) |  |
| UI-082 | 底栏导览与快捷分类 | 固定文字 | 分类导航 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:212](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L212) |  |
| UI-083 | 底栏导览与快捷分类 | 固定文字 | 分类导航 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:224](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L224) |  |
| UI-084 | 播放区 | 固定文字 | 无法播放该媒体，请查看日志并尝试关闭硬件解码。 |  | [src/NexusExplorer/Services/MediaPlayerService.cs:137](../src/NexusExplorer/Services/MediaPlayerService.cs#L137) |  |
| UI-085 | 播放区 | 界面文字 | 🎵 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:17](../src/NexusExplorer/Views/PlayerPanel.xaml#L17) | TextBlock.Text |
| UI-086 | 播放区 | 界面文字 | 0:00 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:25](../src/NexusExplorer/Views/PlayerPanel.xaml#L25) | TextBlock.Text |
| UI-087 | 播放区 | 界面文字 | 0:00 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:27](../src/NexusExplorer/Views/PlayerPanel.xaml#L27) | TextBlock.Text |
| UI-088 | 播放区 | 按钮或控件文字 | ⏮ 上一项 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:30](../src/NexusExplorer/Views/PlayerPanel.xaml#L30) | Button.Content |
| UI-089 | 播放区 | 按钮或控件文字 | ▶ 播放 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:31](../src/NexusExplorer/Views/PlayerPanel.xaml#L31) | Button.Content |
| UI-090 | 播放区 | 按钮或控件文字 | ■ 停止 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:32](../src/NexusExplorer/Views/PlayerPanel.xaml#L32) | Button.Content |
| UI-091 | 播放区 | 按钮或控件文字 | ⏭ 下一项 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:33](../src/NexusExplorer/Views/PlayerPanel.xaml#L33) | Button.Content |
| UI-092 | 播放区 | 按钮或控件文字 | ➡ 顺序 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:34](../src/NexusExplorer/Views/PlayerPanel.xaml#L34) | Button.Content |
| UI-093 | 播放区 | 界面文字 | 🔊 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:35](../src/NexusExplorer/Views/PlayerPanel.xaml#L35) | TextBlock.Text |
| UI-094 | 播放区 | 悬停说明 | 关闭后使用软件解码；下次打开媒体生效 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:37](../src/NexusExplorer/Views/PlayerPanel.xaml#L37) | CheckBox.ToolTip |
| UI-095 | 播放区 | 按钮或控件文字 | 硬件解码 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:37](../src/NexusExplorer/Views/PlayerPanel.xaml#L37) | CheckBox.Content |
| UI-096 | 播放区 | 按钮或控件文字 | 音轨 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:39](../src/NexusExplorer/Views/PlayerPanel.xaml#L39) | Button.Content |
| UI-097 | 播放区 | 按钮或控件文字 | ⏮ 上一张 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:42](../src/NexusExplorer/Views/PlayerPanel.xaml#L42) | Button.Content |
| UI-098 | 播放区 | 按钮或控件文字 | ＋ |  | [src/NexusExplorer/Views/PlayerPanel.xaml:43](../src/NexusExplorer/Views/PlayerPanel.xaml#L43) | Button.Content |
| UI-099 | 播放区 | 按钮或控件文字 | 适合窗口 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:44](../src/NexusExplorer/Views/PlayerPanel.xaml#L44) | Button.Content |
| UI-100 | 播放区 | 按钮或控件文字 | － |  | [src/NexusExplorer/Views/PlayerPanel.xaml:45](../src/NexusExplorer/Views/PlayerPanel.xaml#L45) | Button.Content |
| UI-101 | 播放区 | 按钮或控件文字 | 1:1 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:46](../src/NexusExplorer/Views/PlayerPanel.xaml#L46) | Button.Content |
| UI-102 | 播放区 | 按钮或控件文字 | ⏭ 下一张 |  | [src/NexusExplorer/Views/PlayerPanel.xaml:47](../src/NexusExplorer/Views/PlayerPanel.xaml#L47) | Button.Content |
| UI-103 | 播放区 | 固定文字 | ⏸ 暂停 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:65](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L65) |  |
| UI-104 | 播放区 | 固定文字 | ▶ 播放 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:65](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L65) |  |
| UI-105 | 播放区 | 时间显示格式 | {(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2} |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:70](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L70) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-106 | 播放区 | 时间显示格式 | {(int)time.TotalMinutes}:{time.Seconds:D2} |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:70](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L70) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-107 | 播放区 | 固定文字 | ➡ 顺序 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:84](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L84) |  |
| UI-108 | 播放区 | 固定文字 | 🔀 随机 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:84](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L84) |  |
| UI-109 | 播放区 | 固定文字 | 🔁 列表循环 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:84](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L84) |  |
| UI-110 | 播放区 | 固定文字 | 🔂 单曲循环 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:84](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L84) |  |
| UI-111 | 播放区 | 固定文字 | 无可用音轨 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:123](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L123) |  |
| UI-112 | 播放区 | 动态文字模板 | {_effectiveScale:0.##}× |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:136](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L136) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-113 | 播放区 | 固定文字 | 适合窗口 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:136](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L136) |  |
| UI-114 | 整理结果弹窗 | 动态文字模板 | 分类「{categoryName}」整理完成:<br> |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:19](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L19) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 整理汇总的四段文字按顺序拼接。 |
| UI-115 | 整理结果弹窗 | 动态文字模板 |   移动 {moved + renamed} 个(其中改名保留 {renamed} 个)<br> |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:20](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L20) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 整理汇总的四段文字按顺序拼接。 |
| UI-116 | 整理结果弹窗 | 动态文字模板 |   跳过搬移并使用已有文件 {skipped} 个,已在目标位置 {already} 个<br> |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:21](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L21) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 整理汇总的四段文字按顺序拼接。 |
| UI-117 | 整理结果弹窗 | 动态文字模板 |   失效 {missing} 个,失败 {failed} 个 |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:22](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L22) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 整理汇总的四段文字按顺序拼接。 |
| UI-118 | 整理结果弹窗 | 固定文字 | <br>未处理的文件: |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:41](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L41) |  |
| UI-119 | 整理结果弹窗 | 动态文字模板 | • {problem.FileName} — {problem.Error ?? "源文件不存在"} |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:49](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L49) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-120 | 整理结果弹窗 | 固定文字 | 源文件不存在 |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:49](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L49) | 这是外层模板中的可选词，单独列出便于替换。 |
| UI-121 | 整理结果弹窗 | 固定文字 | … |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:58](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L58) |  |
| UI-122 | 整理结果弹窗 | 固定文字 | 整理结果 |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:66](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L66) |  |
| UI-123 | 整理结果弹窗 | 固定文字 | 确定 |  | [src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs:76](../src/NexusExplorer/Views/Dialogs/OrganizeResultDialog.cs#L76) |  |
| UI-124 | 文件列表图标 | 图标字符 | 🎬 |  | [src/NexusExplorer/Converters/FileIconConverter.cs:21](../src/NexusExplorer/Converters/FileIconConverter.cs#L21) |  |
| UI-125 | 文件列表图标 | 图标字符 | 🎵 |  | [src/NexusExplorer/Converters/FileIconConverter.cs:22](../src/NexusExplorer/Converters/FileIconConverter.cs#L22) |  |
| UI-126 | 文件列表图标 | 图标字符 | 🖼️ |  | [src/NexusExplorer/Converters/FileIconConverter.cs:23](../src/NexusExplorer/Converters/FileIconConverter.cs#L23) |  |
| UI-127 | 文件列表图标 | 图标字符 | 📄 |  | [src/NexusExplorer/Converters/FileIconConverter.cs:24](../src/NexusExplorer/Converters/FileIconConverter.cs#L24) |  |
| UI-128 | 文件操作 | 固定文字 | 当前分类 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:18](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L18) |  |
| UI-129 | 文件操作 | 固定文字 | 当前分类 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:51](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L51) |  |
| UI-130 | 文件操作 | 动态文字模板 | 当前分类:{_main.CurrentCategory.Name} |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:52](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L52) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-131 | 文件操作 | 动态文字模板 | 已添加 {result.Added.Count} 个文件。 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:130](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L130) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-132 | 文件操作 | 动态文字模板 | • {f.FileName}: {f.Error} |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:134](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L134) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-133 | 文件操作 | 动态文字模板 | 已重新定位「{file.FileName}」。 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:160](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L160) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-134 | 文件操作 | 动态文字模板 | 从分类移除 {files.Count} 个文件？源文件保留。 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:186](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L186) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-135 | 文件操作 | 动态文字模板 | 删除 {files.Count} 个文件？源文件进入回收站。 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:187](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L187) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-136 | 重新定位预览弹窗 | 固定文字 | 重新定位预览 |  | [src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs:12](../src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs#L12) |  |
| UI-137 | 重新定位预览弹窗 | 动态文字模板 | 仅更新位置，不搬文件。请核对全部 {mapping.Count} 项映射；显示失效的文件不会被猜测修复。 |  | [src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs:17](../src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs#L17) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| UI-138 | 重新定位预览弹窗 | 固定文字 | 确认重新定位 |  | [src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs:20](../src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs#L20) |  |
| UI-139 | 重新定位预览弹窗 | 固定文字 | 取消 |  | [src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs:21](../src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs#L21) |  |

## 错误与警告

| 编号 | 使用位置 | 类型 | 当前文字 | 新文案 | 源码定位 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| ERR-001 | 主窗口 | 固定文字 | 加载失败 |  | [src/NexusExplorer/Views/MainWindow.xaml.cs:28](../src/NexusExplorer/Views/MainWindow.xaml.cs#L28) |  |
| ERR-002 | 分类操作 | 校验或操作错误 | 分类结构存在循环。 |  | [src/NexusExplorer/Services/CategoryService.cs:49](../src/NexusExplorer/Services/CategoryService.cs#L49) |  |
| ERR-003 | 分类操作 | 校验或操作错误 | 分类结构存在循环。 |  | [src/NexusExplorer/Services/CategoryService.cs:64](../src/NexusExplorer/Services/CategoryService.cs#L64) |  |
| ERR-004 | 分类操作 | 校验或操作错误 | 分类结构存在循环。 |  | [src/NexusExplorer/Services/CategoryService.cs:71](../src/NexusExplorer/Services/CategoryService.cs#L71) |  |
| ERR-005 | 分类操作 | 校验或操作错误 | 分类名称不能为空或为 . / ..。 |  | [src/NexusExplorer/Services/CategoryService.cs:76](../src/NexusExplorer/Services/CategoryService.cs#L76) |  |
| ERR-006 | 分类操作 | 校验或操作错误 | 分类名称包含 Windows 不支持的字符或结尾。 |  | [src/NexusExplorer/Services/CategoryService.cs:78](../src/NexusExplorer/Services/CategoryService.cs#L78) |  |
| ERR-007 | 分类操作 | 校验或操作错误 | 分类名称不能使用 Windows 保留名称。 |  | [src/NexusExplorer/Services/CategoryService.cs:81](../src/NexusExplorer/Services/CategoryService.cs#L81) |  |
| ERR-008 | 分类操作 | 校验或操作错误 | 父分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:89](../src/NexusExplorer/Services/CategoryService.cs#L89) |  |
| ERR-009 | 分类操作 | 校验或操作错误 | 分类最多支持 10 层。 |  | [src/NexusExplorer/Services/CategoryService.cs:90](../src/NexusExplorer/Services/CategoryService.cs#L90) |  |
| ERR-010 | 分类操作 | 校验或操作错误 | 分类名称已存在 |  | [src/NexusExplorer/Services/CategoryService.cs:91](../src/NexusExplorer/Services/CategoryService.cs#L91) |  |
| ERR-011 | 分类操作 | 校验或操作错误 | 目标目录已存在，请使用重新定位关联已有目录。 |  | [src/NexusExplorer/Services/CategoryService.cs:93](../src/NexusExplorer/Services/CategoryService.cs#L93) |  |
| ERR-012 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:113](../src/NexusExplorer/Services/CategoryService.cs#L113) |  |
| ERR-013 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:122](../src/NexusExplorer/Services/CategoryService.cs#L122) |  |
| ERR-014 | 分类操作 | 校验或操作错误 | 分类名称已存在 |  | [src/NexusExplorer/Services/CategoryService.cs:123](../src/NexusExplorer/Services/CategoryService.cs#L123) |  |
| ERR-015 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:137](../src/NexusExplorer/Services/CategoryService.cs#L137) |  |
| ERR-016 | 分类操作 | 校验或操作错误 | 不能移到自身或子分类下。 |  | [src/NexusExplorer/Services/CategoryService.cs:139](../src/NexusExplorer/Services/CategoryService.cs#L139) |  |
| ERR-017 | 分类操作 | 校验或操作错误 | 目标分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:141](../src/NexusExplorer/Services/CategoryService.cs#L141) |  |
| ERR-018 | 分类操作 | 校验或操作错误 | 移动后超过最大 10 层限制。 |  | [src/NexusExplorer/Services/CategoryService.cs:143](../src/NexusExplorer/Services/CategoryService.cs#L143) |  |
| ERR-019 | 分类操作 | 校验或操作错误 | 目标分类下已存在同名分类 |  | [src/NexusExplorer/Services/CategoryService.cs:144](../src/NexusExplorer/Services/CategoryService.cs#L144) |  |
| ERR-020 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:150](../src/NexusExplorer/Services/CategoryService.cs#L150) |  |
| ERR-021 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:160](../src/NexusExplorer/Services/CategoryService.cs#L160) |  |
| ERR-022 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:166](../src/NexusExplorer/Services/CategoryService.cs#L166) |  |
| ERR-023 | 分类操作 | 校验或操作错误 | 目标目录已被另一个分类使用。 |  | [src/NexusExplorer/Services/CategoryService.cs:175](../src/NexusExplorer/Services/CategoryService.cs#L175) |  |
| ERR-024 | 分类操作 | 校验或操作错误 | 分类位置未初始化。 |  | [src/NexusExplorer/Services/CategoryService.cs:186](../src/NexusExplorer/Services/CategoryService.cs#L186) |  |
| ERR-025 | 分类操作 | 校验或操作错误 | 快捷分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:223](../src/NexusExplorer/Services/CategoryService.cs#L223) |  |
| ERR-026 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:238](../src/NexusExplorer/Services/CategoryService.cs#L238) |  |
| ERR-027 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:246](../src/NexusExplorer/Services/CategoryService.cs#L246) |  |
| ERR-028 | 分类操作 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/CategoryService.cs:257](../src/NexusExplorer/Services/CategoryService.cs#L257) |  |
| ERR-029 | 分类操作 | 校验或操作错误 | 空目录回收失败，分类记录保留。 |  | [src/NexusExplorer/Services/CategoryService.cs:286](../src/NexusExplorer/Services/CategoryService.cs#L286) |  |
| ERR-030 | 分类操作 | 动态文字模板 | 创建分类失败: {ex.Message} |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:86](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L86) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-031 | 分类操作 | 动态文字模板 | 重命名失败: {ex.Message} |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:106](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L106) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-032 | 分类操作 | 动态文字模板 | 移除分类失败: {ex.Message} |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:122](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L122) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-033 | 分类操作 | 动态文字模板 | 删除失败: {ex.Message} |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:150](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L150) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-034 | 分类操作 | 动态文字模板 | 移动分类失败: {ex.Message} |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:169](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L169) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-035 | 分类操作 | 动态文字模板 | 调整排序失败: {ex.Message} |  | [src/NexusExplorer/ViewModels/CategoryViewModel.cs:211](../src/NexusExplorer/ViewModels/CategoryViewModel.cs#L211) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-036 | 分类树与文件区 | 固定文字 | 目录不存在，请重新定位。 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:197](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L197) |  |
| ERR-037 | 分类树与文件区 | 固定文字 | 文件不存在，请重新定位。 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:199](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L199) |  |
| ERR-038 | 分类树与文件区 | 固定文字 | 整理失败，请查看日志 |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:227](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L227) |  |
| ERR-039 | 分类树与文件区 | 动态文字模板 | 导入失败：{ex.Message} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:252](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L252) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-040 | 启动与全局错误 | 固定文字 | 启动失败，原数据保留：<br> |  | [src/NexusExplorer/App.xaml.cs:93](../src/NexusExplorer/App.xaml.cs#L93) | 后面拼接操作恢复明细或异常信息。 |
| ERR-041 | 启动与全局错误 | 动态文字模板 | 发生未处理的错误:<br>{e.Exception.Message} |  | [src/NexusExplorer/App.xaml.cs:126](../src/NexusExplorer/App.xaml.cs#L126) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-042 | 启动配置 | 校验或操作错误 | 配置文件无法读取，已停止启动以避免切换到错误数据库。 |  | [src/NexusExplorer/Infrastructure/AppConfig.cs:33](../src/NexusExplorer/Infrastructure/AppConfig.cs#L33) |  |
| ERR-043 | 图片与动画 | 校验或操作错误 | GIF 解码后需要过多内存，请缩小尺寸或减少帧数后预览。 |  | [src/NexusExplorer/Services/ImageAnimation.cs:65](../src/NexusExplorer/Services/ImageAnimation.cs#L65) |  |
| ERR-044 | 图片与动画 | 校验或操作错误 | GIF 帧信息不完整，无法播放。 |  | [src/NexusExplorer/Services/ImageAnimation.cs:70](../src/NexusExplorer/Services/ImageAnimation.cs#L70) |  |
| ERR-045 | 图片与动画 | 校验或操作错误 | GIF 局部帧超出画布范围，无法播放。 |  | [src/NexusExplorer/Services/ImageAnimation.cs:92](../src/NexusExplorer/Services/ImageAnimation.cs#L92) |  |
| ERR-046 | 图片与动画 | 校验或操作错误 | APNG 解码后需要过多内存，请缩小尺寸或减少帧数后预览。 |  | [src/NexusExplorer/Services/ImageAnimation.cs:131](../src/NexusExplorer/Services/ImageAnimation.cs#L131) |  |
| ERR-047 | 图片与动画 | 校验或操作错误 | APNG 帧信息不完整，无法播放。 |  | [src/NexusExplorer/Services/ImageAnimation.cs:140](../src/NexusExplorer/Services/ImageAnimation.cs#L140) |  |
| ERR-048 | 图片与动画 | 校验或操作错误 | PNG 文件不完整。 |  | [src/NexusExplorer/Services/ImageAnimation.cs:164](../src/NexusExplorer/Services/ImageAnimation.cs#L164) |  |
| ERR-049 | 图片与动画 | 校验或操作错误 | APNG 未包含动画帧。 |  | [src/NexusExplorer/Services/ImageAnimation.cs:177](../src/NexusExplorer/Services/ImageAnimation.cs#L177) |  |
| ERR-050 | 底栏导览与快捷分类 | 校验或操作错误 | 分类结构存在循环。 |  | [src/NexusExplorer/ViewModels/NavigationViewModel.cs:88](../src/NexusExplorer/ViewModels/NavigationViewModel.cs#L88) |  |
| ERR-051 | 底栏导览与快捷分类 | 固定文字 | 当前没有打开的文件。 |  | [src/NexusExplorer/ViewModels/NavigationViewModel.cs:191](../src/NexusExplorer/ViewModels/NavigationViewModel.cs#L191) |  |
| ERR-052 | 底栏导览与快捷分类 | 固定文字 | 请先选择一个分类。 |  | [src/NexusExplorer/ViewModels/NavigationViewModel.cs:197](../src/NexusExplorer/ViewModels/NavigationViewModel.cs#L197) |  |
| ERR-053 | 底栏导览与快捷分类 | 固定文字 | 排序失败 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:87](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L87) |  |
| ERR-054 | 底栏导览与快捷分类 | 固定文字 | 分类导航 |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:101](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L101) |  |
| ERR-055 | 底栏导览与快捷分类 | 动态文字模板 | 导航失败: {ex.Message} |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:152](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L152) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-056 | 底栏导览与快捷分类 | 动态文字模板 | 选择失败: {ex.Message} |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:192](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L192) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-057 | 底栏导览与快捷分类 | 动态文字模板 | 取消失败: {ex.Message} |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:212](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L212) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-058 | 底栏导览与快捷分类 | 动态文字模板 | 归类失败: {ex.Message} |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:224](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L224) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-059 | 播放区 | 校验或操作错误 | 媒体文件不存在。 |  | [src/NexusExplorer/Services/MediaPlayerService.cs:77](../src/NexusExplorer/Services/MediaPlayerService.cs#L77) |  |
| ERR-060 | 播放区 | 校验或操作错误 | 播放器拒绝打开该媒体。 |  | [src/NexusExplorer/Services/MediaPlayerService.cs:90](../src/NexusExplorer/Services/MediaPlayerService.cs#L90) |  |
| ERR-061 | 播放区 | 固定文字 | 暂不支持预览该文件类型。 |  | [src/NexusExplorer/ViewModels/PlayerViewModel.cs:90](../src/NexusExplorer/ViewModels/PlayerViewModel.cs#L90) |  |
| ERR-062 | 播放区 | 固定文字 | 播放 |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:33](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L33) |  |
| ERR-063 | 数据库升级 | 校验或操作错误 | 数据库来自更新版本，不能降级打开。 |  | [src/NexusExplorer/Data/DatabaseInitializer.cs:55](../src/NexusExplorer/Data/DatabaseInitializer.cs#L55) |  |
| ERR-064 | 数据库升级 | 校验或操作错误 | 无法识别旧数据库结构。原数据库保留，升级已停止。 |  | [src/NexusExplorer/Data/DatabaseInitializer.cs:75](../src/NexusExplorer/Data/DatabaseInitializer.cs#L75) |  |
| ERR-065 | 整理文件 | 固定文字 | 源文件不存在 |  | [src/NexusExplorer/Services/OrganizationService.cs:35](../src/NexusExplorer/Services/OrganizationService.cs#L35) |  |
| ERR-066 | 整理文件 | 校验或操作错误 | 分类不存在。 |  | [src/NexusExplorer/Services/OrganizationService.cs:81](../src/NexusExplorer/Services/OrganizationService.cs#L81) |  |
| ERR-067 | 整理文件 | 校验或操作错误 | 分类结构存在循环。 |  | [src/NexusExplorer/Services/OrganizationService.cs:84](../src/NexusExplorer/Services/OrganizationService.cs#L84) |  |
| ERR-068 | 整理文件 | 校验或操作错误 | 目标文件已有分类记录，无法跳过并改用它。请选择保留两个文件或处理已有记录。 |  | [src/NexusExplorer/Services/OrganizationService.cs:130](../src/NexusExplorer/Services/OrganizationService.cs#L130) |  |
| ERR-069 | 整理文件 | 校验或操作错误 | 目标文件已不存在，原记录保留。 |  | [src/NexusExplorer/Services/OrganizationService.cs:131](../src/NexusExplorer/Services/OrganizationService.cs#L131) |  |
| ERR-070 | 文件操作 | 校验或操作错误 | 文件不存在: {absolutePath} |  | [src/NexusExplorer/Services/FileService.cs:69](../src/NexusExplorer/Services/FileService.cs#L69) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-071 | 文件操作 | 校验或操作错误 | 目标分类不存在。 |  | [src/NexusExplorer/Services/FileService.cs:74](../src/NexusExplorer/Services/FileService.cs#L74) |  |
| ERR-072 | 文件操作 | 校验或操作错误 | 不递归导入目录联接点或符号链接。 |  | [src/NexusExplorer/Services/FileService.cs:135](../src/NexusExplorer/Services/FileService.cs#L135) |  |
| ERR-073 | 文件操作 | 校验或操作错误 | 文件夹不存在: {directory} |  | [src/NexusExplorer/Services/FileService.cs:137](../src/NexusExplorer/Services/FileService.cs#L137) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-074 | 文件操作 | 校验或操作错误 | 文件不存在。 |  | [src/NexusExplorer/Services/FileService.cs:190](../src/NexusExplorer/Services/FileService.cs#L190) |  |
| ERR-075 | 文件操作 | 校验或操作错误 | 目标分类不存在。 |  | [src/NexusExplorer/Services/FileService.cs:196](../src/NexusExplorer/Services/FileService.cs#L196) |  |
| ERR-076 | 文件操作 | 校验或操作错误 | 文件不存在: {newAbsolutePath} |  | [src/NexusExplorer/Services/FileService.cs:213](../src/NexusExplorer/Services/FileService.cs#L213) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-077 | 文件操作 | 校验或操作错误 | 文件记录不存在。 |  | [src/NexusExplorer/Services/FileService.cs:217](../src/NexusExplorer/Services/FileService.cs#L217) |  |
| ERR-078 | 文件操作 | 校验或操作错误 | 该路径已被文件「{conflict.FileName}」占用。 |  | [src/NexusExplorer/Services/FileService.cs:224](../src/NexusExplorer/Services/FileService.cs#L224) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-079 | 文件操作 | 校验或操作错误 | 文件不存在。 |  | [src/NexusExplorer/Services/FileService.cs:257](../src/NexusExplorer/Services/FileService.cs#L257) |  |
| ERR-080 | 文件操作 | 校验或操作错误 | 文件不存在。 |  | [src/NexusExplorer/Services/FileService.cs:269](../src/NexusExplorer/Services/FileService.cs#L269) |  |
| ERR-081 | 文件操作 | 动态文字模板 | 文件已失效(可能被移动或删除):<br>{file.AbsolutePath}<br><br>请右键选择「重新定位」。 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:61](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L61) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-082 | 文件操作 | 固定文字 | 请先选择一个分类。 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:72](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L72) |  |
| ERR-083 | 文件操作 | 固定文字 | 请先选择一个分类。 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:91](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L91) |  |
| ERR-084 | 文件操作 | 动态文字模板 | <br>… 以及另外 {result.Failed.Count - 5} 个失败 |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:136](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L136) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-085 | 文件操作 | 动态文字模板 | 成功添加 {result.Added.Count} 个,失败 {result.Failed.Count} 个:<br>{errors} |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:137](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L137) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-086 | 文件操作 | 动态文字模板 | {f.FileName}: {ex.Message} |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:175](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L175) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-087 | 文件操作 | 动态文字模板 | 成功 {succeeded.Count}，失败 {errors.Count}<br> |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:178](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L178) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-088 | 文件操作 | 动态文字模板 | {file.FileName}: {ex.Message} |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:198](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L198) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-089 | 文件操作 | 动态文字模板 | 成功 {succeeded}，失败 {failures.Count}<br> |  | [src/NexusExplorer/ViewModels/FileListViewModel.cs:202](../src/NexusExplorer/ViewModels/FileListViewModel.cs#L202) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 |
| ERR-090 | 文件操作与恢复 | 校验或操作错误 | 存在未完成的文件操作，请重新启动以恢复，恢复完成前暂停新的文件操作。日志保留了原文件与目标位置。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:23](../src/NexusExplorer/Services/FileOperationExecutor.cs#L23) |  |
| ERR-091 | 文件操作与恢复 | 校验或操作错误 | 为避免跨目录操作，暂不搬迁符号链接或目录联接点。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:41](../src/NexusExplorer/Services/FileOperationExecutor.cs#L41) |  |
| ERR-092 | 文件操作与恢复 | 校验或操作错误 | 目标路径已有受管理文件，请选择保留两个文件。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:54](../src/NexusExplorer/Services/FileOperationExecutor.cs#L54) |  |
| ERR-093 | 文件操作与恢复 | 校验或操作错误 | 目标文件已存在。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:55](../src/NexusExplorer/Services/FileOperationExecutor.cs#L55) |  |
| ERR-094 | 文件操作与恢复 | 校验或操作错误 | 文件复制校验失败。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:68](../src/NexusExplorer/Services/FileOperationExecutor.cs#L68) |  |
| ERR-095 | 文件操作与恢复 | 校验或操作错误 | 目标文件发生变化，保留原文件等待恢复。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:103](../src/NexusExplorer/Services/FileOperationExecutor.cs#L103) |  |
| ERR-096 | 文件操作与恢复 | 校验或操作错误 | 源文件发生变化，保留源文件等待处理。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:106](../src/NexusExplorer/Services/FileOperationExecutor.cs#L106) |  |
| ERR-097 | 文件操作与恢复 | 校验或操作错误 | 旧目标已保留在备份位置，回收站清理失败。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:110](../src/NexusExplorer/Services/FileOperationExecutor.cs#L110) |  |
| ERR-098 | 文件操作与恢复 | 校验或操作错误 | 源文件无法验证，禁止自动回滚。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:115](../src/NexusExplorer/Services/FileOperationExecutor.cs#L115) |  |
| ERR-099 | 文件操作与恢复 | 校验或操作错误 | 目标内容发生变化，禁止自动回滚。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:123](../src/NexusExplorer/Services/FileOperationExecutor.cs#L123) |  |
| ERR-100 | 文件操作与恢复 | 校验或操作错误 | 目标路径被占用，保留备份等待恢复。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:130](../src/NexusExplorer/Services/FileOperationExecutor.cs#L130) |  |
| ERR-101 | 文件操作与恢复 | 校验或操作错误 | 目标与当前目录相同。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:143](../src/NexusExplorer/Services/FileOperationExecutor.cs#L143) |  |
| ERR-102 | 文件操作与恢复 | 校验或操作错误 | 源目录与目标目录不能相互包含。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:145](../src/NexusExplorer/Services/FileOperationExecutor.cs#L145) |  |
| ERR-103 | 文件操作与恢复 | 校验或操作错误 | 目标目录不存在。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:146](../src/NexusExplorer/Services/FileOperationExecutor.cs#L146) |  |
| ERR-104 | 文件操作与恢复 | 校验或操作错误 | 源目录不存在或目标目录已存在。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:148](../src/NexusExplorer/Services/FileOperationExecutor.cs#L148) |  |
| ERR-105 | 文件操作与恢复 | 校验或操作错误 | 目录复制校验失败。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:181](../src/NexusExplorer/Services/FileOperationExecutor.cs#L181) |  |
| ERR-106 | 文件操作与恢复 | 校验或操作错误 | 源和目标目录均存在，保留现场等待检查。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:247](../src/NexusExplorer/Services/FileOperationExecutor.cs#L247) |  |
| ERR-107 | 文件操作与恢复 | 校验或操作错误 | 源和目标目录均不存在，无法恢复。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:250](../src/NexusExplorer/Services/FileOperationExecutor.cs#L250) |  |
| ERR-108 | 文件操作与恢复 | 校验或操作错误 | 目录内容变化，保留现场等待恢复。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:262](../src/NexusExplorer/Services/FileOperationExecutor.cs#L262) |  |
| ERR-109 | 文件操作与恢复 | 校验或操作错误 | 目录含新增文件，已保留，需手动检查。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:270](../src/NexusExplorer/Services/FileOperationExecutor.cs#L270) |  |
| ERR-110 | 文件操作与恢复 | 校验或操作错误 | 目标目录验证失败，保留源目录。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:275](../src/NexusExplorer/Services/FileOperationExecutor.cs#L275) |  |
| ERR-111 | 文件操作与恢复 | 校验或操作错误 | 源目录验证失败，禁止自动回滚。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:283](../src/NexusExplorer/Services/FileOperationExecutor.cs#L283) |  |
| ERR-112 | 文件操作与恢复 | 校验或操作错误 | 暂存目录含其他文件，保留现场等待检查。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:301](../src/NexusExplorer/Services/FileOperationExecutor.cs#L301) |  |
| ERR-113 | 文件操作与恢复 | 校验或操作错误 | 文件无法送入回收站，记录已保留。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:313](../src/NexusExplorer/Services/FileOperationExecutor.cs#L313) |  |
| ERR-114 | 文件操作与恢复 | 校验或操作错误 | 已提交目录位置缺失，保留现场。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:337](../src/NexusExplorer/Services/FileOperationExecutor.cs#L337) | 操作恢复弹窗中的逐项明细。 |
| ERR-115 | 文件操作与恢复 | 校验或操作错误 | 无法识别操作日志，保留现场等待检查。 |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:357](../src/NexusExplorer/Services/FileOperationExecutor.cs#L357) | 操作恢复弹窗中的逐项明细。 |
| ERR-116 | 文件操作与恢复 | 动态文字模板 | {operation.Source}: {ex.Message} |  | [src/NexusExplorer/Services/FileOperationExecutor.cs:360](../src/NexusExplorer/Services/FileOperationExecutor.cs#L360) | 花括号为运行时变量或表达式，修改时保留；换行也应保留。 操作恢复弹窗中的逐项明细。 |
| ERR-117 | 文件操作与恢复 | 校验或操作错误 | 存在未完成的文件操作，请重新启动并完成恢复后再修改数据。 |  | [src/NexusExplorer/Services/MutationGate.cs:14](../src/NexusExplorer/Services/MutationGate.cs#L14) |  |
| ERR-118 | 目录和文件位置 | 校验或操作错误 | 目录位置存在循环。 |  | [src/NexusExplorer/Models/DirectoryLocation.cs:15](../src/NexusExplorer/Models/DirectoryLocation.cs#L15) |  |
| ERR-119 | 目录和文件位置 | 校验或操作错误 | 目录位置的父级缺失。 |  | [src/NexusExplorer/Models/DirectoryLocation.cs:17](../src/NexusExplorer/Models/DirectoryLocation.cs#L17) |  |
| ERR-120 | 目录和文件位置 | 校验或操作错误 | 文件相对路径越出目录位置。 |  | [src/NexusExplorer/Services/LocationService.cs:44](../src/NexusExplorer/Services/LocationService.cs#L44) |  |
| ERR-121 | 目录和文件位置 | 校验或操作错误 | 多个旧分类使用同一物理目录，需先明确位置映射，升级已停止。 |  | [src/NexusExplorer/Services/LocationService.cs:66](../src/NexusExplorer/Services/LocationService.cs#L66) |  |
| ERR-122 | 目录和文件位置 | 校验或操作错误 | 分类结构存在循环。 |  | [src/NexusExplorer/Services/LocationService.cs:72](../src/NexusExplorer/Services/LocationService.cs#L72) |  |
| ERR-123 | 目录和文件位置 | 校验或操作错误 | 分类父级缺失。 |  | [src/NexusExplorer/Services/LocationService.cs:74](../src/NexusExplorer/Services/LocationService.cs#L74) |  |
| ERR-124 | 目录和文件位置 | 校验或操作错误 | 文件所属分类缺失，升级已停止。 |  | [src/NexusExplorer/Services/LocationService.cs:94](../src/NexusExplorer/Services/LocationService.cs#L94) |  |
| ERR-125 | 目录和文件位置 | 校验或操作错误 | 旧数据库存在重复文件路径，升级已停止。 |  | [src/NexusExplorer/Services/LocationService.cs:96](../src/NexusExplorer/Services/LocationService.cs#L96) |  |

## 动态与系统文字

| 编号 | 使用位置 | 类型 | 当前文字 | 新文案 | 源码定位 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| SYS-001 | 分类名称输入弹窗 | 系统、数据或图片文字 | {原分类名称或输入内容} |  | [src/NexusExplorer/Views/Dialogs/InputDialog.cs:15](../src/NexusExplorer/Views/Dialogs/InputDialog.cs#L15) | 新建时可为空，重命名时填入现有名称；用户输入。 |
| SYS-002 | 分类名称输入弹窗 | 系统、数据或图片文字 | {弹窗标题} |  | [src/NexusExplorer/Views/Dialogs/InputDialog.cs:22](../src/NexusExplorer/Views/Dialogs/InputDialog.cs#L22) | 由新建分类、创建子分类或重命名入口传入；对应模板已列出。 |
| SYS-003 | 分类名称输入弹窗 | 系统、数据或图片文字 | {输入项说明} |  | [src/NexusExplorer/Views/Dialogs/InputDialog.cs:53](../src/NexusExplorer/Views/Dialogs/InputDialog.cs#L53) | 当前传入：分类名称:。 |
| SYS-004 | 分类整理状态说明 | 数据绑定显示 | {已整理或待整理的悬停说明} |  | [src/NexusExplorer/Resources/Controls.xaml:6](../src/NexusExplorer/Resources/Controls.xaml#L6) | 数据来自绑定；固定模板已在主清单列出，分类名及文件名来自用户数据。 |
| SYS-005 | 分类树与文件区 | 数据绑定显示 | {分类名称} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:13](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L13) | 数据来自绑定；固定模板已在主清单列出，分类名及文件名来自用户数据。 |
| SYS-006 | 分类树与文件区 | 数据绑定显示 | {文件名} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:22](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L22) | 数据来自绑定；固定模板已在主清单列出，分类名及文件名来自用户数据。 |
| SYS-007 | 分类树与文件区 | 数据绑定显示 | {文件列表标题} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml:128](../src/NexusExplorer/Views/CategoryFilePanel.xaml#L128) | 数据来自绑定；固定模板已在主清单列出，分类名及文件名来自用户数据。 |
| SYS-008 | 同名冲突弹窗 | 系统、数据或图片文字 | {冲突文件名} |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:39](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L39) | 显示实际文件名。 |
| SYS-009 | 同名冲突弹窗 | 系统、数据或图片文字 | {目标文件路径} |  | [src/NexusExplorer/Views/Dialogs/ConflictDialog.cs:47](../src/NexusExplorer/Views/Dialogs/ConflictDialog.cs#L47) | 显示实际目标路径。 |
| SYS-010 | 启动及全局错误弹窗 | 系统、数据或图片文字 | 确定（默认系统按钮） |  | [src/NexusExplorer/App.xaml.cs:64](../src/NexusExplorer/App.xaml.cs#L64)<br>[src/NexusExplorer/App.xaml.cs:93](../src/NexusExplorer/App.xaml.cs#L93)<br>[src/NexusExplorer/App.xaml.cs:125](../src/NexusExplorer/App.xaml.cs#L125) | 启动恢复、启动失败及未处理错误的 Windows 消息框按钮。 |
| SYS-011 | 导览及加载失败弹窗 | 系统、数据或图片文字 | 确定（默认系统按钮） |  | [src/NexusExplorer/Views/NavigationBar.xaml.cs:87](../src/NexusExplorer/Views/NavigationBar.xaml.cs#L87)<br>[src/NexusExplorer/Views/MainWindow.xaml.cs:28](../src/NexusExplorer/Views/MainWindow.xaml.cs#L28) | Windows 消息框，文字随系统语言。 |
| SYS-012 | 底栏导览与快捷分类 | 数据绑定显示 | {分类名称} |  | [src/NexusExplorer/Views/NavigationBar.xaml:11](../src/NexusExplorer/Views/NavigationBar.xaml#L11) | 数据来自绑定；固定模板已在主清单列出，分类名及文件名来自用户数据。 |
| SYS-013 | 底栏导览与快捷分类 | 数据绑定显示 | {当前播放文件的分类路径} |  | [src/NexusExplorer/Views/NavigationBar.xaml:122](../src/NexusExplorer/Views/NavigationBar.xaml#L122) | 数据来自绑定；固定模板已在主清单列出，分类名及文件名来自用户数据。 |
| SYS-014 | 底栏导览与快捷分类 | 数据绑定显示 | {当前播放文件的分类路径} |  | [src/NexusExplorer/Views/NavigationBar.xaml:122](../src/NexusExplorer/Views/NavigationBar.xaml#L122) | 数据来自绑定；固定模板已在主清单列出，分类名及文件名来自用户数据。 |
| SYS-015 | 播放区 | 系统、数据或图片文字 | {当前音频文件名} |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:78](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L78) | 音频播放区标题来自文件名。 |
| SYS-016 | 播放区音轨菜单 | 系统、数据或图片文字 | {音轨名称} |  | [src/NexusExplorer/Views/PlayerPanel.xaml.cs:119](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L119) | LibVLC 返回实际名称，例如 Disable、Track 1 或文件内音轨标题；需要新增映射才能统一改文案。 |
| SYS-017 | 整理文件 | 自动文件名格式 | {stem} ({index++}){extension} |  | [src/NexusExplorer/Services/OrganizationService.cs:140](../src/NexusExplorer/Services/OrganizationService.cs#L140) | 保留两个文件时自动生成的名称，例如 文件 (1).mp3；会改变实际文件名。 |
| SYS-018 | 确认弹窗 | 系统、数据或图片文字 | 是 / 否（含系统访问键） |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:65](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L65) | MessageBoxButton.YesNo：文字由 Windows 语言决定，例如 是(Y)、否(N)；不是应用中的固定字符串，若要统一措辞需自定义弹窗。 |
| SYS-019 | 程序图标 | 系统、数据或图片文字 | Gemini |  | [src/NexusExplorer/Assets/AppIcon.ico](../src/NexusExplorer/Assets/AppIcon.ico) | 文字嵌入图片，也存在于 AppIcon.png；修改需要重新制作图片和 ICO。 |
| SYS-020 | 程序图标 | 系统、数据或图片文字 | 你还能有Gemini聪明？ |  | [src/NexusExplorer/Assets/AppIcon.ico](../src/NexusExplorer/Assets/AppIcon.ico) | 文字嵌入图片，可能在较大图标或资源管理器中看到；不是界面字符串。 |
| SYS-021 | 窗口框架 | 系统、数据或图片文字 | {最小化、最大化、还原、关闭、移动、大小及窗口系统菜单} |  | [src/NexusExplorer/Views/MainWindow.xaml:1](../src/NexusExplorer/Views/MainWindow.xaml#L1)<br>[src/NexusExplorer/Views/Dialogs/DialogChrome.cs:12](../src/NexusExplorer/Views/Dialogs/DialogChrome.cs#L12) | 原生窗口边框和系统菜单的可见或悬停文字由 Windows 提供。 |
| SYS-022 | 输入控件 | 系统、数据或图片文字 | {撤销、剪切、复制、粘贴、删除、全选等系统菜单文字} |  | [src/NexusExplorer/Views/Dialogs/InputDialog.cs:13](../src/NexusExplorer/Views/Dialogs/InputDialog.cs#L13) | WPF TextBox 默认编辑菜单和系统输入法候选文字，由框架或输入法提供。 |
| SYS-023 | 选择导入或定位目录 | 系统、数据或图片文字 | {Windows 文件夹选择器的按钮、标签、导航名称和提示} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:68](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L68) | 例如 选择文件夹、取消、新建文件夹、文件夹、搜索；系统文字随 Windows 版本和语言变化，目录名称来自用户数据。 |
| SYS-024 | 选择文件和重新定位文件 | 系统、数据或图片文字 | {Windows 文件选择器的按钮、标签、导航名称和提示} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:52](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L52)<br>[src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:58](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L58) | 例如 打开、取消、文件名、文件类型、所有文件、搜索、桌面、此电脑；由 Windows、Shell 扩展和实际目录决定。软件仅设置标题，不提供固定过滤文案。 |
| SYS-025 | 重新定位预览弹窗 | 系统、数据或图片文字 | {分类与文件位置映射，项间空一行} |  | [src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs:24](../src/NexusExplorer/Views/Dialogs/LocationPreviewDialog.cs#L24) | 分类和文件映射模板在主清单中；路径来自实际数据。 |
| SYS-026 | 错误与播放弹窗 | 系统、数据或图片文字 | 确定（含系统访问键） |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:66](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L66)<br>[src/NexusExplorer/Views/PlayerPanel.xaml.cs:33](../src/NexusExplorer/Views/PlayerPanel.xaml.cs#L33) | MessageBoxButton.OK：文字由 Windows 语言决定；自定义输入、整理结果中的确定按钮另有独立记录。 |
| SYS-027 | 错误和失败明细 | 系统、数据或图片文字 | {ex.Message / e.Exception.Message / problem.Error / f.Error} |  | [src/NexusExplorer/Views/CategoryFilePanel.xaml.cs:70](../src/NexusExplorer/Views/CategoryFilePanel.xaml.cs#L70) | 应用会直接显示系统、SQLite、.NET、WPF、LibVLC 或图片库的异常信息；内容随错误、文件及系统语言变化，无法列成有限的固定句子。应用自定义中文异常已逐项列出。 |
