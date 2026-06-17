# 跨模块经验教训

> 本文档记录跨模块的通用踩坑经验，避免在不同模块里重复踩同样的坑。

## 1. Avalonia 相关

### 1.1 ScrollViewer 内子元素宽度不被约束

**症状：** ScrollViewer 内的长文本或宽控件超出可视区，但滚动条不出现。

**原因：** ScrollViewer 用 StackPanel 布局时，子元素按内容尺寸排列，不会受到 ScrollViewer 宽度约束。

**解决：** ScrollViewer 内第一层必须用 Grid，Grid 会约束子元素宽度。

```xaml
<!-- 错 -->
<ScrollViewer>
    <StackPanel>
        <TextBlock Text="Very long text..."/>
    </StackPanel>
</ScrollViewer>

<!-- 对 -->
<ScrollViewer>
    <Grid>
        <StackPanel>
            <TextBlock Text="Very long text..." TextWrapping="Wrap"/>
        </StackPanel>
    </Grid>
</ScrollViewer>
```

### 1.2 嵌套 Style 选择器语法错误

**症状：** `AVLN2000: Unable to resolve type PointerOver from namespace ...`

**原因：** Avalonia 不支持 WPF 的嵌套 Style 语法。

**解决：** 用 CSS 风格的扁平选择器：

```xaml
<!-- 错 -->
<Style Selector="Button.accent">
    <Style Selector="PointerOver">
        <Setter Property="Background" Value="..."/>
    </Style>
</Style>

<!-- 对 -->
<Style Selector="Button.accent">
    <Setter Property="Background" Value="#1976D2"/>
</Style>
<Style Selector="Button.accent:pointerover">
    <Setter Property="Background" Value="#1565C0"/>
</Style>
```

### 1.3 CornerRadius 不能设在 TextBlock

**症状：** `AVLN2000: Unable to resolve suitable regular or attached property CornerRadius on type Avalonia.Controls.TextBlock`

**解决：** 把 TextBlock 包在 Border 里，CornerRadius 设在 Border 上。

### 1.4 暗色主题兼容

**原则：** 不要硬编码颜色（`#FAFAFA`、`#F5F5F5`、`#E3F2FD`），用 `DynamicResource` 主题资源。

**可用资源：**
- `SystemControlBackgroundListLowBrush` — 卡片背景
- `SystemControlForegroundListLowBrush` — 卡片边框
- `SystemControlForegroundBaseHighBrush` — 主要文字
- `SystemControlForegroundBaseMediumBrush` — 次要文字

**品牌色可硬编码：** 成功 `#4CAF50`、警告 `#F57C00`、错误 `#D32F2F`、主色 `#1976D2`

### 1.5 MessageBox.Avalonia 版本兼容

**症状：** `NU1605: 检测到包降级`

**原因：** `MessageBox.Avalonia 12.0.0` 依赖 `Avalonia >= 12.0.1`，项目用的是 `Avalonia 11.2.*`。

**解决：** 不引入 `MessageBox.Avalonia`，自己写简易 Window 做对话框。

### 1.6 TaskCompletionSource 重复 SetResult

**症状：** `System.InvalidOperationException: An attempt was made to transition a task to a final state when it had already completed.`

**原因：** 对话框的按钮点击后调用 `dialog.Close()`，触发 `Closed` 事件；事件里又调用 `tcs.SetResult(false)`，但按钮里已经 SetResult 过了。

**解决：** 用 `TrySetResult`，且按钮里**先** SetResult **再** Close：

```csharp
Command = new RelayCommand(() =>
{
    tcs.TrySetResult(true);   // 先设置结果
    dialog.Close();           // 再关闭（即使触发 Closed 也没事，TrySetResult 幂等）
})
```

## 2. .NET / C# 相关

### 2.1 命名空间与类同名

**症状：** `error CS0117: "HostProtocol" 未包含 "TransportType" 的定义`

**原因：** 命名空间是 `EAPSimulator.Core.Protocols.HostProtocol`，里面又有个类叫 `HostProtocol`。代码 `HostProtocol.TransportType.HttpPost` 编译器分不清是访问命名空间下的枚举还是类的成员。

**解决：** 全限定：

```csharp
tt = EAPSimulator.Core.Protocols.HostProtocol.TransportType.HttpPost;
```

### 2.2 CancellationToken 的传递

**症状：** `ObjectDisposedException: The CancellationTokenSource has been disposed.`

**原因：** `_cts` 可能在 `Stop()` 时被替换或 dispose，但正在执行的步骤还持有旧 token。

**解决：** 每次访问前先快照：

```csharp
var token = _cts?.Token ?? CancellationToken.None;
```

### 2.3 异步事件的处理

**症状：** 事件处理器里的异步操作没有被 await，异常被吞掉。

**解决：** 用 `Task.Run()` 包装，但要注意异常处理和日志记录：

