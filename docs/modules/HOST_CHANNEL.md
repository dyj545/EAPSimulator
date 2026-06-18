# Host 通道重构开发文档

> 本文档记录 Config 界面 Host 通道部分的重构过程、设计决策、踩过的坑、以及后续待办。
> 主要文件：
> - 视图：[`src/EAPSimulator.UI/Views/ConfigView.axaml`](../src/EAPSimulator.UI/Views/ConfigView.axaml)
> - ViewModel：[`src/EAPSimulator.UI/ViewModels/ConfigViewModel.cs`](../src/EAPSimulator.UI/ViewModels/ConfigViewModel.cs)
> - 模型：[`src/EAPSimulator.Core/Protocols/HostProtocol/HostChannelConfig.cs`](../src/EAPSimulator.Core/Protocols/HostProtocol/HostChannelConfig.cs)
> - 传输层：[`src/EAPSimulator.Core/Protocols/HostProtocol/IHostTransport.cs`](../src/EAPSimulator.Core/Protocols/HostProtocol/IHostTransport.cs)
> - 窗口：[`src/EAPSimulator.UI/ConfigWindow.axaml`](../src/EAPSimulator.UI/ConfigWindow.axaml)
> - 全局样式：[`src/EAPSimulator.UI/App.axaml`](../src/EAPSimulator.UI/App.axaml)

---

## 一、背景

Config 界面里的 "Host 通道"是用来配置 EAPSimulator 与外部系统（MES / RMS / WMS / 其他 IT 系统）通讯的连接，每个通道一个独立的传输实例，可以独立连接 / 断开 / 发消息。

支持的协议：`HttpPost` / `Tcp` / `Mqtt` / `Kafka` / `RabbitMq` / `ActiveMq` / `Grpc` / `OpcUa`。

**重构前的问题：**
- UI 字段散乱，所有协议共用一组字段（URL、RemoteHost、RemotePort、ContentType...），但实际每个协议需要的字段集完全不同
- 比如 MQTT 实际需要 broker / topic / clientId，但界面只暴露 URL+Port
- 字段冗余：URL 已含端口，又单独要求填端口
- 概念混淆：Active 模式只对 TCP 有意义，HTTP/MQTT 没这个概念
- 没有连接状态反馈、没有输入验证
- 配置和运行混在一起，已连接时仍可改配置（修改半截会搞坏正在跑的连接）
- 暗色主题下控件颜色硬编码，文字看不见
- 弹窗默认 480px 太窄，长 URL 把按钮挤掉

---

## 二、整体设计思路

把"通道"分两个状态：**头部（始终可见）** + **详细配置（折叠/展开）**。

```
┌──────────────────────────────────────────────────────────┐
│ ▶ ●  MES · HttpPost · http://...api/mes   未连接 [连接][✕] │  ← 头部
└──────────────────────────────────────────────────────────┘
（点 ▶ 展开后才显示详细配置，折叠态界面更清爽）

┌──────────────────────────────────────────────────────────┐
│ ▼ ●  MES · HttpPost · ...                未连接 [连接][✕] │
├──────────────────────────────────────────────────────────┤
│ ⚠ 通道已连接，断开后才能修改配置（仅 IsConnected 时显示） │
│                                                          │
│ 名称       [MES                ]                         │
│ 协议       [HttpPost ▾]    Body 格式 [Json ▾]            │
│ ─────────────────────────────                            │
│ HTTP 配置                                                │
│   URL          [http://...                  ]            │
│   Content-Type [application/json ▾]                      │
└──────────────────────────────────────────────────────────┘
```

**核心规则：**
- 默认折叠，列表清爽
- 头部一行显示关键信息：状态点 + 名称 + 协议 + 端点摘要 + 状态文字 + 操作按钮
- 已连接时**禁止编辑**配置（`IsEnabled="{Binding !IsConnected}"`），并显示提示条
- 详细配置按协议拆分，**只显示当前协议需要的字段**

---

## 三、分步实施记录

重构按三步走，每步可独立运行查看效果：

