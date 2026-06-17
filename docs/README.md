# EAPSimulator 开发文档导航

> 本文档是项目的文档入口。按模块组织，每个模块一份独立文档。

## 项目概述

EAPSimulator 是一个 **EAP（Equipment Automation Program）模拟器**，用于在 MES / RMS / WMS 等外部系统对接前，模拟真实设备的 SECS/GEM 通讯行为。

**核心价值：** 让 MES 开发方在没有真实设备的情况下，也能完成联调测试。

## 架构概览

```
┌─────────────────────────────────────────────────────────┐
│                    UI (Avalonia)                        │
│  MainWindow · ConfigWindow · AutoReplyView · ...       │
└──────────────┬──────────────────────────────┬───────────┘
               │                              │
               ▼                              ▼
┌──────────────────────────┐    ┌──────────────────────────┐
│   EapBridge (桥接层)      │    │   ScenarioEngine (场景)   │
│  SECS ↔ Host 双向转发     │    │  脚本驱动的自动应答       │
└──────┬───────────┬───────┘    └──────────────────────────┘
       │           │
       ▼           ▼
┌────────────┐ ────────────────┐
│ SECS/GEM   │ │ Host Protocol  │
│ Protocol   │ │ (8种传输协议)    │
└──────┬─────┘ └────────────────┘
       │
       ▼
┌────────────┐
│ HSMS       │
│ Transport  │
└────────────┘
```

## 模块文档索引

| 模块 | 文档 | 说明 |
|---|---|---|
| 全局 | [ARCHITECTURE.md](ARCHITECTURE.md) | 项目架构、技术栈、数据流 |
| 全局 | [UI_GUIDELINES.md](UI_GUIDELINES.md) | UI 通用约定（主题/布局/样式） |
| 全局 | [LESSONS_LEARNED.md](LESSONS_LEARNED.md) | 跨模块踩坑沉淀 |
| SECS/GEM | [modules/SECS_GEM.md](modules/SECS_GEM.md) | SECS/GEM 协议实现 |
| Host 通道 | [modules/HOST_CHANNEL.md](modules/HOST_CHANNEL.md) | Host 通道（MES/RMS/WMS） |
| 自定义协议 | [modules/CUSTOM_PROTOCOL.md](modules/CUSTOM_PROTOCOL.md) | 自定义 JSON 协议 |
| 场景引擎 | [modules/SCENARIO_ENGINE.md](modules/SCENARIO_ENGINE.md) | 自动应答场景脚本 |
| 桥接层 | [modules/EAP_BRIDGE.md](modules/EAP_BRIDGE.md) | SECS ↔ Host 双向转发 |
| 设备状态 | [modules/EQUIPMENT_STATE.md](modules/EQUIPMENT_STATE.md) | GEM 状态机 |
| 服务端 | [modules/SERVER.md](modules/SERVER.md) | 无头运行模式 |
| 业务 | [three-party-communication.md](three-party-communication.md) | 三方通讯业务文档 |
| 迁移 | [AVALONIA_MIGRATION_REQUIREMENTS.md](AVALONIA_MIGRATION_REQUIREMENTS.md) | WPF → Avalonia 迁移记录 |

## 文档模板

每个模块文档遵循统一结构：

```markdown
# 模块名

## 1. 概述（这模块解决什么问题）
## 2. 关键设计决策（为什么这样做）
## 3. 文件结构 / 代码导览（去哪看）
## 4. 数据流 / 状态机（怎么跑）
## 5. 踩过的坑（出过什么问题）
## 6. 待办（TODO）
## 7. 变更记录（一行一条 + 日期）
```

## 开发约定

- **新增模块**时，在 `docs/modules/` 下新建文档，并更新本导航
- **修改模块**时，更新对应文档的"变更记录"段
- **踩坑**时，优先记在对应模块文档的"踩过的坑"段；跨模块的通用坑记在 `LESSONS_LEARNED.md`
- **UI 约定**（颜色/布局/主题）统一记在 `UI_GUIDELINES.md`，不要在每个模块里重复写
