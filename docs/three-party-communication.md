# EAP 三方通讯泳道图

## Equipment ↔ EAP ↔ MES 通讯流程

```mermaid
sequenceDiagram
    participant EQ as Equipment<br/>(设备端)
    participant EAP as EAP Simulator<br/>(桥接层)
    participant MES as Host/MES<br/>(上层系统)

    Note over EQ, MES: ── 通讯建立 ──

    EQ->>EAP: TCP Connect (Passive/Active)
    EAP->>EQ: S1F13 Establish Communication
    EQ->>EAP: S1F14 Establish Comm Reply (ACK)
    EAP->>EQ: S1F2 Are You There Reply

    MES->>EAP: Host Connect (TCP/HTTP/MQ/gRPC)
    EAP->>MES: Host Handshake

    Note over EQ, MES: ── 正常生产流程 ──

    EQ->>EAP: S6F11 Collection Event Report<br/>(CEID=1001, ProcessStart)
    EAP->>EAP: Scenario Engine<br/>Mapper: CEID → variable
    EAP->>EAP: Judgement: CEID == 1001?
    EAP->>EQ: S6F12 Collection Event ACK
    EAP->>MES: Host: ProcessStart Report

    MES->>EAP: Host: RemoteCommand (Start)
    EAP->>EAP: Scenario Engine<br/>Host Trigger Match
    EAP->>EQ: S2F41 Host Command (START)

    EQ->>EAP: S2F42 Host Command ACK
    EAP->>MES: Host: CommandAck

    EQ->>EAP: S6F11 Collection Event Report<br/>(CEID=1002, ProcessComplete)
    EAP->>EQ: S6F12 Collection Event ACK
    EAP->>MES: Host: ProcessComplete Report

    Note over EQ, MES: ── 告警处理 ──

    EQ->>EAP: S5F1 Alarm Report<br/>(ALID=1, Temperature High)
    EAP->>EQ: S5F2 Alarm ACK
    EAP->>MES: Host: AlarmReport

    EQ->>EAP: S5F1 Alarm Report Clear<br/>(ALID=1)
    EAP->>EQ: S5F2 Alarm ACK
    EAP->>MES: Host: AlarmClear

    Note over EQ, MES: ── 配方管理 ──

    MES->>EAP: Host: RecipeDownload (RecipeA)
    EAP->>EQ: S7F3 Recipe Send
    EQ->>EAP: S7F4 Recipe ACK
    EAP->>MES: Host: RecipeAck

    MES->>EAP: Host: RecipeRequest
    EAP->>EQ: S7F1 Recipe Request
    EQ->>EAP: S7F2 Recipe Data
    EAP->>MES: Host: RecipeData

    Note over EQ, MES: ── 数据采集 ──

    EQ->>EAP: S1F3 Selected Equipment Status
    EAP->>EQ: S1F4 Status Reply<br/>(SVID values)

    MES->>EAP: Host: DataRequest
    EAP->>EQ: S1F3 Selected Equipment Status
    EQ->>EAP: S1F4 Status Reply
    EAP->>MES: Host: DataReport

    Note over EQ, MES: ── 状态管理 ──

    EAP->>EAP: Scenario: StateAlterer<br/>ControlState = REMOTE
    EAP->>EAP: EquipmentModel.StatusVariables updated

    Note over EQ, MES: ── 通讯断开 ──

    EQ->>EAP: TCP Disconnect
    EAP->>MES: Host: Disconnect Event
    MES->>EAP: Host Disconnect
```

## 消息流向说明

| 方向 | 协议 | 说明 |
|------|------|------|
| Equipment → EAP | SECS/GEM (HSMS) | 设备事件、告警、状态上报 |
| EAP → Equipment | SECS/GEM (HSMS) | 远程命令、配方下载、状态查询 |
| EAP → MES | Host Protocol | 事件转发、告警通知、数据上报 |
| MES → EAP | Host Protocol | 远程命令、配方管理、数据请求 |

## 场景引擎在通讯中的角色

```
Equipment                    Scenario Engine                    MES
    │                              │                              │
    │──S6F11 (CEID)──►            │                              │
    │                    ┌────────┴────────┐                     │
    │                    │ 1. Mapper       │                     │
    │                    │    CEID → var   │                     │
    │                    │ 2. Judgement    │                     │
    │                    │    var == 1001? │                     │
    │                    │ 3. Host Action  │                     │
    │                    └────────┬────────┘                     │
    │                              │──── ProcessComplete ──────►│
    │◄──S6F12 ACK───              │                              │
    │                              │                              │
    │                              │◄── RemoteCommand ──────────│
    │                    ┌────────┴────────┐                     │
    │                    │ Host Trigger    │                     │
    │                    │ Match → Action  │                     │
    │                    └────────┬────────┘                     │
    │◄──S2F41 Command──           │                              │
    │──S2F42 ACK────►             │                              │
    │                              │──── CommandAck ───────────►│
```
