# NewLife.OPC 版本更新记录

## v1.1.2026.1024 (2026-10-24)

### 驱动架构升级
- **异步驱动接口**：升级 NewLife.IoT 3.2，`Open`/`Close`/`Read`/`Write` 全面迁移为 `OpenAsync`/`CloseAsync`/`ReadAsync`/`WriteAsync`，与 IoT 标准库异步优先架构对齐。
- **结构化读取结果**：读取返回 `ReadResult`，携带点位值、质量码、时间戳与诊断帧，替代原有裸字典返回。
- **结构化写入结果**：写入返回 `WriteResult`，携带实际成功点数、设备回显值与错误码，批量写入一次完成。
- **OPC UA 会话异步化**：会话创建改为真正的异步等待，消除同步阻塞调用；连接未打开时返回 `ConnectionFailed` 结构化结果。
- **OPC DA 错误处理**：批量读写异常统一转换为 `ReadError`/`WriteError` 错误码并写入日志，不再向调用方抛出底层 COM 异常。

### 工程与依赖
- **解决方案格式**：`NewLife.OPC.sln` 升级为新版 `NewLife.OPC.slnx` 格式，适配新版 .NET SDK 与 Visual Studio。
- **依赖更新**：NewLife.IoT 由 2.7.2025.1112 升级至 3.2.2026.1024，同步使用 NewLife.Core 12.0。
- **跨平台构建**：引入 Microsoft.NETFramework.ReferenceAssemblies，Linux/macOS 环境亦可编译 net461 目标。
- **示例程序**：Test 示例升级至 net10.0 并改用异步 API。

### CI 与发布
- **多渠道发布**：推送 `v*` 标签时同时发布 NuGet.org 与 GitHub Packages，并创建附带 nupkg/snupkg 的 GitHub Release，发布说明自动摘取本文件对应版本段落。
- **持续集成**：构建与测试统一在 Ubuntu 上执行，使用 `NewLife.OPC.slnx` 作为构建入口，并显式声明工作流权限。

### 文档
- **生态版图**：同步 NewLife 生态版图与新生命开发团队介绍。

### 兼容性说明
- 驱动接口已升级至 NewLife.IoT v3，同步方法 `Open`/`Read`/`Write` 被异步接口取代，属于破坏性变更，升级前请调整调用方代码。

---
