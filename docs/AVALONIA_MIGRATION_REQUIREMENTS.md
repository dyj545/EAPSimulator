# EAPSimulator WPF → Avalonia UI 迁移需求文档

> 日期: 2026-05-14
> 方案: 方案 A — Avalonia UI 全平台替换
> 目标: Windows / Linux 双平台运行，功能零丢失

---

## 一、迁移背景

### 当前架构
```
EAPSimulator.Core   (net9.0, 纯逻辑)     ← 已跨平台，不需改动
EAPSimulator.UI     (net9.0-windows, WPF) ← 唯一阻断点
```

### 迁移目标
- Core 库零改动
- UI 层从 WPF + WPF-UI 迁移到 Avalonia UI 11
- Windows 和 Linux 上均可编译、运行
- 功能和交互行为完全一致

---

## 二、迁移范围（逐项检查清单）

### 2.1 Core 库（不改动）

| 项目 | 状态 |
|------|------|
| EAPSimulator.Core.csproj | net9.0, 无 Windows 依赖 ✅ |
| HSMS TCP 通信 | System.Net.Sockets, 跨平台 ✅ |
| SECS-II 编解码 | 纯逻辑 ✅ |
| AutoReply 引擎 | 纯逻辑 ✅ |
| JSON 配置加载 | Newtonsoft.Json, 跨平台 ✅ |
| 日志 | Microsoft.Extensions.Logging, 跨平台 ✅ |

**结论: Core 库零改动，直接复用。**

### 2.2 UI 层 — 需迁移的文件清单

#### 项目文件 (1 个)
- [ ] `EAPSimulator.UI.csproj` — 改 TFM、替换 NuGet 包

#### 应用入口 (3 个文件)
- [ ] `App.xaml` — 改 Application 基类和资源字典
- [ ] `App.xaml.cs` — 改 using 和启动逻辑
- [ ] `AssemblyInfo.cs` — 可能需要调整

#### 窗口 (4 个文件)
- [ ] `MainWindow.xaml` — FluentWindow → Window + FluentTheme
- [ ] `MainWindow.xaml.cs` — Grid.SetColumnSpan 等 API
- [ ] `ConfigWindow.xaml` — FluentWindow → Window
- [ ] `ConfigWindow.xaml.cs` — 简单，基本不改

#### 视图 (10 个文件)
- [ ] `Views/AutoReplyView.xaml` — 最复杂，大量 WPF-UI 控件和触发器（迁移后为 `AutoReplyView.axaml`）
- [ ] `Views/AutoReplyView.xaml.cs` — 模板选择控件迁移后改为 `AutoCompleteBox`（不再依赖 ComboBox 事件处理）
- [ ] `Views/ConfigView.xaml` — ui:TextBox 等
- [ ] `Views/ConfigView.xaml.cs` — 基本不改
- [ ] `Views/MessageEditorView.xaml` — 复杂，TreeView + ContextMenu + 触发器
- [ ] `Views/MessageEditorView.xaml.cs` — 剪贴板、键盘快捷键、VisualTree
- [ ] `Views/MessageLogView.xaml` — 较简单
- [ ] `Views/MessageLogView.xaml.cs` — 基本不改
- [ ] `Views/StatusPanelView.xaml` — 较简单
- [ ] `Views/StatusPanelView.xaml.cs` — 基本不改

#### ViewModel (7 个文件)
- [ ] `ViewModels/MainViewModel.cs` — App.Current.Dispatcher.Invoke
- [ ] `ViewModels/MessageLogViewModel.cs` — App.Current.Dispatcher.Invoke
- [ ] `ViewModels/AutoReplyViewModel.cs` — OpenFileDialog (Microsoft.Win32)
- [ ] `ViewModels/ConfigViewModel.cs` — OpenFileDialog
- [ ] `ViewModels/MessageEditorViewModel.cs` — OpenFileDialog + SaveFileDialog
- [ ] `ViewModels/StatusPanelViewModel.cs` — 无 WPF 依赖
- [ ] `ViewModels/SecsItemViewModel.cs` — 无 WPF 依赖

#### 转换器 (2 个文件)
- [ ] `Converters/HexToBrushConverter.cs` — IValueConverter + SolidColorBrush
- [ ] `Converters/ListBoxIndexConverter.cs` — IValueConverter + ListBox

---

## 三、WPF → Avalonia 关键差异及处理要求

