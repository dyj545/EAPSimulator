# UI 通用约定

> 本文档记录 Avalonia UI 的通用约定，所有模块的 UI 开发都应遵循。

## 1. 主题与颜色

### 1.1 不要硬编码颜色

**错误：**
```xaml
<Border Background="#FAFAFA" BorderBrush="#E0E0E0"/>
<TextBlock Foreground="#333333"/>
```

**正确：**
```xaml
<Border Background="{DynamicResource SystemControlBackgroundListLowBrush}"
        BorderBrush="{DynamicResource SystemControlForegroundListLowBrush}"/>
<TextBlock Foreground="{DynamicResource SystemControlForegroundBaseHighBrush}"/>
```

### 1.2 可用主题资源

| 用途 | 资源名 |
|---|---|
| 卡片背景 | `SystemControlBackgroundListLowBrush` |
| 卡片边框 | `SystemControlForegroundListLowBrush` |
| 提示框背景 | `SystemControlBackgroundListMediumBrush` |
| 主要文字 | `SystemControlForegroundBaseHighBrush` |
| 次要文字 | `SystemControlForegroundBaseMediumBrush` |
| 成功色 | `#4CAF50`（品牌色，可硬编码） |
| 警告色 | `#F57C00`（品牌色，可硬编码） |
| 错误色 | `#D32F2F`（品牌色，可硬编码） |

### 1.3 自定义样式

在 `App.axaml` 里定义全局样式类：

```xaml
<Style Selector="Button.accent">
    <Setter Property="Background" Value="#1976D2"/>
    <Setter Property="Foreground" Value="White"/>
</Style>
<Style Selector="Button.accent:pointerover">
    <Setter Property="Background" Value="#1565C0"/>
</Style>

<Style Selector="Border.section-card">
    <Setter Property="BorderBrush" Value="{DynamicResource SystemControlForegroundListLowBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="8"/>
    <Setter Property="Padding" Value="16"/>
</Style>
```

## 2. 布局规则

### 2.1 ScrollViewer 内必须用 Grid

**错误：**
```xaml
<ScrollViewer>
    <StackPanel>  <!-- 子元素宽度不被约束 -->
        <TextBlock Text="Long text..."/>
    </StackPanel>
</ScrollViewer>
```

**正确：**
```xaml
<ScrollViewer>
    <Grid>
        <StackPanel>  <!-- Grid 约束宽度 -->
            <TextBlock Text="Long text..." TextWrapping="Wrap"/>
        </StackPanel>
    </Grid>
</ScrollViewer>
```

### 2.2 标签 + 输入框布局

统一用 Grid，标签固定宽度，输入框 `*` 列：

```xaml
<Grid ColumnDefinitions="100,*">
    <TextBlock Grid.Column="0" Text="标签"/>
    <TextBox Grid.Column="1" Text="{Binding Value}"/>
</Grid>
```

**标签宽度约定：**
- 短标签（2-4 字）：80-100px
- 中标签（5-8 字）：120-140px
- 长标签（9 字以上）：160px

### 2.3 短字段并列两列

端口、ID 等短字段用 5 列 Grid 并列：

```xaml
<Grid ColumnDefinitions="100,*,16,80,*">
    <TextBlock Grid.Column="0" Text="主机"/>
    <TextBox   Grid.Column="1" Text="{Binding Host}"/>
    <!-- Column 2 是 16 像素间隙 -->
    <TextBlock Grid.Column="3" Text="端口"/>
    <TextBox   Grid.Column="4" Text="{Binding Port}"/>
</Grid>
```

### 2.4 输入框高度

- 主要输入框：36px
- 紧凑输入框：32px
- 按钮：32-36px（与输入框对齐）

### 2.5 间距

- Section 之间：20px
- Section 内部：12px
- 行内字段：8-10px
- 标签与输入框：0（Grid 自动对齐）

### 2.6 模糊搜索 / 可输入下拉 — 用 `AutoCompleteBox`

任何"列表挑一个 + 也允许直接输入新值"的场景都用 `AutoCompleteBox` + `FilterMode="Contains"`，不要再走 ComboBox 的编辑模式 / IsTextSearchEnabled / StaysOpenOnEdit 那条老路（Avalonia 下行为不一致且不可控）。

