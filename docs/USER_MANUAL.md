# EAPSimulator 用户手册

> 面向使用者的操作说明。开发实现细节请看 [开发文档导航](README.md) 和 [场景引擎模块](modules/SCENARIO_ENGINE.md)。

## 1. 启动与配置

### 1.1 启动桌面 UI

```bash
dotnet run --project src/EAPSimulator.UI
```

启动后默认加载：

| 文件 | 用途 |
|---|---|
| `secs_message_templates.json` | SECS/GEM 消息模板 |
| `host_message_templates.json` | Host/MES/RMS 消息模板 |
| `auto_reply_rules.json` | 快速回复规则与场景脚本 |
| `secs_gem_active.json` / `secs_gem_passive.json` / `secs_gem_alternating.json` | SECS/GEM 连接参数 |

### 1.2 选择连接角色

在配置页选择 SECS/GEM 连接模式：

| 模式 | 当前模拟器角色 | 常见用途 |
|---|---|---|
| `Active` | Host / EAP 侧 | 主动连接设备或设备模拟器 |
| `Passive` | Equipment 侧 | 等待 Host / EAP 连接 |
| `Alternating` | 自动尝试连接和监听 | 两端启动顺序不固定的联调 |

新建场景时会按当前连接模式自动给 `Role` 填默认值；运行时如果场景角色与当前角色不一致，引擎会拒绝启动，避免 Host / Equipment 方向写反。

## 2. 快速回复

快速回复适合“一条请求 → 一条固定回复”的场景，例如 S1F1 → S1F2、S1F13 → S1F14。

操作步骤：

1. 打开 **自动回复** 页，选择 **快速回复**。
2. 点击 **+ 添加**。
3. 在 **触发消息** 的模板搜索框输入关键字（如 `S6F11`、`S1F13`），选择触发模板。
4. 如需按字段过滤，点击 **+ 添加条件**：
   - 默认模式：选择字段 → 操作符 → 期望值。
   - 表达式模式：点击 `ƒx`，填写表达式，例如 `num(secs["1"]) == 101`。
5. 在 **回复模板** 中选择要发送的回复模板。
6. 点击顶部 **保存**。

说明：

- 模板选择框支持模糊搜索，也允许直接输入；只有输入内容匹配到模板时，字段下拉才会自动带出模板字段。
- 条件中的 SECS 路径使用 `0/1/2` 形式，表示从根 List 逐层按下标访问。
- 快速回复优先级高于场景引擎；同一条消息如果已命中快速回复，就不会再触发场景。

## 3. 场景引擎

场景适合多步骤流程，例如“收到 S6F11 事件 → 回复 S6F12 → 等待/发送后续消息 → 分支/循环处理”。

### 3.1 创建场景

1. 打开 **自动回复** 页，选择 **场景**。
2. 点击 **+ 场景**。
3. 在右侧 **场景设置** 填写：
   - **名称 / 描述**：用于列表和日志识别。
   - **角色**：Host 表示 EAP/Active 侧；Equipment 表示设备/Passive 侧；Any 两侧均可运行。
   - **启用**：是否参与保存后的注册。
   - **循环**：场景结束后是否从第一步重新执行。
   - **连接后自动启动**：协议连接后自动运行。
4. 点击顶部 **保存**。

### 3.2 添加与编辑步骤

中间区域默认显示 **流程图**，也可以切换到 **列表**。

- 点击工具栏中的 `Send`、`Recv`、`Reply`、`Branch`、`Loop`、`ForEach` 等按钮会在当前选中步骤之后插入新步骤。
- 新增 `Send` 步骤默认不再带模板，需在模板框中明确选择，避免误发第一个模板（通常是 S1F1）。
- 模板 / Host 消息 / 子场景搜索框在点击或聚焦时会自动全选当前文本，但不会立即弹出完整清单；开始键盘输入关键字后才展开过滤清单。
- Host 消息名支持唯一关键字自动归一：例如输入 `LotEnd`，如果只匹配到 `MESLOTEND_MES_LotEnd`，离开输入或绑定回写时会自动改成完整模板名，运行时才能精确找到 Host 模板。
- 在流程图节点上右键，可以插入到节点前/后、删除步骤、切换断点。
- 拖动流程图节点可以调整布局；点击 **↺ 重置布局** 恢复自动布局。
- 可编辑连线（Branch case / default / OnError）末端有圆点，拖到目标节点即可修改跳转 Label；目标节点没有 Label 时会自动生成。