### 第 1 步 ✅ 折叠 / 展开 + 头部摘要
- 加 `IsExpanded` 属性 + `ToggleExpandCommand`
- 加 `EndpointSummary` 计算属性，根据协议显示不同摘要
- 头部布局用 Grid 而非 StackPanel.Horizontal（防止长 URL 把后续元素挤掉）
- 已连接时整块禁用，显示提示条

### 第 2 步 ✅ 按协议拆分配置表单
- 在 `HostChannelConfig` / `HostChannelViewModel` 中补齐每个协议的字段
- 加 8 个布尔属性 (`IsHttp` / `IsTcp` / `IsMqtt` / ...) 给 XAML 用 `IsVisible` 绑定
- 每个协议一个独立 `StackPanel`，切换协议时只显示对应字段

### 第 2.5 步 ✅ 布局美化（用户反馈）
- 短字段（端口、ID）并列两列，长字段（URL、Topic）独占一行
- 统一所有 section 的标签宽度（HSMS = 100、Timeout = 140、协议特有 = 100）
- 窗口默认尺寸 480 → 720，最小宽度 400 → 560
- 头部用 Grid 而非 StackPanel.Horizontal

### 第 3 步 ⏳ 待办
- 输入验证（IP 格式、端口范围、URL 格式）
- 测试连接按钮（实际发请求验证配置）
- 运行统计（发送/接收计数、最近延迟、最后错误）

---

## 四、关键设计决策

### 1. ViewModel 中字段集中维护

`HostChannelViewModel` 一个类持有所有协议的字段。**没有按协议拆成多个子 ViewModel**，原因：

- 字段总数 30+ 但单个简单
- 拆 8 个子 VM + Switch ContentControl 反而增加复杂度
- 切换协议时不需要丢失其他协议的配置（用户切回去时还在）
- 持久化到 JSON 时字段全保留即可

### 2. 协议判断用多个 bool 属性

```csharp
public bool IsHttp     => TransportType == "HttpPost";
public bool IsTcp      => TransportType == "Tcp";
public bool IsMqtt     => TransportType == "Mqtt";
// ...
```

XAML 直接用 `IsVisible="{Binding IsMqtt}"`，比写 Converter 简单。
切协议时在 `OnTransportTypeChanged` 里手动 `OnPropertyChanged` 触发刷新。

### 3. 端点摘要用 switch 表达式

不同协议显示不同摘要：

```csharp
public string EndpointSummary => TransportType switch
{
    "HttpPost"  => string.IsNullOrWhiteSpace(HttpUrl) ? "(未配置)" : HttpUrl,
    "Tcp"       => $"{RemoteHost}:{RemotePort}",
    "Mqtt"      => $"{MqttBroker}:{MqttPort} / {MqttTopic}",
    // ...
};
```

依赖字段任一变更都会触发 `OnPropertyChanged(nameof(EndpointSummary))`：

```csharp
partial void OnHttpUrlChanged(string value)    => OnPropertyChanged(nameof(EndpointSummary));
partial void OnRemoteHostChanged(string value) => OnPropertyChanged(nameof(EndpointSummary));
// ...
```

### 4. 已连接时锁定配置

```xaml
<StackPanel IsEnabled="{Binding !IsConnected}">
    <Border IsVisible="{Binding IsConnected}">
        <TextBlock Text="⚠ 通道已连接，断开后才能修改配置"/>
    </Border>
    <!-- 字段们 -->
</StackPanel>
```

### 5. 布局统一规则

| 字段类型 | 布局 |
|---|---|
| 长内容（URL、Topic、配置文件路径） | 独占一行，TextBox 拉满 |
| 短内容成对（IP+Port、用户名+密码） | 两列并排 |
| 标签宽度 | 100（协议特有字段）/ 140（超时设置）/ 160（HSMS 大字段） |
| 输入框高度 | 32（紧凑）/ 36（主要） |
| 间距 | StackPanel.Spacing=10~12，行内 Margin=0,8 |

**短字段两列布局公式：**

```xaml
<Grid ColumnDefinitions="100,*,16,80,*">
    <TextBlock Grid.Column="0" Text="主机"/>
    <TextBox   Grid.Column="1" Text="{Binding Host}"/>
    <!-- Column 2 是 16 像素间隙 -->
    <TextBlock Grid.Column="3" Text="端口"/>
    <TextBox   Grid.Column="4" Text="{Binding Port}"/>
</Grid>
```

