# 场景引擎模块

## 1. 概述

场景引擎（ScenarioEngine）是 EAPSimulator 的**自动应答核心**，驱动用户编写的场景脚本，实现"收到消息 A → 自动回复消息 B → 等待消息 C → ..."的自动化流程。

**核心能力：**
- 脚本化的场景定义（JSON）
- 支持多种步骤类型：Send / Receive / Reply / Delay / Log / Branch / HostSend / HostReceive / SetVariable / Loop / ForEach / CallScenario
- 支持多通道 Host 消息路由
- 支持手动运行、连接后自动启动、按入站消息触发
- 支持流程图编辑、断点、暂停、单步和变量观察
- 与 SECS 消息路由集成（实现 `ISecsMessageHandler`）

## 2. 关键设计决策

### 2.1 为什么叫"场景"而不是"脚本"

- "脚本"暗示图灵完备的编程语言
- "场景"强调**声明式**：用户定义"什么情况下做什么"，而不是"怎么一步步做"
- 场景是 JSON 结构，不是代码，降低使用门槛

### 2.2 步骤类型设计

| 步骤类型 | 作用 | 阻塞？ |
|---|---|---|
| Send | 发送 SECS 消息 | 否 |
| Receive | 等待特定 SECS 消息 | 是（直到匹配或超时） |
| Reply | 用最后收到的消息构建回复 | 否 |
| Delay | 等待 N 毫秒 | 是 |
| Log | 记录日志 | 否 |
| Branch | 根据条件跳转 | 否 |
| HostSend | 向 Host 通道发送消息 | 否 |
| HostReceive | 等待 Host 通道消息 | 是 |
| SetVariable | 给变量赋值（字面量或上一条消息字段） | 否 |
| Loop | 循环开始（与 EndLoop 用同 LoopId 配对） | 否 |
| EndLoop | 循环结束，回跳到 Loop 后第 1 步 | 否 |
| ForEach | 遍历列表（SECS子项 / Host数组 / 变量分隔），与 EndForEach 用同 ForEachId 配对 | 否 |
| EndForEach | 遍历结束，下一项继续或跳出 | 否 |
| CallScenario | 把另一个场景作为子例程嵌入运行 | 否 |

### 2.3 多通道 Host 消息路由

```csharp
// 按通道名注册
_hostSends["MES"] = mesSendFunc;
_hostSends["RMS"] = rmsSendFunc;

// 步骤指定通道名
{ "type": "HostSend", "channel": "MES", "template": "..." }
```

- 空字符串 `""` 是默认通道（向后兼容）
- 步骤省略 `channel` 字段时使用默认通道
- 每个通道有独立的 `Channel<HostMessage>` 收件箱

### 2.4 Receive 步骤的实现

用 `System.Threading.Channels.Channel<SecsMessage>` 实现异步等待：

```csharp
_inbox = Channel.CreateUnbounded<SecsMessage>();

// Receive 步骤：
var msg = await _inbox.Reader.ReadAsync(ct);  // 阻塞直到有消息
```

- `MessageRouter` 收到 SECS 消息后推入 `_inbox`
- Receive 步骤从 `_inbox` 读取并匹配
- 匹配失败的消息丢弃（或记录日志）

## 3. 文件结构

```
src/EAPSimulator.Core/Protocols/SecsGem/AutoReply/
├── ScenarioEngine.cs           ← 引擎主类
├── ScenarioModels.cs           ← 场景数据模型（ScenarioDefinition / ScenarioStep）
├── FlowLayoutModels.cs         ← 流程图布局坐标
├── AutoReplyHandler.cs         ← ISecsMessageHandler 实现
├── AutoReplyRule.cs            ← 应答规则（Stream/Function 匹配）
── AutoReplyConfig.cs          ← 应答配置
└── MatchUtil.cs                ← 消息字段匹配工具
```

## 4. 数据流

### 4.1 场景运行流程

```
用户点击"运行"
  → ScenarioEngine.RunAsync(scenario)
  → 创建 CancellationTokenSource
  → 遍历步骤列表
  → 对每个步骤：
      → StepStarted 事件（UI 高亮当前步骤）
      → 执行步骤逻辑
      → StepCompleted 事件（UI 标记完成）
  → ScenarioFinished 事件
```

### 4.2 Receive 步骤的匹配

