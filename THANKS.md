# 鸣谢 / Acknowledgements

LiteSSH 的诞生离不开这些开源项目与创作者，在此致以诚挚感谢。

## 核心依赖

| 项目 | 用途 | 许可证 |
| --- | --- | --- |
| [SSH.NET](https://github.com/sshnet/SSH.NET) | SSH / SFTP 协议栈与端口转发实现 | MIT |
| [BouncyCastle.Cryptography](https://www.bouncycastle.org/csharp/) | SSH.NET 使用的密码学原语（Ed25519 等） | MIT |
| [.NET 6 / WPF](https://github.com/dotnet/wpf) | 运行时与桌面 UI 框架 | MIT |

## 设计语言

| 项目 | 用途 | 许可证 |
| --- | --- | --- |
| [Miuix](https://github.com/miuix-kotlin-multiplatform/miuix) | 界面设计语言参考：圆角卡片、药丸按钮、分组列表、滑动开关等视觉规范 | Apache-2.0 |

> 说明：Miuix 官方仅提供 Android / Compose Multiplatform 实现，并没有 .NET 版本。
> LiteSSH 依据其公开的设计规范（配色层级、圆角半径、控件形态、间距节奏），在 WPF 中自行实现了
> 一套主题库 [Themes/Miuix.xaml](./src/LiteSSH/Themes/Miuix.xaml)，未复制其任何源代码。

## 参考项目

| 项目 | 说明 |
| --- | --- |
| [EasySSH](https://github.com/NeoHeee/EasySSH) | LiteSSH 的功能与交互参考来源（Rust + Tauri 实现）。LiteSSH 为独立的 .NET 重写版本，仅借鉴其产品功能与交互思路，不复用其代码 |

## 终端协议参考

- [XTerm Control Sequences](https://invisible-island.net/xterm/ctlseqs/ctlseqs.html) — 转义序列行为依据
- [ECMA-48](https://ecma-international.org/publications-and-standards/standards/ecma-48/) — 控制功能标准
- [Unicode 标准附件 #11](https://www.unicode.org/reports/tr11/) — 东亚宽字符宽度规则

## 字体

- `Cascadia Mono`、`Segoe Fluent Icons`、`Microsoft YaHei UI` — 随 Windows / Windows Terminal 提供，LiteSSH 仅引用系统字体，不分发字体文件

## 特别感谢

- 所有在 [EasySSH](https://github.com/NeoHeee/EasySSH) 提交反馈、报告缺陷的用户，他们的经验塑造了 LiteSSH 的功能取舍
- 以及每一位愿意给这个项目提 Issue、提 PR 的你