### 6. 暗色主题兼容

不要硬编码颜色（`#FAFAFA`、`#F5F5F5`、`#E3F2FD`）。用主题资源：

```xaml
<Style Selector="Border.section-card">
    <Setter Property="BorderBrush" Value="{DynamicResource SystemControlForegroundListLowBrush}"/>
    <!-- 不设 Background，让它继承主题背景 -->
</Style>

<Style Selector="Border.hint-box">
    <Setter Property="Background" Value="{DynamicResource SystemControlBackgroundListLowBrush}"/>
</Style>
```

需要色彩对比的提示框（如成功 / 警告）可以保留品牌色边框 + 主题感知背景：

```xaml
<Style Selector="Border.success-box">
    <Setter Property="Background" Value="{DynamicResource SystemControlBackgroundListLowBrush}"/>
    <Setter Property="BorderBrush" Value="#4CAF50"/>
    <Setter Property="BorderThickness" Value="1"/>
</Style>
```

---

## 五、踩过的坑

### 坑 1：StackPanel 不限制子元素宽度

**症状：** 头部摘要用 `StackPanel Orientation="Horizontal"` 放 5 个 TextBlock，长 URL 不会被 `TextTrimming="CharacterEllipsis"` 截断，反而把后续按钮挤出可视区。

**原因：** StackPanel 不限制子元素宽度，子项按内容尺寸排列。

**修法：** 头部容器用 Grid，让端点摘要列吃 `*` 列：

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

### 坑 2：TaskCompletionSource 重复 SetResult

**症状：** `System.InvalidOperationException: An attempt was made to transition a task to a final state when it had already completed.`

**原因：** 自定义对话框的 Yes/No 按钮点击后调用了 `dialog.Close()`，触发 `Closed` 事件；事件里又调用 `tcs.SetResult(false)`，但按钮里已经 SetResult 过了。

**修法：** 用 `TrySetResult`，且按钮里**先** SetResult **再** Close：

```csharp
Command = new RelayCommand(() =>
{
    tcs.TrySetResult(true);   // 先设置结果
    dialog.Close();           // 再关闭（即使触发 Closed 也没事，TrySetResult 幂等）
})
```

### 坑 3：MessageBox.Avalonia 12.0 与 Avalonia 11.2 不兼容

**症状：** `NU1605: 检测到包降级`

**原因：** `MessageBox.Avalonia 12.0.0` 依赖 `Avalonia >= 12.0.1`，项目用的是 `Avalonia 11.2.*`。

**修法：**
1. 不引入 `MessageBox.Avalonia`，自己写一个简易 Window 做对话框
2. 或锁定 `MessageBox.Avalonia 11.2.x`（但可能没这个版本）

最终选了方案 1，写在 `ConfigViewModel.ShowMessageAsync` / `ShowConfirmDialogAsync` 里。

### 坑 4：CornerRadius 不能设在 TextBlock

**症状：** `Avalonia error AVLN2000: Unable to resolve suitable regular or attached property CornerRadius on type Avalonia.Controls.TextBlock`

**修法：** 把 TextBlock 包在 Border 里，CornerRadius 设在 Border 上。

### 坑 5：嵌套 Style 选择器语法错误

**症状：** `AVLN2000: Unable to resolve type PointerOver from namespace ...`

**错误写法：**

```xaml
<Style Selector="Button.accent">
    <Style Selector="PointerOver">  <!-- 错 -->
        <Setter Property="Background" Value="..."/>
    </Style>
</Style>
```

**正确写法：** 顶层平铺，用 CSS 风格的伪类：

```xaml
<Style Selector="Button.accent">
    <Setter Property="Background" Value="#1976D2"/>
</Style>
<Style Selector="Button.accent:pointerover">
    <Setter Property="Background" Value="#1565C0"/>
</Style>
```

### 坑 6：HostProtocol 命名空间与类同名

**症状：** `error CS0117: "HostProtocol" 未包含 "TransportType" 的定义`