```
_inbox.Reader.ReadAsync()  ← 阻塞
  → 收到 SecsMessage
  → MatchUtil.Match(message, step.Conditions)
  → 匹配成功：继续下一步
  → 匹配失败：记录日志，继续等待（或超时）
```

### 4.3 Branch 步骤

```json
{
  "type": "Branch",
  "cases": [
    {
      "conditions": [
        { "expression": "num(secs[\"1/0\"]) > 10 && contains(vars[\"lot\"], \"WAFER\")" }
      ],
      "targetLabel": "big"
    }
  ],
  "defaultLabel": "small"
}
```

- 每个 `case` 的 conditions 是 AND 关系；首个命中的跳到对应 Label
- Condition 支持两种格式：表达式（`expression` 非空时优先）或旧的 `path/operator/value` 三件套
- `LastSource` 枚举跟踪最后收到的是 SECS 还是 Host 消息，决定字段访问的对象

### 4.4 变量与模板渲染

`ScenarioVariables` 是引擎内一份全局的字符串字典，每次 `Start()` 重建。

- **赋值**：`SetVariable` 步骤；来源 `Literal` / `LastSecsField` / `LastHostField`
- **引用**：在 `SecsMessageTemplate.ItemXml`、`HostField.Value`、`HostMessage.RawBody`、`Log.Message` 中用 `${name}` 占位符
- **未定义变量**：保留原样（便于调试），不静默清空

### 4.5 循环（Loop / EndLoop）

`Loop` 与 `EndLoop` 通过 `LoopId` 配对。引擎在每个执行帧上维护一个循环栈（嵌套时多个循环同时存在）。每轮循环把当前迭代号写入 `${$loop.<LoopId>.i}`（从 1 开始），可在循环体内的模板里引用。

```
[Loop L1 ×3] ─┐
   Send ${tag}-${$loop.L1.i}
[EndLoop L1] ─┘   → 产生 3 条消息：tag-1 / tag-2 / tag-3
```

**循环条件 (LoopWhile)**：可在 `Loop` 上填写布尔表达式（见 4.7）。进入循环前和每轮结束后各评估一次；为 false 即退出。表达式里 `loop["LoopId"]` 始终是即将执行的迭代号（1, 2, 3 …）。

- `LoopTimes > 0` 时优先按次数；`LoopTimes = 0` 且 `LoopWhile` 非空时按表达式控制；都不填则无限循环。
- 表达式语法错误会被记录到日志并视为 false（安全退出），不会让整个场景失败。

### 4.6 子场景（CallScenario）

引擎用一份 `Stack<Frame>` 跑场景；`CallScenario` 步骤把目标场景作为新帧压栈，子场景执行完弹出，父场景从下一步继续。变量在父子场景间**共享**（即子场景里 `SetVariable` 影响父）。最大递归深度 16，超过抛错。子场景的 `Role` 与当前协议不兼容会阻止调用。

### 4.6.1 遍历（ForEach / EndForEach）

`ForEach` 与 `EndForEach` 通过 `ForEachId` 配对，与 Loop 同样支持嵌套。集合在**进入**循环时一次性物化，运行期间源数据被改也不影响迭代序列。

| ForEachSource | ForEachPath 含义 | 物化方式 |
|---|---|---|
| `SecsList` | 上一条 SECS 消息的路径（如 `1/0`，空 = 根） | 取 `SecsList.Items` 的字符串值；非 List 节点视为 1 元素 |
| `HostArrayList` | 上一条 Host 消息的字段名 | 取 `field.Children` 的 `Value`；叶子非空时视为 1 元素 |
| `Variable` | 变量名（不带 `${}`） | 用 `ForEachSeparator`（默认 `,`）切分；为空 = 0 元素 |

每轮迭代写入：

- `${$foreach.<Id>.item}` — 当前项
- `${$foreach.<Id>.index}` — 0 基下标
- `${<ForEachItemVariable>}` — 可选别名（非空才设置）
- 表达式上下文：`foreach["<Id>"]` / `foreachIndex["<Id>"]`

空集合会跳过整段 body 并打印 `foreach <Id> skipped (0 items)`。

### 4.6.2 错误分支（OnErrorLabel）

任何步骤都可以填 `OnErrorLabel`。一旦步骤抛异常（包括 Receive/HostReceive 在 `OnTimeout=Fail` 下的 `TimeoutException`），引擎不再 abort 场景，而是：

