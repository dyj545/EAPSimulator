# 设备状态模块

## 1. 概述

`EquipmentStateManager` 管理 GEM（Generic Equipment Model）所需的完整设备状态，包括：
- 状态变量（SVID — Status Variable ID）
- 设备常量（ECID — Equipment Constant ID）
- 采集事件（CEID — Collection Event ID）
- 数据报告（RPTID — Report ID）
- 报警（ALID — Alarm ID）
- 状态集（State Set）

**核心价值：** 把原来硬编码在 `EquipmentModel` 里的数据抽成可配置的状态管理器，支持从 JSON 加载。

## 2. 关键设计决策

### 2.1 为什么从 EquipmentModel 拆分

- `EquipmentModel` 原本既管 GEM 状态机又管数据，职责过重
- 状态数据（SVID/ECID/CEID）应该可配置，不应硬编码
- 拆分后 `EquipmentModel` 专注状态机，`EquipmentStateManager` 专注数据

### 2.2 默认值 vs 配置文件

- `CreateDefault()` 提供开箱即用的默认值（温度/压力/真空度等）
- 用户可通过 JSON 配置文件覆盖默认值
- 配置文件路径由 `ConfigViewModel` 管理

### 2.3 状态变量的类型

```csharp
public class StateVariable
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }    // 统一用 string，显示时再转换
    public string Unit { get; set; }
    public string Description { get; set; }
}
```

- `Value` 用 `string` 而非 `double/int`，因为 SECS 的 SVID 返回值可能是任意类型
- 显示和序列化时保持 string，业务层按需转换

## 3. 文件结构

```
src/EAPSimulator.Core/EquipmentState/
├── EquipmentStateManager.cs    ← 状态管理器主类
└── StateDefinitions.cs         ← 数据结构定义（StateVariable / EquipmentConstant / ...）
```

## 4. 与 GEM 状态机的关系

```
EquipmentModel (状态机)
├── CurrentState: OFFLINE/ONLINE/PROCESSING/...
├── ControlState: LOCAL/REMOTE/REMOTE_LOCAL
└── 依赖 EquipmentStateManager 提供：
    ├── SVID 值（S6F11 Event Report 用）
    ├── ECID 值（S2F13/S2F15 用）
    ├── CEID 列表（S6F11 触发条件）
    ── ALID 列表（S5F1/S5F3 用）
```

## 5. 踩过的坑

### 坑 1：SVID 值的更新时机

SVID 值在 `S6F11 Event Report` 发送时必须反映最新状态。最初在创建消息时快照值，导致并发修改时值不一致。解决：在 `DataMapper.Map()` 时实时读取。

### 坑 2：ECID 的只读保护

有些 ECID 是只读的（如设备序列号），不应被 S2F15 修改。解决：`EquipmentConstant` 加 `IsReadOnly` 标志。

## 6. 待办

- [ ] 支持从 JSON 文件加载完整配置
- [ ] 支持 SVID 值的动态计算（如公式 `A + B * 2`）
- [x] 支持 CEID 的自动触发条件 — 2026-06-24（由 ScenarioEngine 的 `TriggerOnMessage` 实现，见 [SCENARIO_ENGINE.md](SCENARIO_ENGINE.md#463-按入站消息触发场景triggeronmessage)）

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2026-06 | 从 EquipmentModel 拆分出 EquipmentStateManager |
| 2026-06-25 | 文档确认 CEID 自动触发由 ScenarioEngine `TriggerOnMessage` 承接；默认示例 `Lot Start` 使用 S6F11 ProcessStart 模板路径 `1` 的 CEID=101 触发 |
