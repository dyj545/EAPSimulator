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
  "condition": "lastReceived.Stream == 1",
  "trueStep": 5,
  "falseStep": 10
}
```

- 条件表达式用简单语法（`lastReceived.Stream == 1`）
- 支持 SECS 和 Host 消息的字段访问
- `LastSource` 枚举跟踪最后收到的是 SECS 还是 Host 消息

## 5. 踩过的坑

### 坑 1：CancellationToken 的传递

`_cts` 可能在 `Stop()` 时被替换或 dispose，但正在执行的步骤还持有旧 token。解决：每次访问前先快照 `var token = _cts?.Token ?? CancellationToken.None`。

### 坑 2：HostReceive 的多通道隔离

最初所有 Host 消息进同一个 inbox，导致通道 A 的消息被通道 B 的 Receive 步骤误消费。解决：`Dictionary<string, Channel<HostMessage>>` 按通道名隔离。

### 坑 3：Branch 条件的解析

最初用 `DataTable.Compute()` 解析表达式，但无法访问 `lastReceived.Stream` 这样的对象属性。解决：自己实现简单表达式解析器（或改用 `DynamicExpresso` 库）。

## 6. 待办

- [ ] 支持循环步骤（Loop / ForEach）
- [ ] 支持子场景调用（SubScenario）
- [ ] 支持变量赋值（SetVariable）
- [ ] 图形化场景编辑器（拖拽式）
- [ ] 条件表达式改用成熟的解析库

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2025-04 | 初始版本：Send/Receive/Reply/Delay |
| 2025-05 | 加入 Branch / Log / HostSend |
| 2025-06 | 多通道 Host 消息路由 |
