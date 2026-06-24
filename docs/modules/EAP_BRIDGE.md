# EAP 桥接层模块

## 1. 概述

EapBridge 是 SECS/GEM 设备与 Host MES 之间的**双向数据桥接层**，负责：
- 将 SECS 事件（如 S6F11 Event Report）转换为 Host 消息发送给 MES
- 将 Host 命令（如 Start/Stop/Abort）转换为 SECS 消息发送给设备
- 管理消息模板的加载和字段映射

**核心价值：** 让 SECS 和 Host 两套协议能够协同工作，实现完整的 EAP 自动化流程。

## 2. 关键设计决策

### 2.1 为什么需要桥接层

- SECS/GEM 是设备侧协议，Host 是 MES 侧协议
- 两者消息格式完全不同（SECS-II 二进制 vs JSON/HTTP）
- 需要统一的字段映射机制（如 SECS 的 SVID → Host 的 field name）
- 需要解耦：SECS 协议改动不影响 Host 逻辑，反之亦然

### 2.2 消息模板机制

```csharp
// Host 模板：定义 SECS→Host 的转换规则
{
  "name": "EventReport",
  "trigger": { "stream": 6, "function": 11 },  // S6F11
  "fields": [
    { "name": "equipmentId", "source": "constant", "value": "EQUIP001" },
    { "name": "eventId", "source": "secs", "path": "Items[0].Items[0]" },
    { "name": "timestamp", "source": "system", "format": "ISO8601" }
  ]
}

// SECS 模板：定义 Host→SECS 的转换规则
{
  "name": "StartCommand",
  "trigger": { "hostMessage": "START" },
  "stream": 1,
  "function": 17,
  "fields": [
    { "name": "EquipmentID", "source": "host", "path": "equipmentId" }
  ]
}
```

### 2.3 数据映射器（DataMapper）

```csharp
public class DataMapper
{
    // 注册映射规则
    public void AddMapping(string hostField, string secsPath, Func<object, object> converter);
    
    // 执行映射
    public Dictionary<string, object> Map(SecsMessage secsMsg, HostMessageTemplate template);
}
```

- 支持常量、SECS 字段、系统变量（时间戳/计数器）三种数据源
- 支持自定义转换器（如 string → int）
- 映射规则可持久化到 JSON

## 3. 文件结构

```
src/EAPSimulator.Core/Protocols/Bridge/
├── EapBridge.cs                ← 桥接主类
├── DataMapper.cs               ← 字段映射器
└── MappingConfig.cs            ← 字段映射的 JSON 持久化 (MappingGroup + MappingConfig)

src/EAPSimulator.UI/
├── ViewModels/BridgeMappingViewModel.cs   ← 桥接映射编辑器 VM
└── Views/BridgeMappingView.axaml          ← 三列布局: 组列表 / 映射列表 / 详情
```

## 3.1 映射编辑 UI（2026-06-24）

主窗口左侧 Tab 加 **"桥接映射"** —— 按业务事件分组管理 SECS↔Host 字段映射。

**结构**：

| 元素 | 内容 |
|---|---|
| 第 1 列 | 映射组列表，启用/禁用图标 + 组名 + 映射条数 |
| 第 2 列 | 选中组的详情头（名称/SECS模板/Host模板/启用/描述）+ 映射条目列表 |
| 第 3 列 | 选中映射的字段编辑（来源/去向/SECS路径/Host字段/转换/描述） |
| 顶栏 | + 映射组 / 删除组 / 💾 保存 / 📂 加载 |

**持久化**：`bridge_mappings.json` 与可执行同目录。

```json
{
  "groups": [
    {
      "name": "EventReport",
      "secsTemplate": "S6F11",
      "hostTemplate": "EventReport",
      "enabled": true,
      "description": "wafer move",
      "mappings": [
        { "source": "Secs", "target": "Host",
          "secsPath": "1/0", "hostFieldName": "lotId",
          "conversion": "Trim", "description": "lot id" }
      ]
    }
  ]
}
```

**应用到运行中的 Bridge**：

- UI 保存按钮：写文件 + 立刻 `MappingConfig.ApplyTo(bridge.Mapper)`（如已 AttachBridge）
- `EapServerWorker` 启动 Bridge 时自动 `MappingConfig.Load().ApplyTo(_bridge.Mapper)`
- BridgeMapping VM 构造时自动 `LoadConfig`；SECS/Host 模板下拉名由 MainViewModel 从 MessageEditor / HostEditor 同步

## 3.2 拖拽连线视图（2026-06-24）

顶栏切换按钮 `📋 表格` / `🌐 拖拽连线`，默认表格。

`MappingCanvas` 控件（`src/EAPSimulator.UI/Controls/MappingCanvas.cs`）：

- **左列**：当前组的 SECS 模板字段树（深度优先展开 SecsItem，叶子可拖拽）
- **右列**：当前组的 Host 模板字段树（嵌套 HostField 用点号拼路径，叶子可拖拽）
- **中间 Canvas**：每条 mapping 一根 bezier 连线（绿色，控制点沿水平方向延伸）

**叶子锚点**：

- SECS 叶子右侧、Host 叶子左侧各画一个 9px 圆点
- 按住圆点拖拽 → 跟随鼠标的虚线幽灵线
- 松开时命中对侧叶子的圆点（容差 14px） → 新建 mapping（重复的跳过）
- 松开时没命中 → 取消

**右键连线** → 删除映射。

**布局响应**：监听 SECS/Host 两个 ScrollViewer 的 `ScrollChanged` 与 `_wireCanvas.LayoutUpdated`，滚动 / 拖列宽都会重算锚点 + 重画线。

## 4. 数据流

### 4.1 SECS → Host 流程

```
SECS 消息收到（如 S6F11 Event Report）
  → EapBridge.OnSecsMessageReceived()
  → 匹配 Host 模板（按 Stream/Function）
  → DataMapper.Map() 提取字段
  → 构建 HostMessage
  → HostProtocol.SendHostMessageAsync()
  → MES 收到 JSON 消息
```

### 4.2 Host → SECS 流程

```
Host 消息收到（如 START 命令）
  → EapBridge.OnHostMessageReceived()
  → 匹配 SECS 模板（按 hostMessage 类型）
  → DataMapper.Map() 提取字段
  → 构建 SecsMessage
  → SecsGemProtocol.SendAsync()
  → 设备收到 SECS 消息
```

## 5. 踩过的坑

### 坑 1：模板匹配优先级

多个模板可能匹配同一条消息（如 S6F11 有多个 CEID）。解决：按 specificity 排序（精确匹配 > 通配符匹配）。

### 坑 2：字段路径解析

SECS 消息的 Items 是嵌套列表，路径如 `Items[0].Items[1].Value` 需要递归解析。`DataMapper` 用简单的字符串分割实现。

### 坑 3：异步事件的处理

`MessageReceived` 事件是同步的，但桥接逻辑可能是异步的（如调用外部 API）。解决：用 `Task.Run()` 包装，但要注意异常处理和日志记录。

## 6. 待办

- [ ] 支持模板的版本管理（多版本并存）
- [ ] 支持条件映射（if-else 逻辑）
- [ ] 支持批量映射（一条 SECS 消息生成多条 Host 消息）
- [ ] 映射规则的图形化编辑器

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2025-04 | 初始版本：基本桥接 + 模板匹配 |
| 2025-05 | 加入 DataMapper 字段映射 |
