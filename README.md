# EAPSimulator

半导体/面板制造行业的设备自动化协议模拟器，用于 EAP（Equipment Automation Program）系统的开发调试与协议验证。

## 核心特性

- **双协议支持** — SECS/GEM（基于 HSMS 传输层）+ 可扩展自定义协议
- **双角色切换** — Host（主动连接）/ Equipment（被动监听）/ Alternating 模式
- **分层消息路由** — 条件自动回复 → 场景引擎 → 内置处理器，三级优先级匹配
- **JSON 消息模板** — 通过配置文件定义 SECS 消息结构，无需修改代码
- **可视化调试** — 基于 Avalonia 的跨平台桌面 UI，提供消息日志、状态监控、消息编辑器

## 项目结构

```
EAPSimulator/
├── src/
│   ├── EAPSimulator.Core/          # 协议核心库
│   │   ├── Protocols/
│   │   │   ├── SecsGem/            # SECS/GEM 协议实现
│   │   │   │   ├── Hsms/           # HSMS 传输层 (TCP)
│   │   │   │   ├── SecsII/         # SECS-II 消息编解码
│   │   │   │   ├── Gem/            # GEM 状态机与设备模型
│   │   │   │   ├── Handlers/       # 内置消息处理器 (S1-S6)
│   │   │   │   └── AutoReply/      # 自动回复规则与场景引擎
│   │   │   └── Custom/             # 自定义协议框架
│   │   └── Configuration/          # 配置模型
│   └── EAPSimulator.UI/            # Avalonia 桌面 UI
│       ├── Views/                  # 界面视图
│       └── ViewModels/             # MVVM 视图模型
├── secs_message_templates.json     # SECS 消息模板定义
├── secs_gem_active.json            # SECS/GEM 主动模式配置
├── secs_gem_passive.json           # SECS/GEM 被动模式配置
├── secs_gem_alternating.json       # SECS/GEM 交替模式配置
├── auto_reply_rules.json           # 自动回复规则配置
└── custom_protocol.json            # 自定义协议配置
```

## 快速开始

完整操作说明见 [用户手册](docs/USER_MANUAL.md)；开发实现说明见 [开发文档导航](docs/README.md)。

### 环境要求

- .NET 9.0 SDK

### 构建运行

```bash
# 克隆仓库
git clone https://github.com/dyj545/EAPSimulator.git
cd EAPSimulator

# 构建
dotnet build

# 运行
dotnet run --project src/EAPSimulator.UI
```

### 连接模式配置

在 `secs_gem_*.json` 中配置连接参数：

```json
{
  "LocalHost": "0.0.0.0",
  "LocalPort": 5000,
  "RemoteHost": "127.0.0.1",
  "RemotePort": 5000,
  "DeviceId": 1,
  "ConnectionMode": "Active"
}
```

| 模式 | 说明 |
|------|------|
| `Active` | 作为 Host 主动连接 Equipment |
| `Passive` | 作为 Equipment 被动等待连接 |
| `Alternating` | 同时监听和连接，先成功的一方生效 |

## 消息路由机制

接收到 SECS 消息时，按以下优先级依次处理：

1. **条件自动回复规则** — 根据字段值匹配，快速响应常见消息
2. **场景引擎** — 多步骤对话流程编排，支持手动运行、连接后自动启动、按入站消息触发、循环/分支/调试
3. **内置处理器** — 覆盖标准 SECS/GEM 消息

## 自动回复规则配置

在 `auto_reply_rules.json` 中定义条件匹配规则：

```json
{
  "triggerStream": 1,
  "triggerFunction": 13,
  "conditions": [],
  "replyStream": 1,
  "replyFunction": 14,
  "replyTemplateName": "Establish Communication Reply (ACK)",
  "enabled": true
}
```

## 自定义协议

通过 JSON 定义自定义协议的消息格式，支持插件扩展：

```json
{
  "name": "CustomEquipment",
  "framing": "Delimiter",
  "delimiter": "\r\n",
  "encoding": "UTF-8",
  "messages": [
    {
      "id": "CMD001",
      "name": "HEARTBEAT",
      "fields": [
        { "name": "CMD", "type": "String" },
        { "name": "TIMESTAMP", "type": "String" }
      ]
    }
  ]
}
```

## 已支持的 SECS 消息

| Stream | 功能 |
|--------|--------|
| S1 | 设备状态查询 (Are You There, Status Variable, Establish Communication) |
| S2 | 设备控制 (Equipment Constant, Host Command, Remote Command, Report/Event 定义) |
| S5 | 报警管理 (Alarm Report, Enable/Disable) |
| S6 | 事件收集 (Collection Event Report, Report 定义) |
| S7 | Process Program 管理 (Load, Send, Request, Delete) |
| S9 | 错误消息 (Unrecognized ID/Stream/Function, Illegal Data) |
| S10 | 终端消息 (Terminal Request, Display) |

## 技术栈

- .NET 9.0
- Avalonia UI 11.2 (跨平台桌面框架)
- CommunityToolkit.Mvvm (MVVM 框架)
- Serilog (日志)
- Newtonsoft.Json (JSON 序列化)

## 许可证

[MIT License](LICENSE)