**原因：** 命名空间是 `EAPSimulator.Core.Protocols.HostProtocol`，里面又有个类叫 `HostProtocol`。代码 `HostProtocol.TransportType.HttpPost` 编译器分不清是访问命名空间下的枚举还是类的成员。

**修法：** 全限定：

```csharp
tt = EAPSimulator.Core.Protocols.HostProtocol.TransportType.HttpPost;
```

### 坑 7：FromModel 时 ContentType 默认值丢失

**症状：** 旧版本持久化的 JSON 里 ContentType 是空字符串，加载后 ComboBox 显示空白。

**修法：** `FromModel` 时兜底：

```csharp
ContentType = string.IsNullOrEmpty(c.ContentType) ? "application/json" : c.ContentType,
```

### 坑 8：ScrollViewer 内子元素宽度不被约束

**项目记忆中已记录** — 见 `memory/feedback_avalonia_scrollviewer_width.md`。
ScrollViewer 内的子元素必须用 Grid 而非 StackPanel 才能正确约束宽度。

---

## 六、文件结构 & 关键代码导览

### HostChannelConfig.cs（持久化模型）

[`src/EAPSimulator.Core/Protocols/HostProtocol/HostChannelConfig.cs`](../src/EAPSimulator.Core/Protocols/HostProtocol/HostChannelConfig.cs)

```csharp
public class HostChannelConfig
{
    // 通用
    public string Name { get; set; }
    public string TransportType { get; set; }
    public bool IsActiveMode { get; set; }
    public string BodyFormat { get; set; }
    public string TemplatePath { get; set; }

    // HTTP
    public string HttpUrl { get; set; }
    public string ContentType { get; set; }
    public Dictionary<string, string> HttpHeaders { get; set; }

    // TCP / 通用 host:port
    public string RemoteHost { get; set; }
    public int RemotePort { get; set; }
    public string LocalHost { get; set; }
    public int LocalPort { get; set; }

    // MQTT / Kafka / RabbitMQ / ActiveMQ / gRPC / OPC UA
    // ... 各协议专属字段

    // 转换为传输层配置
    public HostTransportConfig ToTransportConfig() => ...;
}
```

### HostChannelViewModel（ConfigViewModel.cs 内）

[`src/EAPSimulator.UI/ViewModels/ConfigViewModel.cs:682`](../src/EAPSimulator.UI/ViewModels/ConfigViewModel.cs)

- ObservableProperty 字段对应 `HostChannelConfig` 一一映射
- `IsExpanded` 控制详细配置区显隐
- `IsHttp/IsTcp/...` 协议判断属性
- `EndpointSummary` 头部端点摘要
- `ToModel()` / `FromModel()` 与 `HostChannelConfig` 互转
- `ConnectRequested` / `DisconnectRequested` 事件解耦传输层

### ConfigView.axaml 关键结构

```
ScrollViewer
└─ StackPanel (主容器)
   ├─ Border.section-card  HSMS 连接
   ├─ Border.section-card  超时设置
   ├─ Border.section-card  自定义协议
   └─ Border.section-card  Host 通道
      ├─ Button "+ 添加通道"
      └─ ItemsControl
         └─ DataTemplate
            └─ Border.channel-card
               ├─ Header Grid (始终可见)
               │  ├─ ▶/▼ 切换按钮
               │  ├─ 状态点
               │  ├─ Summary Grid (Name · Type · Endpoint)
               │  ├─ StatusText
               │  └─ Action Buttons (连接/断开/✕)
               └─ Border (IsVisible=IsExpanded)
                  └─ StackPanel (IsEnabled=!IsConnected)
                     ├─ 已连接提示条
                     ├─ 通用字段（名称 / 协议+Body格式）
                     ├─ section-divider
                     └─ 各协议专属字段（用 IsVisible 切换）
```

---

## 七、运行时数据流

```
[ConfigViewModel.HostChannels]
    ↓ ObservableCollection
[XAML ItemsControl 渲染每个 HostChannelViewModel]
    ↓ 用户点击 "连接"
[HostChannelViewModel.ConnectCommand]
    ↓ 触发事件
[ConnectRequested 事件]
    ↓ MainViewModel 订阅处理
[ToModel().ToTransportConfig()]
    ↓ 创建传输实例
[HostTransportFactory.Create() → IHostTransport]
    ↓ 调用
[transport.ConnectAsync()]
    ↓ 成功
[更新 IsConnected / StatusText / StatusColor]
```