```csharp
private void OnMessageReceived(object sender, EventArgs e)
{
    _ = Task.Run(async () =>
    {
        try
        {
            await HandleMessageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle message");
        }
    });
}
```

## 3. 架构设计相关

### 3.1 配置类字段冗长时不要拆 ViewModel

**场景：** `HostChannelViewModel` 有 30+ 字段（每个协议一套）。

**错误做法：** 拆 8 个子 ViewModel（HttpViewModel / TcpViewModel / ...）+ Switch ContentControl。

**正确做法：** 一个类持有所有字段，靠 `IsVisible` 控制显隐。

**理由：**
- 字段总数 30+ 但单个简单
- 拆 8 个子 VM 反而增加复杂度
- 切换协议时不需要丢失其他协议的配置（用户切回去时还在）
- 持久化到 JSON 时字段全保留即可

### 3.2 协议判断用多个 bool 属性

**场景：** XAML 需要根据协议类型显示不同字段。

**错误做法：** 写 Converter（`ProtocolToVisibilityConverter`）。

**正确做法：** ViewModel 里加多个 bool 属性：

```csharp
public bool IsHttp => TransportType == "HttpPost";
public bool IsTcp => TransportType == "Tcp";
public bool IsMqtt => TransportType == "Mqtt";
```

XAML 直接用 `IsVisible="{Binding IsMqtt}"`，比写 Converter 简单。

**注意：** 切协议时在 `OnTransportTypeChanged` 里手动 `OnPropertyChanged` 触发刷新：

```csharp
partial void OnTransportTypeChanged(string value)
{
    OnPropertyChanged(nameof(IsHttp));
    OnPropertyChanged(nameof(IsTcp));
    OnPropertyChanged(nameof(IsMqtt));
    // ...
}
```

### 3.3 已连接时锁定配置

**场景：** 通道已连接时，用户仍可修改配置（修改半截会搞坏正在跑的连接）。

**解决：**

```xaml
<StackPanel IsEnabled="{Binding !IsConnected}">
    <Border IsVisible="{Binding IsConnected}">
        <TextBlock Text="⚠ 通道已连接，断开后才能修改配置"/>
    </Border>
    <!-- 字段们 -->
</StackPanel>
```

### 3.4 端点摘要用 switch 表达式

**场景：** 不同协议显示不同的端点摘要（HTTP 显示 URL，TCP 显示 host:port，MQTT 显示 broker/topic）。

**解决：**

```csharp
public string EndpointSummary => TransportType switch
{
    "HttpPost" => string.IsNullOrWhiteSpace(HttpUrl) ? "(未配置)" : HttpUrl,
    "Tcp" => $"{RemoteHost}:{RemotePort}",
    "Mqtt" => $"{MqttBroker}:{MqttPort} / {MqttTopic}",
    // ...
};
```

依赖字段任一变更都会触发 `OnPropertyChanged(nameof(EndpointSummary))`：

```csharp
partial void OnHttpUrlChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
partial void OnRemoteHostChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
// ...
```

## 4. 布局相关

### 4.1 短字段并列两列

**场景：** 端口、ID 等短字段独占一行浪费空间。

**解决：** 用 5 列 Grid 并列：

```xaml
<Grid ColumnDefinitions="100,*,16,80,*">
    <TextBlock Grid.Column="0" Text="主机"/>
    <TextBox   Grid.Column="1" Text="{Binding Host}"/>
    <!-- Column 2 是 16 像素间隙 -->
    <TextBlock Grid.Column="3" Text="端口"/>
    <TextBox   Grid.Column="4" Text="{Binding Port}"/>
</Grid>
```

### 4.2 标签宽度统一

| 字段类型 | 标签宽度 |
|---|---|
| 短标签（2-4 字） | 80-100px |
| 中标签（5-8 字） | 120-140px |
| 长标签（9 字以上） | 160px |

### 4.3 头部摘要用 Grid 而非 StackPanel

**场景：** 头部显示"名称 · 协议 · 端点"，长 URL 会把后续按钮挤出可视区。

**原因：** StackPanel 不限制子元素宽度，子项按内容尺寸排列。

**解决：** 用 Grid，让端点摘要列吃 `*` 列：

```xaml
<Grid ColumnDefinitions="Auto,Auto,Auto,Auto,*">
    <TextBlock Grid.Column="0" Text="{Binding Name}"/>
    <TextBlock Grid.Column="1" Text="·"/>
    <TextBlock Grid.Column="2" Text="{Binding TransportType}"/>
    <TextBlock Grid.Column="3" Text="·"/>
    <TextBlock Grid.Column="4" Text="{Binding EndpointSummary}"
               TextTrimming="CharacterEllipsis"/>
</Grid>
```

## 5. 变更记录

| 日期 | 内容 |
|---|---|
| 2026-06-17 | 初始版本：从各模块踩坑记录中提炼 |
