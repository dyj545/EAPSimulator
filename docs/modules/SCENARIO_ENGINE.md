# 场景引擎模块

## 1. 概述

场景引擎（ScenarioEngine）是 EAPSimulator 的**自动应答核心**，驱动用户编写的场景脚本，实现"收到消息 A → 自动回复消息 B → 等待消息 C → ..."的自动化流程。

**核心能力：**
- 脚本化的场景定义（JSON）
- 支持多种步骤类型：Send / Receive / Reply / Delay / Log / Branch / HostSend / HostReceive
- 支持多通道 Host 消息路由
- 支持运行/停止/单步执行
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

**向后兼容**：`FieldCondition` 同时保留旧的 `Path/Operator/Value` 三件套。UI 里通过 `ƒx` 按钮在两种模式间切换；JSON 反序列化时，`expression` 字段为空就回退到旧字段。

### 4.8 流程图视图

中间列右上角的 `📋 列表` / `🌐 流程图` 按钮在两种视图间切换；默认显示流程图。

**结构**：

| 文件 | 职责 |
|---|---|
| `Core/.../ScenarioFlowLayout.cs` | 纯数据布局：扫描 Steps 产出 Nodes + Edges，无 UI 依赖；可单测 |
| `Core/.../ScenarioModels.cs::ScenarioFlowPersistedLayout` | JSON 持久化结构（每步的 X/Y 覆写） |
| `UI/Controls/ScenarioFlowCanvas.cs` | 渲染节点 + 连线 + 拖动；监听 Steps 集合与拓扑字段变化自动重建 |

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