常用步骤：

| 步骤 | 用途 |
|---|---|
| `Receive` | 等待一条 SECS 消息；选择模板后自动填 S/F，并可添加字段条件 |
| `Reply` | 用模板回复上一条 `Receive` 捕获的消息；新增 Reply 时会优先按前一个 Receive 自动推荐 S+F+1 的回复模板 |
| `Send` | 主动发送一条 SECS 模板消息 |
| `HostSend` / `HostReceive` | 通过 Host 通道收发 MES/RMS/WMS 消息；`HostReceive` 可直接添加 Host 字段条件 |
| `SetVariable` | 设置变量，后续模板可用 `${变量名}` 引用 |
| `Branch` | 按条件跳到指定 Label |
| `Loop` / `EndLoop` | 按次数或 `While ƒx` 表达式循环 |
| `ForEach` / `EndForEach` | 遍历 SECS List、Host 数组字段或变量分隔列表 |
| `CallScenario` | 调用另一个场景作为子流程 |
| `Log` | 输出调试日志 |

### 3.3 按入站消息触发场景

除了手动 **▶ 运行** 和 **连接后自动启动**，场景还可以在引擎空闲时由入站 SECS 消息自动触发。

配置步骤：

1. 选中场景。
2. 勾选 **按入站消息触发**。
3. 在 **模板搜索** 中选择触发模板，例如 `S6F11 - Collection Event Report (ProcessStart)`。
4. 可选：点击 **+ 条件**，选择 `CEID` 字段并填写值，例如 `101`。
5. 建议把场景第一步设为 `Receive`，并选择同一个触发模板。这样触发消息会被第一步立即捕获，后续 `Reply` 才能拿到上一条消息的 `SystemBytes`。
6. 点击 **保存**，重新连接协议或再次应用规则后生效。

注意：

- 只有引擎空闲时才扫描触发场景；如果已有场景正在运行，新消息会进入当前运行场景的 inbox，不会启动另一个场景。
- 多个触发场景同时命中时，按配置列表顺序只启动第一个。
- 如果勾选触发但第一步不是 `Receive`，界面会显示橙色警告；这不阻止保存，但后续 `Reply` 很可能因为没有上一条接收消息而失败。
- 快速回复优先级更高；如果同一 S/F 的快速回复无条件命中，场景触发不会被执行。需要场景接管该消息时，请删除或禁用对应快速回复，或给快速回复加更严格条件。

### 3.4 S10F5 → MES → 字段 Matching → 回复机台

如果设备发送 `S10F5` 后，EAP 需要问 MES，并根据 MES 返回字段决定回复给机台，可在一个场景里串起来：

1. 场景勾选 **按入站消息触发**，触发模板选择 `S10F5 - Terminal Display Multi`。
2. 第 1 步添加 `Receive`，模板也选择 `S10F5 - Terminal Display Multi`，用于捕获设备原始消息。
3. 第 2 步添加 `HostSend`：
   - 消息名选择要发给 MES 的 Host 模板，例如 `FPPMOVEIN_LotTrackIn` / `FPPMOVEINQUERY_MoveInQuery`。
   - 可以直接输入唯一关键字，例如 `LotEnd` 会自动识别为 `MESLOTEND_MES_LotEnd`；如果关键字命中多个模板，请继续输入到唯一或从下拉列表中选择。
   - 如需把 S10F5 字段带给 MES，先用 `SetVariable` 从上一条 SECS 字段取值，再在 Host 模板默认值或 RawBody 中写 `${变量名}`。
4. 第 3 步添加 `HostReceive`：
   - 消息名选择 MES 返回模板，例如 `FPPMOVEIN_LotTrackIn_REPLY`；如果 MES 可能返回多种消息，也可以留空匹配任意 Host 消息。
   - 点击 **+ 条件** 添加字段 Matching。传统模式下 `字段` 直接写 Host 字段名，例如 `typeId`、`insResult`；操作符选 `==`，值填 `O` / `Y` / `N`。
   - 也可点 `ƒx` 使用表达式，例如 `host["typeId"] == "O"`、`host.Name == "FPPMOVEIN_LotTrackIn_REPLY" && host["typeId"] == "O"`。
5. 第 4 步添加 `Reply` 或 `Send`：
   - 如果要回复触发的 `S10F5`，通常选择对应 ACK 模板 `S10F6 - Terminal Display Multi Acknowledge` 作为 `Reply`。
   - 如果实际要回复的是其它 SECS 消息（例如现场口径称为 `S5F10`，但当前模板库暂无 S5F10），先在消息编辑器新增对应 SECS 模板，再在这里选择。