---

## 八、待办

### 第 3 步 ✅ 已完成（2026-06-17）

- [x] 输入验证（名称非空、URL 前缀、端口范围、IP 格式）
- [x] 测试连接按钮（`TestConnectionCommand` + `TestConnectionRequested` 事件）
- [x] 验证错误提示（红色背景 + 错误列表）
- [x] 测试结果反馈（成功绿色/失败红色，带 `BoolToBrushConverter`）
- [x] 新增属性：`ValidationErrors`、`IsTesting`、`LastTestResult`、`LastTestSuccess`、`SendCount`、`ReceiveCount`、`LastLatency`、`LastError`

### 后续改进

- [ ] 运行统计（发送/接收计数、最近延迟、最后错误）
- [ ] HTTP Headers 编辑（动态添加 key-value）
- [ ] 通道复制 / 导入 / 导出
- [ ] 模板路径选择（每通道独立模板文件）
- [ ] 日志查看面板（每通道独立日志）

---

## 九、给后续开发者的提示

1. **不要在 Avalonia 里用 WPF 思维做嵌套 Style** — 用 CSS 风格的扁平选择器
2. **暗色主题兼容是默认要求** — 不硬编码颜色，用 `DynamicResource` 主题资源
3. **ScrollViewer 内必须用 Grid** — StackPanel 不约束子元素宽度
4. **TaskCompletionSource 用 TrySetResult** — 防止重复 SetResult 抛异常
5. **MessageBox.Avalonia 版本要严格匹配 Avalonia 版本** — 否则 NU1605 包降级错误
6. **协议判断不要用 string 比较散落各处** — 集中在 ViewModel 的 `IsXxx` 属性，XAML 直接绑定
7. **配置类字段冗长时不要拆 ViewModel** — 一个类持有所有字段，靠 `IsVisible` 控制显隐反而更简单
8. **窗口默认宽度** — 含 Host 通道这种含长 URL 的，最少 720px

---

## 十、变更记录

| 日期 | 内容 |
|---|---|
| 2026-06-17 | 初版 — 折叠/展开 + 头部摘要 + 按协议拆分表单 + 布局规整 |
| 2026-06-18 | Host 通道连接状态在主界面统一展示：顶部工具栏紧凑灯条 + 右侧 StatusPanel "Host Channels" 卡片（带 Connect/Disconnect 按钮）；统一错误红到 `#D32F2F` |
| 2026-06-18 | 实现 Host 通道"测试连接"功能：MainViewModel 订阅 `TestConnectionRequested`，临时建立 HostProtocol 验证后立即断开（5s 超时） |
| 2026-06-18 | HTTP 通道支持自定义 Header（Authorization / X-Api-Key 等鉴权头），Config 界面 HTTP 区块加 Headers 表格；Core 层既有 `HttpHeaders` 字段一直可用，本轮只暴露 UI |
| 2026-06-18 | HTTP 通道闭环：测试连接改为真发 OPTIONS/HEAD/GET 探测请求（带 token、识别 401/403/5xx）；Listener Passive 模式校验 `Authorization` 头，不匹配返 401；Config 界面 HTTP 区块加主动/被动模式切换 + 期望 Authorization 输入框（仅 Passive 可见） |
| 2026-06-18 | MQTT 补齐 username/password（既有字段从未传给 broker，认证 broker 连不上）；8 个 transport SendAsync 失败时主动触发 Disconnected 事件，UI 灯立即变红，不再要等到下一次发送才暴露掉线 |

---

> 复盘心得：开发过程中曾过度关注样式美化（颜色、间距、圆角）而忽略了**功能逻辑的合理性**——
> 比如让 MQTT 协议下还显示 URL 和 Content-Type、有 URL 又有单独端口字段冗余、Active 模式对 HTTP 没意义等。
> 重构的核心思路应该先理顺**这个配置项有什么用**、**用户怎么知道配对了**、**配置怎么生效**，
> 再考虑界面美观。后续做任何 UI 改动，先回答这三个问题。
