# SECS/GEM 协议模块

## 1. 概述

SECS/GEM 是半导体制造行业标准的设备通讯协议。本模块实现了完整的 SECS/GEM 协议栈，让 EAPSimulator 能作为"虚拟设备"与 MES 系统进行 SECS 通讯。

**核心能力：**
- HSMS-SS（Single Session）TCP 连接管理
- SECS-II 消息编码/解码（L 型二进制格式）
- GEM 设备状态机（OFFLINE → ONLINE → PROCESSING → ...）
- 标准消息自动应答（S1F13/S1F14/S1F17/S6F11 等）
- 可插拔消息路由（MessageRouter + Handler 链）

## 2. 关键设计决策

### 2.1 协议栈分层

```
SecsGemProtocol (对外 IProtocol 接口)
├── HsmsTransport   (HSMS 连接管理、消息收发)
├── MessageRouter   (消息路由、Handler 链)
├── EquipmentModel  (GEM 状态机、设备属性)
└── Handlers/       (具体消息处理器)
```

- `SecsGemProtocol` 实现 `IProtocol` 接口，对外统一
- `HsmsTransport` 只负责底层 TCP + HSMS 帧
- `MessageRouter` 按 Stream/Function 分发到 Handler
- `EquipmentModel` 维护 GEM 状态机，Handler 可读写

### 2.2 为什么不用现成 SECS 库

- 需要完全控制消息路由逻辑（配合 ScenarioEngine 做自动应答）
- 需要与 Host Protocol 桥接（EapBridge）
- 需要支持自定义消息模板（SecsMessageTemplate）
- 现成库（如 SECS.Net）耦合度太高，扩展困难

### 2.3 W-bit 回复机制

SECS 消息的 W-bit 表示"需要回复"。实现方式：
- `HsmsTransport` 收到消息后，检查 W-bit
- 如果有 W-bit，调用 `MessageRouter.RouteAsync()` 获取回复
- 回复消息的 SystemBytes 必须与原消息一致
- 通过 `CancellationToken` 支持超时取消

## 3. 文件结构

```
src/EAPSimulator.Core/Protocols/SecsGem/
├── SecsGemProtocol.cs          ← 主协议类，实现 IProtocol
── MessageRouter.cs            ← 消息路由器
├── SecsMessageTemplate.cs      ← 消息模板（JSON 定义）
├── Hsms/
│   ├── HsmsTransport.cs        ← HSMS TCP 传输层
│   ├── HsmsConnection.cs       ← 单个 HSMS 连接
│   ├── HsmsHeader.cs           ← HSMS 消息头（10 字节）
│   ├── HsmsMessage.cs          ← HSMS 完整消息
│   └── HsmsStateMachine.cs     ← HSMS 连接状态机
├── SecsII/
│   ├── SecsItem.cs             ← SECS-II 数据项（L/U/B/A/J/I/F）
│   └── SecsMessage.cs          ← SECS 消息（Stream/Function/W-bit + Items）
── Gem/
│   ├── EquipmentModel.cs       ← GEM 设备模型（状态/变量/报警）
│   └── GemStateMachine.cs      ← GEM 状态机
├── Handlers/
│   └── MessageHandlers.cs      ← 标准消息处理器注册
└── AutoReply/
    ├── ScenarioEngine.cs       ← 场景脚本引擎（见 SCENARIO_ENGINE.md）
    ├── AutoReplyHandler.cs     ← 自动应答处理器
    ├── AutoReplyRule.cs        ← 应答规则
    ├── AutoReplyConfig.cs      ← 应答配置
    ├── ScenarioModels.cs       ← 场景数据模型
    ├── FlowLayoutModels.cs     ← 流程图布局模型
    └── MatchUtil.cs            ← 消息匹配工具
```

## 4. 数据流

### 4.1 接收流程

```
TCP 收到字节流
  → HsmsTransport 解析 HSMS 帧
  → 检查是否 Data Message
  → SecsMessage.Decode() 解析 SECS-II
  → MessageReceived 事件（通知 UI/日志）
  → 如果 W-bit = 1:
      → MessageRouter.RouteAsync()
      → 匹配 Handler
      → Handler 生成回复
      → HsmsTransport 发送回复
```

### 4.2 发送流程

```
UI / ScenarioEngine 调用 SendAsync()
  → SecsGemProtocol 构建 SecsMessage
  → HsmsTransport 编码 HSMS 帧
  → TCP 发送
  → MessageSent 事件
```

### 4.3 状态机

```
                  S1F13 (COMM ENABLE)
DISCONNECTED ──────────────────────→ OFFLINE
     ↑                                  │
     │                                  │ S1F17 (ONLINE)
     │                                  ↓
     │                              ONLINE
     │                                  │
     │                                  │ S6F11 (Event Report)
     │                                  ↓
     ────────────────────────── PROCESSING
         S1F14 (COMM DISABLE)
```

## 5. 踩过的坑

### 坑 1：SystemBytes 必须一致
SECS 回复消息的 SystemBytes 必须与原消息完全一致，否则对方会丢弃。`MessageRouter` 里必须 `reply.SystemBytes = secsMsg.SystemBytes`。

### 坑 2：W-bit 判断时机
必须在 `HsmsTransport` 层判断 W-bit，而不是在 `SecsGemProtocol` 层。因为 HSMS 协议本身有 Select/Select Response 机制，W-bit 是 SECS 层的概念。

### 坑 3：SecsItem 编码的 L 型嵌套
SECS-II 的 L 型（List）可以嵌套，编码时必须递归处理。`SecsItem.Encode()` 用递归实现。

### 坑 4：HSMS 连接状态机
HSMS 有 6 个状态（NOT_CONNECTED → CONNECTED_SEPARATE → CONNECTED_SINGLE → ...），状态转换必须严格按规范。`HsmsStateMachine` 实现了完整的状态转换表。

## 6. 待办

- [ ] 支持 HSMS-GS（Group Session，多会话）
- [ ] 支持 SECS-II 的 A 型（ASCII）和 J 型（JIS-8）编码
- [ ] GEM 变量/常量/报警的完整实现
- [ ] 性能优化：消息池化、零拷贝编码

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2025-04 | 初始版本：HSMS + SECS-II + GEM 状态机 |
| 2025-05 | 加入 MessageRouter + Handler 链 |
| 2025-06 | 加入 ScenarioEngine 自动应答 |
