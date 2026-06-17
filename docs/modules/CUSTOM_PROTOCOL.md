# 自定义协议模块

## 1. 概述

自定义协议模块允许用户通过 JSON 配置文件定义自己的通讯协议，用于模拟非标准设备或特殊业务场景。

**核心能力：**
- JSON 定义消息类型（请求/响应格式）
- 插件机制扩展消息处理逻辑
- 支持 TCP 长连接
- 可配置为客户端或服务端

## 2. 关键设计决策

### 2.1 为什么需要自定义协议

- 有些设备不使用 SECS/GEM，而是自定义 TCP 协议
- MES 对接时可能需要模拟特殊格式的消息
- 测试场景需要快速定义简单的请求/响应规则

### 2.2 插件机制

```csharp
public interface ICustomProtocolPlugin
{
    string Name { get; }
    void Initialize(CustomProtocol protocol);
    Task<ProtocolMessage?> HandleMessageAsync(ProtocolMessage message, CancellationToken ct);
}
```

- 插件在 `CustomProtocol.RegisterPlugin()` 时注册
- 每个插件可以处理特定类型的消息
- 插件链按注册顺序执行，第一个返回非 null 的插件终止链

### 2.3 消息定义格式

```json
{
  "name": "MyCustomProtocol",
  "messages": [
    {
      "id": "LOGIN",
      "direction": "Receive",
      "fields": [
        { "name": "username", "type": "string" },
        { "name": "password", "type": "string" }
      ]
    },
    {
      "id": "LOGIN_RESPONSE",
      "direction": "Send",
      "fields": [
        { "name": "status", "type": "int" },
        { "name": "token", "type": "string" }
      ]
    }
  ]
}
```

## 3. 文件结构

```
src/EAPSimulator.Core/Protocols/Custom/
├── CustomProtocol.cs           ← 主协议类
├── CustomTransport.cs          ← TCP 传输层
── ProtocolDefinition.cs       ← 协议定义（JSON 模型）
└── ICustomProtocolPlugin.cs    ← 插件接口
```

## 4. 数据流

```
TCP 收到字节流
  → CustomTransport 解析（按行/按长度/按分隔符）
  → 构建 ProtocolMessage
  → MessageReceived 事件
  → 遍历插件链
  → 第一个返回非 null 的插件生成回复
  → CustomTransport 发送回复
```

## 5. 踩过的坑

### 坑 1：消息边界问题

TCP 是流式协议，没有消息边界。`CustomTransport` 需要用户指定分隔符（如 `\n`）或长度前缀。

### 坑 2：编码问题

自定义协议可能用 UTF-8、GBK、ASCII 等不同编码。`CustomTransport` 默认 UTF-8，但应在 `ProtocolDefinition` 里允许配置。

## 6. 待办

- [ ] 支持二进制消息格式（不只是文本）
- [ ] 支持多种消息边界策略（长度前缀、分隔符、固定长度）
- [ ] 内置常用插件（HTTP 模拟、MQTT 模拟）

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2025-04 | 初始版本：TCP + JSON 定义 |
| 2025-05 | 加入插件机制 |