```xaml
<AutoCompleteBox ItemsSource="{Binding TemplateNames}"
                 Text="{Binding SelectedTemplateName}"
                 FilterMode="Contains"
                 Padding="4,2" FontSize="11"
                 Watermark="(输入关键字过滤)"/>
```

⚠️ **内层 TextBox 不响应顶层 `VerticalContentAlignment`**。文本默认贴顶，必须通过样式选择器穿透：

```xaml
<UserControl.Styles>
    <Style Selector="AutoCompleteBox /template/ TextBox">
        <Setter Property="VerticalContentAlignment" Value="Center"/>
    </Style>
</UserControl.Styles>
```

把它放在使用 AutoCompleteBox 的 View 顶部一次，覆盖该 View 内所有实例。

对于模板名、Host 消息名、子场景名这类“点击后通常要直接替换”的输入框，给 `AutoCompleteBox` 增加 `Classes="templatePicker"`，样式统一设置 `MinimumPrefixLength=0` / `MinimumPopulateDelay=0`。点击/聚焦只做自动全选，不要打开下拉；只在 `TextInputEvent`（真实键盘文本输入）到达 templatePicker 后延后一拍 `IsDropDownOpen=true` 展开过滤结果。不要用普通 `GotFocus`、`PointerPressed` 或 `TextChanged` 打开下拉：切换选中项/数据绑定刷新也会改写 Text，容易误弹其他模板清单。自动全选可捕获模板内层 `TextBox` 的 `GotFocusEvent` / `PointerPressedEvent` 调 `SelectAll()`，但不要在延迟回调中再 `Focus()` 旧 TextBox。不要给所有 AutoCompleteBox 无差别全选：Label 跳转等字段经常需要局部编辑。

**何时不用 AutoCompleteBox**：

- 仅从固定列表里选、绝不需要输入新值 → 用 `ComboBox`（保留键盘下拉/列表语义）。
- 列表元素需要复杂的图标 + 多行模板 → ComboBox + ItemTemplate（AutoCompleteBox 的下拉模板能力较弱）。

## 3. 样式选择器语法

**错误（嵌套 Style）：**
```xaml
<Style Selector="Button.accent">
    <Style Selector="PointerOver">  <!-- 错 -->
        <Setter Property="Background" Value="..."/>
    </Style>
</Style>
```

**正确（扁平选择器）：**
```xaml
<Style Selector="Button.accent">
    <Setter Property="Background" Value="#1976D2"/>
</Style>
<Style Selector="Button.accent:pointerover">
    <Setter Property="Background" Value="#1565C0"/>
</Style>
<Style Selector="Button.accent:pressed">
    <Setter Property="Background" Value="#0D47A1"/>
</Style>
```

## 4. 窗口尺寸

- 默认宽度：720px（含长 URL 的界面）
- 最小宽度：560px
- 默认高度：720px
- 最小高度：540px
- 窗口启动位置：`CenterOwner`

## 5. 对话框

不要引入 `MessageBox.Avalonia`（版本兼容问题），自己写简易 Window：

```csharp
var dialog = new Window
{
    Title = "提示",
    Width = 400,
    Height = 200,
    WindowStartupLocation = WindowStartupLocation.CenterOwner,
    CanResize = false,
};
// ... 添加内容 ...
await dialog.ShowDialog(ownerWindow);
```

**注意：** `TaskCompletionSource` 用 `TrySetResult` 而非 `SetResult`，防止重复设置抛异常。

## 6. 暗色主题测试

每次 UI 改动后，**必须**切换到暗色主题验证：
- 文字是否可见（对比度）
- 边框是否可见
- 按钮状态是否正常（hover/pressed）
- 滚动条是否正常

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2026-06-17 | 初始版本：从 Host Channel 重构中提炼 |
| 2026-06-24 | 新增 §2.6 `AutoCompleteBox` 约定 + 内层 TextBox `VerticalContentAlignment` 样式穿透写法 |
| 2026-06-25 | 补充模板/Host消息/子场景 AutoCompleteBox 使用 `Classes="templatePicker"` 做点击/聚焦自动全选，并强制输入时展开过滤清单的约定 |