1. 把异常信息写到三个变量：`$error.message` / `$error.kind` / `$error.step`
2. 跳到该 Label 对应的步骤继续执行

```
[Receive S1F1, timeout=2s, OnError=rescue]
[Send ok]
...
[Label: rescue]
[Log "${$error.kind}: ${$error.message} (step ${$error.step})"]
```

注意点：

- `OnErrorLabel` **找不到对应 Label** 时，引擎仍按旧行为 Failed 终止，不会吞掉原异常——配置错本身比脚本运行错更值得暴露
- 用户主动 Stop 触发的 `OperationCanceledException` 不走错误分支
- `Skip` / `Continue` 仍是 Receive/HostReceive 的"软失败"语义，与 `OnErrorLabel` 不冲突

### 4.6.3 按入站消息触发场景（TriggerOnMessage）

除手动 ▶ 运行和 `AutoStart`（连接即跑）之外，第三条启动入口：每个场景可以勾选"按入站消息触发"，并配 `TriggerStream/TriggerFunction/TriggerConditions`。引擎**空闲**时收到第一条命中的 SECS 消息会自动 `Start` 该场景，并把那条消息塞进新场景的 `_inbox`——首个 Receive 步骤会立即拿到它。

字段（`ScenarioDefinition`）：

| 字段 | 含义 |
|---|---|
| `TriggerOnMessage` | 总开关；false 时其余字段被忽略 |
| `TriggerStream` / `TriggerFunction` | 必须匹配的 (S,F)；不支持 any |
| `TriggerConditions` | 复用 `FieldCondition`（同 Receive 的条件，支持表达式模式）；典型用法：传统模式 `path=1, operator===, value=101` 或表达式 `secs["1"] == "101"` 表达 CEID 等值 |

实现要点（`ScenarioEngine.HandleAsync` + `TryStartTriggeredScenario`）：

- **只在 `_inbox == null`（引擎空闲）时扫描**；正在运行的场景独占引擎，触发列表此刻完全不看
- 按 `SetTriggerScenarios` 传入的顺序遍历，**首个命中**就 Start 并返回——同一条消息最多触发一个场景
- Role 不匹配（如 EAP 文件加载到 Equipment 侧）跳过，避免方向反了的脚本被误启动
- Start 后立刻把触发消息写入新 `_inbox`，**首个 Receive 步骤无需重做条件匹配也能拿到** `_lastReceived`

#### 与 Receive 步骤的关系

两层独立机制，但因为触发消息会"流"进 inbox，存在配置耦合：

| 配置 | 结果 |
|---|---|
| 触发条件 + 首步 `Recv any` | ✅ 推荐写法：触发器做路由，Receive 只负责接信 |
| 触发条件 + 首步 `Recv` 同样条件 | ✅ 工作但条件被检查两次（多余不冲突） |
| 触发条件 + 首步 `Recv` **不同**条件 | ⚠️ 触发消息进 inbox 但被 Receive 丢弃，等下一条或超时；配错的典型表现 |
| 触发条件 + 首步直接 `Reply` | ❌ `_lastReceived == null`，Reply 报错走 OnErrorLabel；要么改首步为 Receive，要么别用触发 |

#### 注册时机

`AutoReplyViewModel.ApplyToRouter` 在协议启动时把 `Enabled && TriggerOnMessage && Role 匹配` 的场景一次性注册给引擎，之后改场景需要重连或下次 `Apply` 才生效。AutoStart 和 TriggerOnMessage 互不排斥，但同一个场景两个都勾意义不大——AutoStart 已经在连接时把场景拉起来了，TriggerOnMessage 那段窗口（引擎空闲）几乎不会出现。

#### 路由优先级

`MessageRouter` 的优先级仍是：QuickReply → ScenarioEngine → 内置处理器。TriggerOnMessage 是 ScenarioEngine 里的一个入口，因此如果同一条 S/F 先被无条件 QuickReply 命中并返回回复，触发场景不会看到这条消息。需要场景接管某类消息时，要么禁用同 S/F 的 QuickReply，要么给 QuickReply 加更窄条件，避免抢走场景触发消息。

#### UI 静态校验