### 3.1 XAML 命名空间

| WPF 写法 | Avalonia 写法 | 影响文件 |
|-----------|--------------|---------|
| `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"` | 删除，改用 Avalonia 原生控件 | 全部 XAML |
| `clr-namespace:EAPSimulator.UI.Views` | `using:EAPSimulator.UI.Views` | MainWindow, AutoReplyView, MessageEditorView |
| `clr-namespace:EAPSimulator.UI.Converters` | `using:EAPSimulator.UI.Converters` | MainWindow, AutoReplyView, MessageEditorView |
| `clr-namespace:EAPSimulator.UI.ViewModels` | `using:EAPSimulator.UI.ViewModels` | AutoReplyView, MessageEditorView |

**要求:** 所有 `clr-namespace:` 必须替换为 `using:`，否则 Avalonia 编译失败。

### 3.2 窗口基类

| WPF | Avalonia | 影响文件 |
|-----|---------|---------|
| `<ui:FluentWindow>` | `<Window>` + FluentTheme | MainWindow.xaml, ConfigWindow.xaml |
| `ui:FluentWindow.Resources` | `Window.Resources` | MainWindow.xaml |

**要求:**
- `MainWindow` 和 `ConfigWindow` 的根元素改为 `<Window>`
- 在 App.axaml 中配置 `<FluentTheme />` 实现 Fluent 风格
- 移除所有 `ui:` 前缀的窗口标签

### 3.3 控件替换（WPF-UI → Avalonia 原生）

| WPF-UI 控件 | Avalonia 替代 | 出现次数 |
|-------------|-------------|---------|
| `<ui:Button Appearance="Primary">` | `<Button Classes="accent">` | ~15 处 |
| `<ui:Button Appearance="Success">` | `<Button Classes="success">` (需自定义) | ~8 处 |
| `<ui:Button Appearance="Danger">` | `<Button Classes="danger">` (需自定义) | ~5 处 |
| `<ui:Button Appearance="Secondary">` | `<Button>` (默认样式) | ~3 处 |
| `<ui:Button Appearance="Transparent">` | `<Button Classes="transparent">` (需自定义) | ~8 处 |
| `<ui:TextBox>` | `<TextBox>` | ~10 处 (ConfigView) |

**要求:**
- 所有 `ui:Button` 改为 Avalonia 的 `<Button>`
- `Appearance` 属性改为 Avalonia 的 `Classes` 样式类
- 需在 App.axaml 或 Styles 中定义 `accent/success/danger/transparent` 样式类
- `ui:TextBox` 改为 Avalonia 的 `<TextBox>`

### 3.4 布局和可见性

| WPF | Avalonia | 影响 |
|-----|---------|------|
| `Visibility="Collapsed"` | `IsVisible="False"` | 所有动态显示/隐藏 |
| `Visibility="{Binding xxx, Converter={StaticResource BoolToVis}}"` | `IsVisible="{Binding xxx}"` | MessageEditorView L190 |
| `BooleanToVisibilityConverter` | 删除，Avalonia 直接支持 bool → IsVisible | MainWindow, AutoReplyView, MessageEditorView |

**要求:**
- 所有 `Visibility` 绑定改为 `IsVisible` 绑定
- 删除所有 `BooleanToVisibilityConverter`
- Avalonia 中 `IsVisible` 直接接受 bool，无需转换器

### 3.5 样式和触发器

| WPF 语法 | Avalonia 语法 | 影响文件 |
|----------|-------------|---------|
| `<Style.Triggers>` | Avalonia 不支持 Style.Triggers，改用 `<DataTrigger>` 或代码 | AutoReplyView, MessageEditorView |
| `<Trigger Property="Content" Value="{x:Null}">` | 需用 DataTrigger 或 Binding + Converter | AutoReplyView L162 |
| `<DataTrigger Binding="{Binding IsList}" Value="True">` | Avalonia 支持，语法相同 | MessageEditorView L123 |
| `<DataTrigger Binding="{Binding SelectedScenario}" Value="{x:Null}">` | Avalonia 支持，语法相同 | AutoReplyView L255 |

**要求:**
- WPF 的 `Style.Triggers` 中的 `Trigger`（基于属性）需要重写
- WPF 的 `DataTrigger`（基于绑定）在 Avalonia 中基本兼容
- 逐个检查每个触发器，确保行为一致