6. 如 MES 返回不同结果要走不同回复：
   - 做法 A：添加多个 `HostReceive`，每个 HostReceive 用不同条件 + `OnErrorLabel` / 超时分支组织流程。
   - 做法 B：先用一个 `HostReceive` 接收，再用 `Branch` 按 `host["字段名"]` 分支到不同 Label，之后各自放对应 `Reply` / `Send`。

> 注意：Host 通道必须在 Config 中启用并连接；SECS 连接启动后会把 HostSend/HostReceive 接到当前场景引擎。若 HostReceive 一直超时，请检查 Host 消息名、通道连接、字段名大小写和值。

### 3.5 调试场景

工具栏提供：

| 按钮 | 作用 |
|---|---|
| `⏸ 暂停` | 请求在下一步边界暂停 |
| `▶▶ 继续` | 从暂停状态继续运行 |
| `⏭ 单步` | 执行一个步骤后再次暂停 |
| `🔴 断点` | 给当前选中步骤切换断点 |

暂停时：

- 流程图节点会用橙色边框标出当前步骤。
- 右侧变量观察面板显示当前变量快照。
- 断点只用于调试，不会保存到 `auto_reply_rules.json`。

## 4. Host 消息模板管理

打开顶部 **Host** 页可以编辑 `host_message_templates.json`。

左侧模板列表提供：

- **搜索框**：按消息名、描述、方向、分组模糊过滤。消息很多时可输入 `LotEnd`、`MoveIn`、`REPLY` 等关键字快速定位。
- **分组下拉**：按消息名前缀自动分组。规则是取第一个 `_` 之前的部分，例如 `MESLOTEND_MES_LotEnd` 归到 `MESLOTEND`，没有 `_` 的模板归到 `未分组`。
- **计数提示**：显示当前筛选结果数量，例如 `12/180 个模板`。
- **清空**：清除搜索关键字并回到 `全部` 分组。

右侧仍用于编辑模板名称、方向、通道、Body、字段和 JSON 预览。保存后，场景里的 `HostSend / HostReceive` 消息名下拉会同步使用这些模板名。

## 5. 示例：ProcessStart 自动处理

默认 `auto_reply_rules.json` 中包含示例场景 **Lot Start**：

1. 触发条件：收到 `S6F11 - Collection Event Report (ProcessStart)`，且 `CEID == 101`。
2. 第一步 `Receive` 捕获这条 S6F11。
3. 第二步 `Reply` 回复 `S6F12 - Collection Event Report Acknowledge`。
4. 第三步 `Send` 下发 `S2F41 - Host Command (START)`，并等待对端 W-bit 回复。

如果要改成其他事件：

1. 在场景设置中修改触发模板或条件值。
2. 在第一步 `Receive` 中选择相同的模板。
3. 修改后续 `Reply` / `Send` 模板。
4. 保存并重连协议验证。

## 6. 常见问题

### 6.1 触发场景没有启动

检查顺序：

1. 场景是否 **启用**。
2. `Role` 是否和当前连接模式一致。
3. 是否已经有其他场景在运行。
4. 触发模板对应的 S/F 是否正确。
5. 条件路径和值是否正确，例如 ProcessStart 的 CEID 在示例模板中是路径 `1`。
6. 是否存在同 S/F 的快速回复先命中并拦截。
7. 修改后是否已保存并重新连接 / 重新应用规则。

### 6.2 Reply 步骤失败

`Reply` 必须依赖之前的 `Receive` 捕获消息。常见错误：

- 场景第一步是 `Reply` 或 `Send`。
- 按消息触发后第一步不是 `Receive`。
- 第一条 `Receive` 的 S/F 或条件和触发消息不一致，导致触发消息被丢弃。

### 6.3 字段下拉为空

字段下拉依赖模板解析：

- 确认模板名已从下拉中选中，而不是只输入了一半。
- 确认模板的 `itemXml` 可以正常构建 SECS 消息。
- 对 HostReceive 条件，路径按 Host 字段名填写；对 SECS 条件，路径按 `0/1/2` 填写。

### 6.4 流程图节点位置错乱

插入、删除、上移/下移步骤时，布局索引会自动同步。若布局仍不符合预期，点击 **↺ 重置布局** 重新生成默认布局。