`ScenarioViewModel.TriggerWarning / HasTriggerWarning` 计算属性给 UI 一个轻量校验入口：勾选 `TriggerOnMessage` 但首步不是 `Receive`（或步骤为空）时返回一段警告文本，触发面板下方亮一条橙色横幅，场景列表对应项末尾出现 `⚠`（hover 显示完整说明）。校验只是提示，不阻塞保存或运行——引擎仍按原逻辑跑，遇到坑（参见上表"❌"一行）由 OnErrorLabel 兜底或场景失败。失效时机：`Steps` 集合变化、首步 `Kind` 变化、`TriggerOnMessage` 切换。

#### 默认示例

`auto_reply_rules.json` 带一个 Host/EAP 侧示例 `Lot Start`：S6F11 `ProcessStart`（CEID=101，模板路径 `1`）触发场景，首步 Receive 捕获触发消息，随后 Reply S6F12 ACK，再 Send S2F41 START 并等待 W-bit 回复。这个示例用于证明 TriggerOnMessage 的推荐结构：触发器做路由，第一步 Receive 绑定消息，Reply 复用原消息的 SystemBytes。另有 `Lot End` 作为普通手动/可扩展示例，默认未勾 TriggerOnMessage。用户侧操作说明见 [USER_MANUAL.md §3.3](../USER_MANUAL.md#33-按入站消息触发场景) 与 [§4](../USER_MANUAL.md#4-示例processstart-自动处理)。

### 4.7 表达式引擎（ScenarioExpression）

Branch case 条件、Receive/HostReceive 字段条件、Loop 的 `LoopWhile`、AutoReply 规则条件统一走 `ScenarioExpression`，底层是 [DynamicExpresso](https://github.com/davideicardi/DynamicExpresso)。

**沙箱标识符**：

| 名称 | 含义 |
|---|---|
| `vars["name"]` | 场景变量值（缺失返回 `""`） |
| `secs["0/1/2"]` | 最近一条匹配的 SECS 消息中 path 处的字符串值 |
| `host["fieldName"]` / `host.Name` | 最近一条 Host 消息的字段 / 消息名 |
| `loop["LoopId"]` | 该 Loop 当前迭代号（未活跃返回 `"0"`） |
| `num(x)` | 把字符串解析为 double（失败返回 0） |
| `contains(s, t)` / `startsWith` / `endsWith` | 大小写无关字符串匹配 |

**示例**：

```
num(secs["1/0"]) > 10                                  // 数值比较
contains(host["LotID"], "WAFER")                       // 字符串包含
num(vars["count"]) < 5 && host.Name == "MAP_COUNT_REP" // 与/或组合
```

**向后兼容**：`FieldCondition` 同时保留旧的 `Path/Operator/Value` 三件套。UI 里通过 `ƒx` 按钮在两种模式间切换；JSON 反序列化时，`expression` 字段为空就回退到旧字段。**注意（2026-06-24）**：传统模式下的 UI 已经把独立的 `Path` 文本框删除，路径只通过字段下拉（`SelectedFieldOption`）写入 `Path` 属性；模型层 / JSON Schema 不变。参见 §4.10 "编辑器 UI 约定"。

### 4.8 流程图视图

中间列右上角的 `📋 列表` / `🌐 流程图` 按钮在两种视图间切换；默认显示流程图。

**结构**：

| 文件 | 职责 |
|---|---|
| `Core/.../ScenarioFlowLayout.cs` | 纯数据布局：扫描 Steps 产出 Nodes + Edges，无 UI 依赖；可单测 |
| `Core/.../ScenarioModels.cs::ScenarioFlowPersistedLayout` | JSON 持久化结构（每步的 X/Y 覆写） |
| `UI/Controls/ScenarioFlowCanvas.cs` | 渲染节点 + 连线 + 拖动；监听 Steps 集合与拓扑字段变化自动重建 |

**节点几何**：

- 固定宽度 220 px，`MinHeight = 58` px（`ScenarioFlowLayout.NodeHeight`，2026-06-24 由 44 提到 58 让两行内容能不裁切；若详情自然换行需要更高，节点会向下扩展）。
- 节点内容分两行：第 1 行是步骤类型徽标（图标 + Kind，如 `▶ Send` / `⑂ Branch`），加粗、字号 10；第 2 行是详情（模板名 / Label / 条件等），字号 11，`TextWrapping=Wrap` 自动换行。徽标和详情都由 `ScenarioFlowCanvas.SplitKindAndDetail` 从现成的 `DisplayText` 切前两个 token 而来，复用 VM 的格式化逻辑。
- 节点左侧有一个固定宽度 22 px 的步骤索引 TextBlock，方便和列表视图对照。

**边类型与样式**：

| FlowEdgeKind | 何时产生 | 颜色 |
|---|---|---|
| Sequential | 默认顺序流 | 灰 |
| LoopBack | EndLoop → 配对的 Loop 头 | 蓝（贝塞尔回弧） |
| ForEachBack | EndForEach → 配对的 ForEach 头 | 青绿（贝塞尔回弧） |
| BranchCase | Branch 每个 case → Label 节点 | 粉 |
| BranchDefault | Branch 的 defaultLabel | 浅灰 |
| OnError | 任意步骤 OnErrorLabel → Label 节点 | 红，虚线 |

**拖动与持久化**：

- 节点鼠标左键按下 → 选中对应步骤，开始拖动
- 释放后位置写入 `LayoutOverrides`（仅当移动 > 1px），并触发重绘以重路由箭头
- 保存场景时，所有非零位置写入 `ScenarioDefinition.Layout.Nodes`；旧 JSON（无 layout 字段）正常加载
- 工具栏 `↺ 重置布局` 清空 overrides，回到默认列布局

**节点右键菜单**：

- 插入到此之前 / 插入到此之后 → 二级菜单覆盖全部 14 种 step kind
- 删除此步骤
- `LayoutOverrides` 在插入/删除/移动时通过 `ShiftLayoutOverrides` 同步索引，避免拖过的位置跟错节点

**连线编辑（拖箭头改跳转）**：

可编辑边（BranchCase / BranchDefault / OnError）在目标端画一个小圆点 thumb。

- 按住 thumb 拖动 → 出现虚线幽灵线跟随鼠标
- 拖到任意节点 → 该节点高亮橙边；松开即提交
- 拖到空白 → 取消
- 提交规则：BranchCase 改 `Cases[i].TargetLabel`；BranchDefault 改 `DefaultLabel`；OnError 改 `OnErrorLabel`
- 目标节点没有 Label 时自动起一个（`L<idx>`，冲突则递增后缀）
- 顺序边 / Loop/ForEach 回弧没有 thumb（这些拓扑由步骤顺序与 Loop/EndLoop 配对决定，画布上拖没地方写回）

### 4.9 调试器

`ScenarioEngine` 内置断点/暂停/单步能力，UI 在工具栏暴露 ⏸ 暂停 / ▶▶ 继续 / ⏭ 单步 / 🔴 断点 四个按钮。

**核心 API**：

| 方法 | 行为 |
|---|---|
| `Breakpoints` (HashSet&lt;int&gt;) | 步骤索引集合；引擎在每个步骤边界检查 |
| `Pause()` | 请求在下一个步骤边界暂停 |
| `Continue()` | 解除暂停，运行到下一个断点或结束 |
| `StepOver()` | 释放当前暂停 + 在下一步前再次暂停 |
| `Paused` 事件 | 暂停时触发 `(scenario, pc, step)`；UI 据此刷新变量观察 |
| `Resumed` 事件 | 解除暂停时触发 |
| `PausedVariables` | 暂停时的变量快照 |

**实现**：用一个初始 0 permit 的 `SemaphoreSlim` 当信号灯——只在断点/单步/Pause 请求命中时 `WaitAsync` 阻塞，`Continue/StepOver` 各 `Release` 一次。`Stop` 也会 Release 一次让被卡住的步骤观察到取消令牌。

**UI 端**：
- 工具栏 4 个调试按钮 + ⏸ 暂停指示器（绑定 `IsPaused`）
- 步骤 VM 增加 `IsBreakpoint`（**不持久化**）。Run 时同步全集到引擎；运行期间右键菜单/工具栏切换会立即推到引擎
- 流程图节点左上角红点标记断点；暂停时该节点橙色粗边框
- 右侧详情列底部"变量观察"面板（仅暂停时可见），按字典序列出 `${name}` → 值
- 节点右键菜单加"切换断点 🔴"

### 4.10 编辑器 UI 约定（2026-06-24 之后）

下列规约统一覆盖 `AutoReplyView` 的快速回复 + 场景两个 Tab，目的是消除"多个控件指同一个字段"的歧义、把可输入路径压到一条。

**模板 / 子场景 / 消息名选择 — 一律 `AutoCompleteBox` + `FilterMode=Contains`**

涉及 8 处（QuickReply 触发 / QuickReply 回复 / 场景触发 / Send 步骤 / Reply 步骤 / Receive 步骤 / HostSend / HostReceive / CallScenario）。优势：输入"S6"实时列出所有 S6 开头模板，键盘友好，且仍允许直接输入列表里不存在的名字。原来用 `ComboBox` 必须先翻页再选，加之列表长（>120 条）极不友好。

`AutoCompleteBox` 的内层 TextBox 默认贴顶不响应顶层 `VerticalContentAlignment`，因此在 `AutoReplyView.axaml` 顶部加了全局样式 `AutoCompleteBox /template/ TextBox { VerticalContentAlignment=Center }` 一次性穿透模板修正所有实例。

**Label 类跳转 — 一律 `AutoCompleteBox`，去掉"ComboBox + TextBox 双控件"**

覆盖 `OnErrorLabel` / `Branch.DefaultLabel` / `BranchCase.TargetLabel` 三处。之前 ComboBox 显示已有 Label，旁边再放一个 TextBox 允许手动输入；同字段两个控件互相覆盖时容易让用户困惑。改成一个 `AutoCompleteBox` 同时承载两种用法（从列表选 / 直接输入新 Label 名）。

**条件行 — 字段下拉框唯一入口，删 Path TextBox**

QuickReply 规则条件 / 触发条件 / Receive 条件 / Branch case 条件 4 处都曾在字段下拉旁额外放一个 `Path` 文本框。`FieldConditionViewModel.OnSelectedFieldOptionChanged` 已经把选中项的 Path 写回 `Path` 属性，文本框纯属冗余。删除后只剩 `[字段下拉] [操作符] [值]` 三列；切到表达式模式按 `ƒx` 后变成单行表达式输入。

**模板与 Stream/Function 二选一**

QuickReply 触发块和 Receive 步骤之前都在模板选择下方放了 Stream/Function 两个 TextBox 用于手动改。选了模板后 S/F 自动填，再让用户改 S/F 就出现"模板名说 S6F11、S/F 框写着 7/13"的不一致。改成：UI 只暴露模板选择；S/F 仍存在于 VM 模型层（引擎匹配靠它）。极端情况——需要某个 S/F 但库里没对应模板——目前只能先在消息编辑器建一个占位模板，后续若有强需求再加可折叠的"手动覆盖" UI。

`ScenarioStepViewModel.OnTemplateNameChanged` 和 `ScenarioViewModel.OnTriggerTemplateNameChanged` 都有"中间态保护"：用户在 `AutoCompleteBox` 里逐字输入时（`FindTemplateByName` 返回 null）不动 S/F 和 `TemplateFields`，避免被空列表覆盖。2026-06-25 起，新建 `Send` 步骤不再默认填 `TemplateNames.First()`（通常是 S1F1），保持空模板让用户显式选择；`Reply` 仍按最近的 `Receive` 自动推荐 S/F+1 回复模板。

**模板框点击自动全选**

`AutoReplyView.axaml` 中模板 / Host 消息 / 子场景类 `AutoCompleteBox` 标记 `Classes="templatePicker"`，并在样式里设置 `MinimumPrefixLength=0` / `MinimumPopulateDelay=0`。点击/聚焦只做自动全选，不打开下拉；`AutoReplyView.axaml.cs` 只在 `TextInputEvent`（真实键盘文本输入）到达 templatePicker 时标记该控件为 typed，并延后一拍 `IsDropDownOpen=true` 展开过滤结果。不要用 `TextChanged` 作为弹窗依据：切换步骤/场景时绑定会改写 Text，即使控件还有焦点也会误弹其他模板清单。内层 `TextBox` 的 `GotFocusEvent` / `PointerPressedEvent` 会调用 `SelectAll()`，但不再调用 `Focus()`，避免用户点击其他步骤时延迟 Focus 把焦点拉回旧模板框。

**HostReceive 条件直接编辑**

`HostReceive` 步骤详情区现在直接提供 `+ 条件`。传统模式下 `Path` 输入框解释为 Host 字段名（例如 `typeId` / `insResult`），表达式模式的 `ƒx` 由 code-behind 判断当前行位于 `HostReceiveConditions` 区域，自动合成为 `host["field"] == "value"` 而不是 `secs["path"] == "value"`。引擎层本来已支持 `MatchUtil.MatchesCondition(FieldCondition, HostMessage, ScenarioExpression)`，此改动只补 UI 入口。

**子场景下拉 — `ScenarioNames` 集合保持同步**

`AutoReplyViewModel.ScenarioNames` 是给 CallScenario 的 `AutoCompleteBox` 用的字符串集合。`Scenarios.CollectionChanged` + 单个 `ScenarioViewModel.Name` 变更都会调 `RefreshScenarioNames` 重建，保证下拉列表跟场景列表实时一致。

**Host 消息名唯一关键字归一**

`AutoCompleteBox` 的过滤只影响下拉展示，`Text` 绑定仍可能把用户输入的片段（如 `LotEnd`）写回 `ScenarioStepViewModel.HostMessageName`。HostSend 运行时按完整模板名精确查找，因此 `OnHostMessageNameChanged` 会调用 `AutoReplyViewModel.ResolveUniqueHostMessageName`：若输入已精确匹配则保持；若只被一个 `HostMessageNames` 条目包含，则自动替换成完整模板名（例：`LotEnd` → `MESLOTEND_MES_LotEnd`）；若命中多个则不自动猜，继续让用户输入或下拉选择。

**步骤工具栏 — 双行 `WrapPanel`**

`AutoReplyView.axaml` 的步骤工具栏由两个 `WrapPanel` 组成：第 1 行放节点级操作（删除 / ↑ ↓ / 列表-流程图视图切换 / 重置布局），第 2 行是"添加步骤"按钮组（Send / Recv / Reply / Delay / Log / Branch / SetVar / Loop / EndLoop / ForEach / EndForEach / Call）。窗口变窄时每个 WrapPanel 内自动换行，按钮永远不会被横向滚动条遮住。`StackPanel + ScrollViewer` 的老布局只在一行里水平滚，按钮总是有一截被裁掉，弃用。

**QuickReply 规则面板顺序**

`触发消息 → 匹配条件 → 回复模板` 自上而下，把所有"匹配输入"集中在前半，回复定义在后半，符合阅读顺序。

**MoveStepUp / Down 的选中保持**

`AutoReplyViewModel.MoveStepUp/Down` 在 `Steps.Move` 前缓存 `SelectedStep`，移动后再赋回去。`ListBox.SelectedItem` 双向绑定在 `CollectionChanged.Move` 时会瞬时把 `SelectedStep` 置 null，导致连续按 ↑ / ↓ 第二次时按钮的 `CanExecute` 失效。手动恢复一次即可。

## 5. 踩过的坑

### 坑 1：CancellationToken 的传递

`_cts` 可能在 `Stop()` 时被替换或 dispose，但正在执行的步骤还持有旧 token。解决：每次访问前先快照 `var token = _cts?.Token ?? CancellationToken.None`。

### 坑 2：HostReceive 的多通道隔离

最初所有 Host 消息进同一个 inbox，导致通道 A 的消息被通道 B 的 Receive 步骤误消费。解决：`Dictionary<string, Channel<HostMessage>>` 按通道名隔离。

### 坑 3：Branch 条件的解析

最初用 `DataTable.Compute()` 解析表达式，但无法访问 `lastReceived.Stream` 这样的对象属性。曾自己写了简化版字符串字段匹配器（path/operator/value）凑合用了大半年，最终（2026-06-24）切到 `DynamicExpresso.Core` 统一了 Branch / Receive / LoopWhile / AutoReply 的条件语义。解决方案落在 `ScenarioExpression.cs`：用 `Interpreter` + 受限标识符集（`vars/secs/host/loop/num/contains/...`），既拿到 C# 表达式的表达力，又避免暴露 `System.IO` 之类的危险面。

## 6. 待办

- [x] 支持循环步骤（Loop / EndLoop） — 2026-06
- [x] 支持子场景调用（CallScenario） — 2026-06
- [x] 支持变量赋值（SetVariable） + `${var}` 模板渲染 — 2026-06
- [x] 条件表达式改用成熟的解析库（DynamicExpresso） — 2026-06-24
- [x] `LoopWhile` 表达式 — 2026-06-24
- [x] 支持 ForEach 步骤（SECS子项 / Host数组 / 变量分隔） — 2026-06-24
- [x] 异常/超时分支（OnErrorLabel） — 2026-06-24
- [x] 图形化场景编辑器（自动布局 + 拖动 + 联动选中） — 2026-06-24
- [x] 按入站消息触发场景（TriggerOnMessage / 模板 + 条件） — 2026-06-24
- [x] 为 TriggerOnMessage 补充 Core 单测与 Lot Start 示例配置 — 2026-06-25
- [x] 新建 Send 步骤不再默认 S1F1；模板框点击/聚焦自动全选；HostReceive 条件可直接编辑 — 2026-06-25

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2025-04 | 初始版本：Send/Receive/Reply/Delay |
| 2025-05 | 加入 Branch / Log / HostSend |
| 2025-06 | 多通道 Host 消息路由 |
| 2026-06-18 | 加入 SetVariable / Loop / EndLoop / CallScenario；引擎重构为帧栈；新建 EAPSimulator.Core.Tests 工程 |
| 2026-06-24 | 引入 ScenarioExpression（基于 DynamicExpresso）；Branch/Receive/HostReceive/AutoReply 条件支持表达式模式；启用 LoopWhile；UI 上每条条件加 ƒx 模式切换 |
| 2026-06-24 | 加入 ForEach / EndForEach：支持 SECS 列表、Host ArrayList、变量分隔 3 种来源；嵌套与空集合处理；UI 提供专用编辑面板 |
| 2026-06-24 | 每个步骤可声明 OnErrorLabel：步骤抛异常或 Receive 超时(Fail) 时跳到指定 Label，并写入 `$error.message/kind/step` 变量；UI 在步骤详情头部统一加入入口 |
| 2026-06-24 | 加入流程图视图：`ScenarioFlowLayout`（Core，纯数据 + 测试）+ `ScenarioFlowCanvas`（UI，拖动节点 + 自动连线）；中间列加 📋/🌐 切换；节点位置持久化到 `ScenarioDefinition.Layout`，旧文件无 layout 字段照常加载 |
| 2026-06-24 | 流程图连线编辑：BranchCase / BranchDefault / OnError 边末端可拖动 thumb，松开到目标节点即改 TargetLabel / DefaultLabel / OnErrorLabel；自动起 Label 名；FlowEdge 新增 CaseIndex |
| 2026-06-24 | 调试器：ScenarioEngine 增加 Pause/Continue/StepOver/Breakpoints；UI 工具栏 4 个调试按钮 + 流程图节点断点红点 / 暂停橙边框 + 右侧变量观察面板；4 个新测试覆盖断点暂停、单步、变量快照 |
| 2026-06-24 | 按入站消息触发场景：`ScenarioDefinition` 新增 `TriggerOnMessage / TriggerStream / TriggerFunction / TriggerConditions`；`ScenarioEngine.HandleAsync` 在空闲时扫描触发列表，命中即 Start 并把消息注入新 inbox；UI 场景设置加触发面板（模板搜索 AutoCompleteBox + 条件列表）；与现有 AutoStart / 手动运行三入口并存 |
| 2026-06-24 | UI 重构：所有模板/子场景/Host 消息名 ComboBox 改为 `AutoCompleteBox` + `FilterMode=Contains`（8 处）；Label 类跳转去掉旁边的 TextBox 改单 AutoCompleteBox；条件行去掉冗余 Path TextBox；QuickReply 触发块 + Receive 步骤删手动 Stream/Function 框；流程图节点改成两行布局（徽标 + 详情自动换行）、NodeHeight 44→58；步骤工具栏拆双行 WrapPanel；QuickReply 面板顺序改成 触发→条件→回复；MoveStepUp/Down 修复连按时丢选中；新增 `TriggerWarning / HasTriggerWarning` 静态校验 + 触发面板橙色横幅 + 场景列表 ⚠ 标记；详见 §4.10 |
| 2026-06-25 | 补充 TriggerOnMessage 核心单测：命中后自动 Start 并把触发消息送入首个 Receive、条件不命中不启动；`auto_reply_rules.json` 默认示例改为 `Lot Start` / `Lot End`，其中 `Lot Start` 演示 S6F11 ProcessStart(CEID=101) → S6F12 ACK → S2F41 START；新增用户手册 [USER_MANUAL.md](../USER_MANUAL.md) |
| 2026-06-25 | 场景编辑器体验修正：新建 Send 保持空模板（去掉默认 S1F1）；模板/Host消息/子场景 AutoCompleteBox 点击或聚焦自动全选；HostReceive 步骤详情直接支持字段条件与 Host 表达式匹配；Host 消息名支持唯一关键字归一（如 LotEnd → MESLOTEND_MES_LotEnd）；用户手册补充 S10F5→MES→字段匹配→回复机台配置流程 |
