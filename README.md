# 联电数据收集（LianDianDataCollection）

WinForm 工控上位机（SCADA）：Modbus TCP 实时通讯 + 批次号管理 + 耐压/气压测试数据上传校验 + SQLite 单文件持久化 + 产品图自动展示 + 作业指导书 + 数据查询。

## 技术栈

- C# LangVersion 10.0 / .NET Framework 4.7.2（Windows x64，需预装该运行时）
- WinForms + SunnyUI 3.7.2（net472，深色扁平工业科技风：UIPanel/UIButton/UIComboBox/UIDatePicker/UIDataGridView 全站主题化）
- NModbus 3.0.83（TCP）
- System.Data.SQLite 2.0.4 + SQLitePCLRaw.lib.e_sqlite3 3.53.3（WAL / FULL）/ log4net 3.3.2
- xunit + Microsoft.NET.Test.Sdk

## 构建与测试

```powershell
dotnet build LianDianDataCollection.slnx -c Release
dotnet test tests/LianDian.Business.Tests/LianDian.Business.Tests.csproj -c Release
```

## 目录

```
src/LianDian.Core      公共层（INI/日志/枚举/模型）
src/LianDian.Comm      Modbus TCP/寄存器映射/快照轮询
src/LianDian.Data      SQLite 单文件建表/迁移/单表仓储/健康监控
src/LianDian.Business  心跳/批次/耐压/气压服务 + SystemManager
src/LianDian.UI        SunnyUI 工业风界面（UIForm/UIPanel/UIButton/UIComboBox/UIDatePicker/UIDataGridView 等）
config/app.ini         运行配置
docs/                  手册/原型/联调报告/红线复核
```

## 红线（禁止变更）

1. PLC 寄存器地址、数据类型、点位定义（`LianDian.Comm/RegisterMap.cs` 唯一事实来源）。
2. 业务判定逻辑与标志位 1/2/3 语义、心跳 0-1、三方比对、同批合并填数。

## 长期运行与五年数据

按每日约 2,000 条、五年约 365 万条设计。主库不自动删除历史记录；每日在线完整备份，默认轮换超过 7 天的自动备份副本（不是删除 7 天前的生产数据）。必须将备份另外复制到独立设备，定期还原验收。

新增：串行定时器、有效期快照、持久化待应答恢复、磁盘/实际写入健康检查、500 条分页、流式 CSV 导出和限量日志。支持 1280×720、1600×900、1920×1080 的 100% 缩放布局；较小屏幕表格横向滚动，不压缩文字。

详见 [深度检测报告](docs/深度检测报告-2026-09-08.md) 与 [部署手册](docs/部署手册.md)。自动测试不是现场连续运行验收；尚须真实 PLC 跑批、断线/断电恢复及 168 小时连续测试。

## 实际 PLC 测试

连接参数位于 `config/app.ini` 的 `[PLC]` 节。当前 `Ip=127.0.0.1` 是占位值，测试前请填入实际 PLC 地址，并确认端口、站号、地址偏移和字符串布局。运行发布程序时使用程序目录内的 `config/app.ini`。

现场检查项目见 `docs/联调报告.md`，点位表见 `docs/PLC点位表.md`。单元测试仅在内存中验证业务逻辑，不监听 Modbus 端口，不连接现场设备。
