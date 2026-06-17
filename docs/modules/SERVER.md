# 服务端模块

## 1. 概述

`EapServerWorker` 是 EAPSimulator 的**无头运行模式**，不依赖 UI，通过配置文件启动 SECS/GEM + Host 协议栈 + 场景引擎。

**使用场景：**
- CI/CD 自动化测试（无需人工操作 UI）
- 服务器部署（7×24 小时运行）
- 批量测试（多实例并行）

## 2. 关键设计决策

### 2.1 基于 BackgroundService

```csharp
public class EapServerWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 加载配置
        // 启动 SECS/GEM
        // 启动 Host
        // 等待取消
    }
}
```

- 继承 `BackgroundService`，自动集成到 .NET Generic Host
- `stoppingToken` 用于优雅关闭
- 支持 `IHostedService` 生命周期（Start/Stop）

### 2.2 配置加载

```csharp
// 从 JSON 文件加载
var secsConfig = HsmsSettings.LoadFromFile("secs_gem_passive.json");
var hostConfig = HostTransportConfig.LoadFromFile("host_config.json");
var scenarioConfig = ScenarioDefinition.LoadFromFile("scenario.json");
```

- 所有配置从文件加载，不依赖 UI 的 `ConfigViewModel`
- 配置文件路径通过 `ServerConfig` 指定

### 2.3 与 UI 模式的区别

| 维度 | UI 模式 | Server 模式 |
|---|---|---|
| 配置来源 | `ConfigViewModel` 内存对象 | JSON 文件 |
| 启动方式 | 用户点击"连接" | `BackgroundService.StartAsync()` |
| 日志输出 | UI 日志面板 | Serilog 文件/控制台 |
| 场景运行 | 用户手动触发 | 配置文件指定自动运行 |
| 生命周期 | 窗口关闭时停止 | `CancellationToken` 取消时停止 |

## 3. 文件结构

```
src/EAPSimulator.Server/
├── EapServerWorker.cs          ← 后台服务主类
├── ServerConfig.cs             ← 服务端配置模型
└── Program.cs                  ← 入口点（Generic Host 启动）
```

## 4. 启动流程

```
Program.Main()
  → CreateHostBuilder()
  → 注册 EapServerWorker
  → 注册 Serilog
  → Build()
  → Run()
  → EapServerWorker.ExecuteAsync()
      → 加载 SECS 配置
      → 加载 Host 配置
      → 加载场景配置
      → 启动 SecsGemProtocol
      → 启动 HostProtocol
      → 启动 ScenarioEngine
      → 等待 stoppingToken
```

## 5. 踩过的坑

### 坑 1：配置文件路径

UI 模式下配置文件在项目根目录（`GetConfigDirectory()` 找 .sln），但 Server 模式下工作目录可能是 `bin/Debug/net9.0/`。解决：`ServerConfig` 里显式指定配置文件路径，或用环境变量。

### 坑 2：日志输出

UI 模式用 `UiLogBridge` 把日志推到 UI 面板，Server 模式没有 UI。解决：Server 模式用 Serilog 直接写文件/控制台。

### 坑 3：优雅关闭

`stoppingToken` 触发时，需要：
1. 停止 ScenarioEngine（取消正在执行的步骤）
2. 停止 HostProtocol（断开连接）
3. 停止 SecsGemProtocol（断开 HSMS）
4. 等待所有 Task 完成

最初直接 `return`，导致连接没断开。解决：`ExecuteAsync` 里用 `try/finally`，finally 里依次 Stop。

## 6. 待办

- [ ] 支持多实例并行（多个 EapServerWorker）
- [ ] 支持配置文件热重载（文件修改后自动重新加载）
- [ ] 支持 HTTP 管理接口（启动/停止/查看状态）
- [ ] 支持 Docker 部署

## 7. 变更记录

| 日期 | 内容 |
|---|---|
| 2025-06 | 初始版本：BackgroundService + JSON 配置 |
