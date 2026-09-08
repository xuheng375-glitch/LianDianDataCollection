# UI 视觉原型说明

- 原型文件：[UI-原型.html](UI-原型.html)（1920×1080 深色玻璃拟态仪表盘）
- 预览图：[UI-原型.png](UI-原型.png)
- 设计系统：深空渐变底 + 光斑纹理、半透明圆角玻璃面板（白 5–9% + 1px 高光边）、电光青/蓝 + 紫霓虹强调、OK `#34D399` / NG `#F87171` / 警示 `#FBBF24`、Consolas 大数值、微软雅黑 UI 中文。
- WinForms 实现对照：`src/LianDian.UI`（`GlassPanel`/`GlassButton`/`GlassStatusLamp` + `ThemeConfig` 色板，DWM 背景模糊尽力 + 渐变纹理兜底）。