### 3.6 ContextMenu

WPF 和 Avalonia 的 ContextMenu 语法基本相同，但有差异：

| WPF | Avalonia | 影响 |
|-----|---------|------|
| `<ContextMenu>` | `<ContextMenu>` (语法相同) | MessageEditorView |
| `<MenuItem.Icon>` | `<MenuItem.Icon>` (相同) | MessageEditorView |
| `ContextMenu="{StaticResource xxx}"` | `ContextMenu="{StaticResource xxx}"` (相同) | MessageEditorView |

**要求:** ContextMenu 语法基本兼容，但需要验证 Avalonia 中的渲染效果。

### 3.7 TreeView

| WPF | Avalonia | 影响 |
|-----|---------|------|
| `SelectedItemChanged` 事件 | `SelectionChanged` 事件 | MessageEditorView |
| `TreeViewItem` | 需检查数据模板 | MessageEditorView |

**要求:**
- `SelectedItemChanged` 改为 Avalonia 的 `SelectionChanged`
- TreeView 的 SelectedItem 类型可能不同（Avalonia 返回 object 或 IList）

### 3.8 ComboBox

> **⚠️ 此节已部分过时（2026-06-24）**：模糊搜索需求最终改用 `AutoCompleteBox` + `FilterMode=Contains` 实现，不再依赖 ComboBox 的编辑模式 / `StaysOpenOnEdit` / `IsTextSearchEnabled` 等行为。所有模板 / 子场景 / Host 消息名选择控件统一为 `AutoCompleteBox`。本节保留作为迁移期决策记录；新功能请参考 [SCENARIO_ENGINE.md §4.10](modules/SCENARIO_ENGINE.md#410-编辑器-ui-约定2026-06-24-之后)。

| WPF | Avalonia | 影响 |
|-----|---------|------|
| `IsTextSearchEnabled="False"` | 无直接等价，需检查 | AutoReplyView |
| `StaysOpenOnEdit="True"` | 需检查 Avalonia ComboBox 行为 | AutoReplyView |
| `TextChanged` 事件 | `TextChanged` 事件 (存在但用法可能不同) | AutoReplyView |
| `DropDownOpened` 事件 | `DropDownOpened` 事件 (需验证) | AutoReplyView |
| `cb.IsDropDownOpen` | `cb.IsDropDownOpen` (需验证) | AutoReplyView |

**要求:** ~~验证 Avalonia ComboBox 的编辑模式行为 / `StaysOpenOnEdit` 自定义实现 / 模糊搜索在 Avalonia 下表现~~ → **已废弃**：项目已迁移到 `AutoCompleteBox`，该控件原生支持 `Text` 两向绑定 + `FilterMode=Contains` 子串过滤；编辑模式由控件自身实现，无需 hack。注意点：`AutoCompleteBox` 顶层 `VerticalContentAlignment` 不生效，需要通过样式选择器 `AutoCompleteBox /template/ TextBox` 穿透到内层 TextBox 才能让文本垂直居中。

### 3.9 剪贴板和键盘

| WPF | Avalonia | 影响 |
|-----|---------|------|
| `Clipboard.SetText()` | `IClipboard.SetTextAsync()` | MessageEditorView.xaml.cs |
| `Keyboard.Modifiers` | `KeyModifiers` (Avalonia Input) | MessageEditorView.xaml.cs |
| `KeyEventArgs` | `KeyEventArgs` (不同命名空间) | MessageEditorView.xaml.cs |
| `PreviewKeyDown` | `KeyDown` (Avalonia 无 Preview) | MessageEditorView.xaml.cs |

**要求:**
- 剪贴板操作需通过 `TopLevel.GetTopLevel(this)?.Clipboard` 获取
- 键盘修饰键检查改为 Avalonia 的 `KeyModifiers` 枚举
- Avalonia 没有 Preview 事件隧道，KeyDown 已包含

### 3.10 文件对话框

| WPF | Avalonia | 影响文件 |
|-----|---------|---------|
| `Microsoft.Win32.OpenFileDialog` | `StorageProvider.OpenFilePickerAsync()` | AutoReplyVM, ConfigVM, MessageEditorVM |
| `Microsoft.Win32.SaveFileDialog` | `StorageProvider.SaveFilePickerAsync()` | MessageEditorVM |

**要求:**
- Avalonia 的文件对话框是异步的
- 需要通过 `TopLevel.GetTopLevel(this)?.StorageProvider` 获取
- ViewModel 中不能直接调用对话框，需要通过服务接口或传入 TopLevel
- **这是最大的架构改动** — ViewModel 中 3 处 OpenFileDialog 和 1 处 SaveFileDialog 都需要重构

### 3.11 线程调度

| WPF | Avalonia | 影响文件 |
|-----|---------|---------|
| `App.Current.Dispatcher.Invoke(...)` | `Dispatcher.UIThread.InvokeAsync(...)` | MainViewModel, MessageLogViewModel |

**要求:**
- 替换为 Avalonia 的 `Dispatcher.UIThread.InvokeAsync()`
- 引入 `using Avalonia.Threading;`

### 3.12 控件静态方法

| WPF | Avalonia | 影响文件 |
|-----|---------|---------|
| `Grid.SetColumnSpan(panel, N)` | `Grid.SetColumnSpan(panel, N)` (API 相同) | MainWindow.xaml.cs |

**要求:** Grid attached properties 在 Avalonia 中 API 相同，无需修改。

### 3.13 转换器 (IValueConverter)

| WPF | Avalonia | 影响文件 |
|-----|---------|---------|
| `System.Windows.Data.IValueConverter` | `Avalonia.Data.Converters.IValueConverter` | HexToBrushConverter, ListBoxIndexConverter |
| `System.Windows.Media.SolidColorBrush` | `Avalonia.Media.SolidColorBrush` | HexToBrushConverter |
| `System.Windows.Media.Colors` | `Avalonia.Media.Colors` | HexToBrushConverter |
| `System.Windows.Controls.ListBox` | `Avalonia.Controls.ListBox` | ListBoxIndexConverter |
| `CultureInfo culture` 参数 | Avalonia 的 Convert 签名相同 | 两个转换器 |

**要求:**
- 替换 using 命名空间
- API 签名基本相同，改命名空间即可

### 3.14 字体和图标

| WPF-UI | Avalonia | 影响 |
|--------|---------|------|
| `SymbolIcon` | 无直接等价，用 TextBlock + Unicode 或 PathIcon | MainWindow (如有使用) |
| Segoe MDL2 Assets | 不可用 (Windows 专用字体) | 需检查 |
| Segoe UI (默认字体) | Avalonia 默认字体跨平台 | 无需处理 |
| Consolas | 通用等宽字体，跨平台可用 | 无需处理 |
| FontFamily 属性 | Avalonia 语法相同 | 无需处理 |

**要求:**
- Unicode emoji 图标（📂💾🗑✅▼▲▶）跨平台可用
- 如使用了 SymbolIcon，替换为 PathIcon 或 Unicode 文本
- Consolas 字体在 Linux 上可能缺失，需提供回退字体

---

## 四、项目文件变更要求

### 4.1 EAPSimulator.UI.csproj

**变更前:**
```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net9.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
```

**变更后:**
```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net9.0</TargetFramework>
```

**NuGet 包替换:**

| 移除 | 替换为 |
|------|--------|
| WPF-UI 4.2.1 | Avalonia 11.x |
| — | Avalonia.Desktop 11.x |
| — | Avalonia.Themes.Fluent 11.x |
| — | Avalonia.Diagnostics 11.x (可选，调试用) |
| CommunityToolkit.Mvvm 8.x | 保留不变 |
| Microsoft.Extensions.DependencyInjection 9.x | 保留不变 |
| Microsoft.Extensions.Hosting 9.x | 保留不变 |
| Serilog.* | 保留不变 |

### 4.2 解决方案文件 (.sln)

保持不变，两个项目结构不变。

---

## 五、功能回归测试清单

迁移后必须逐项验证以下功能在 Windows 和 Linux 上均正常：

### 5.1 连接管理
- [ ] 选择协议类型（SECS-GEM / Custom Protocol）
- [ ] 选择连接模式（Active / Passive / Alternating）
- [ ] 点击 Connect 建立连接
- [ ] 点击 Disconnect 断开连接
- [ ] 状态面板正确显示连接状态

### 5.2 配置窗口
- [ ] 点击齿轮按钮打开配置窗口
- [ ] 修改 HSMS 参数（Host、Port、DeviceId、超时）
- [ ] 修改 Custom Protocol 参数
- [ ] 浏览按钮打开文件对话框选择配置文件
- [ ] 配置保存和加载

### 5.3 消息编辑器
- [ ] 左侧消息树正确显示分组和消息
- [ ] 右侧编辑面板显示消息体树
- [ ] 添加消息组、消息、消息项
- [ ] 删除消息组、消息、消息项
- [ ] 克隆消息组（带 (副本) 后缀）
- [ ] 克隆消息（同组内精确副本，无后缀）
- [ ] 上移/下移消息项
- [ ] 右键菜单正常工作
- [ ] W-Bit 复选框切换
- [ ] 发送消息（Ctrl+S）
- [ ] 打开消息模板文件（文件对话框）
- [ ] 保存消息模板文件（文件对话框）
- [ ] 键盘快捷键（Ctrl+C 复制、Ctrl+V 粘贴）

### 5.4 自动回复
- [ ] 快速回复 Tab 显示和操作
- [ ] 场景 Tab 显示和操作
- [ ] 添加/删除快速回复规则
- [ ] 添加/删除场景和步骤
- [ ] 场景步骤上下移动
- [ ] 模糊搜索模板功能
- [ ] 匹配条件编辑
- [ ] 保存/加载规则文件（文件对话框）
- [ ] 空状态面板正确隐藏/显示

### 5.5 消息日志
- [ ] 日志列表正确显示
- [ ] 过滤功能
- [ ] 清空日志
- [ ] 新消息自动滚动

### 5.6 状态面板
- [ ] 连接状态显示
- [ ] 设备信息显示
- [ ] 统计信息显示

### 5.7 跨平台验证
- [ ] Windows 上编译通过
- [ ] Windows 上所有功能正常
- [ ] Linux 上编译通过
- [ ] Linux 上所有功能正常
- [ ] Linux 上文件对话框正常
- [ ] Linux 上字体渲染正常

---

## 六、风险和注意事项

### 6.1 高风险项

1. **文件对话框重构** — ViewModel 中直接使用 `Microsoft.Win32.OpenFileDialog`，Avalonia 的对话框是异步的且需要 TopLevel 引用。需要重构为接口注入模式。

2. **Style.Triggers 重写** — WPF 的 `Trigger`（基于属性值）在 Avalonia 中没有直接等价物，需要改为 DataTrigger 或 Behavior。

3. ~~**模糊搜索 ComboBox** — `IsTextSearchEnabled` 和 `StaysOpenOnEdit` 在 Avalonia 中的行为可能不同，需要充分测试。~~ → **已替代（2026-06-24）**：改用 `AutoCompleteBox` + `FilterMode=Contains`，原生支持子串过滤；不再需要测试 ComboBox 的隐藏行为。

### 6.2 中风险项

4. **TreeView 行为差异** — Avalonia 的 TreeView 选择事件和 SelectedItem 类型与 WPF 不同。

5. **ContextMenu 渲染** — 样式和位置可能有差异。

6. **剪贴板 API** — Avalonia 的剪贴板是异步的，需要 async/await。

### 6.3 低风险项

7. **字体回退** — Consolas 在 Linux 上可能不存在，需配置字体回退。

8. **DPI 缩放** — Avalonia 和 WPF 的 DPI 处理方式不同，可能影响布局。

---

## 七、建议的迁移顺序

1. **Phase 0: 准备** — git 备份、创建分支
2. **Phase 1: 项目文件** — 改 csproj、装 NuGet 包、创建 App.axaml
3. **Phase 2: 简单视图** — StatusPanelView → MessageLogView → ConfigView
4. **Phase 3: 主窗口** — MainWindow + ConfigWindow
5. **Phase 4: 复杂视图** — MessageEditorView → AutoReplyView
6. **Phase 5: ViewModel 适配** — Dispatcher、文件对话框
7. **Phase 6: 转换器和样式** — IValueConverter、自定义样式类
8. **Phase 7: 测试** — 按第五节清单逐项验证
9. **Phase 8: 跨平台验证** — Linux 编译和运行测试

---

## 八、验收标准

- [ ] `dotnet build` 在 Windows 上零错误零警告
- [ ] `dotnet build` 在 Linux 上零错误零警告
- [ ] `dotnet run` 在 Windows 上启动正常
- [ ] `dotnet run` 在 Linux 上启动正常
- [ ] 第五节所有测试项通过
- [ ] 无功能回退
