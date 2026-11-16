# LiteSSH

> 一个用 .NET 6 + WPF 重写的轻量 SSH / SFTP 桌面客户端 —— 单文件、绿色免安装、界面遵循 Miuix 设计语言。

LiteSSH 源自对 [EasySSH](https://github.com/NeoHeee/EasySSH)（Rust + Tauri + WebView 实现）的功能重构：
保留核心的 SSH 终端与文件传输体验，用原生 .NET 重写全部内核与界面，去掉 WebView、Electron 与安装器，
最终产物是**一个可执行文件**，整个程序文件夹拷走即可使用。

---

## 特性

### 终端
- **自研 VT100 / xterm 终端内核**：并非包装嵌入式浏览器，而是逐字节解析转义序列并自绘字符网格
- 支持 8 / 16 / 256 色与 24 位真彩色、粗体/斜体/下划线/反显、光标闪烁、自动换行
- 主屏与备用屏切换（`vim`、`less`、`htop` 正常显示）、滚动区域、滚动缓冲（默认 2000 行，可调至 10000）
- 中日韩宽字符按 Unicode EAW 规则双宽渲染
- 括号粘贴模式（Bracketed Paste）、应用光标键模式（`DECCKM`）、`OSC 52` 剪贴板写入、`OSC 0/2` 标题同步
- 高频输出按 16ms 合帧刷新，`cat` 大文件不卡顿
- 双击选词、三击整行、拖拽自动滚动、Shift 扩展选区

### 连接与安全
- 密码认证：同时尝试 `password` 与 `keyboard-interactive`，兼容各类 PAM 配置
- 私钥认证：支持 OpenSSH / PEM 私钥与私钥口令，`~` 与环境变量自动展开
- **主机指纹校验**：首次连接弹窗确认 `SHA256` 指纹，之后每次连接严格比对，指纹变化即拒绝（防中间人）
- 会话与密码通过 Windows DPAPI 加密后写入程序目录，拷走到别的机器无法解密

### SFTP
- 与终端并排的双栏布局，可拖动分隔条调整宽度
- 目录浏览、上级跳转、路径直达、双击进入
- 上传 / 下载、新建文件夹、重命名、删除（支持多选）
- 传输进度实时显示

### 端口转发
- 本地转发（`-L`）与远程转发（`-R`）
- 规则随会话保存，可设置「连接后自动启动」，断开时统一释放

### 会话管理
- 多标签页、分组、标签颜色、搜索过滤
- 复制会话、右键菜单快速连接 / 打开 SFTP
- 支持命令行直达：`LiteSSH.exe --connect "会话名"`

### 界面
- 无边框圆角窗口 + 自绘标题栏，缩放动画与系统一致
- 深浅主题（可跟随系统）、自定义强调色
- 5 套终端配色：Miuix 深色 / Miuix 浅色 / Solarized Dark / One Dark / Nord
- 字体与字号可调，全系统字体列表可选
- 简体中文 / English 双语，运行时切换即时生效

---

## 快速开始

### 环境要求

| 项目 | 版本 |
| --- | --- |
| Windows | 10 1809 或更高（Windows 11 已测） |
| .NET SDK | 6.0（构建用） |
| .NET Desktop Runtime | 6.0（运行单文件版用，Win11 一般已自带） |

> 说明：本项目按需求以 `net6.0-windows` 为目标框架。.NET 6 已结束官方支持，
> 如需长期支持版本，把 `TargetFramework` 改为 `net8.0-windows` 即可，代码无需改动。

### 构建

```powershell
dotnet build .\src\LiteSSH\LiteSSH.csproj -c Debug
```

产物：`src\LiteSSH\bin\Debug\net6.0-windows\LiteSSH.exe`

### 打包单文件

```powershell
# 依赖系统 .NET 运行时，体积最小（约 7.5 MB）
dotnet publish .\src\LiteSSH\LiteSSH.csproj -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=false -o .\dist

# 自带运行时，任何 Win10+ 直接运行（体积较大）
dotnet publish .\src\LiteSSH\LiteSSH.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o .\dist\selfcontained
```

### VS Code

仓库已附带 `/.vscode`，打开文件夹即可使用：

| 操作 | 方式 |
| --- | --- |
| 构建 | `Ctrl+Shift+B`（默认生成任务 `build`） |
| 运行 / 调试 | `F5` → `LiteSSH (Debug)`（自动先构建再启动） |
| 打包单文件 | 命令面板 → `Tasks: Run Task` → `publish` |
| 附加调试 | 先启动程序，再选 `LiteSSH (Attach)` |

---

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl+N` | 新建会话 |
| `Ctrl+W` | 关闭当前标签页 |
| `Ctrl+Tab` | 切换标签页 |
| `Ctrl+,` | 打开设置 |
| `Ctrl+C` | 有选区时复制，无选区时发送 `SIGINT` |
| `Ctrl+V` / `Shift+Insert` | 粘贴 |
| `Ctrl+Shift+C` | 强制复制选区 |
| `Ctrl+Shift+A` | 全选终端内容 |
| `Ctrl+Shift+R` | 重新连接 |
| `Esc` | 清除选区 |
| `PageUp` / `PageDown` | 翻看滚动缓冲 |
| 鼠标滚轮 | 滚动历史输出 |
| 右键 | 有选区则复制，无选区则粘贴 |
| `Shift` + 右键 | 打开上下文菜单 |
| 中键 | 粘贴 |
| 双击 / 三击 | 选中单词 / 整行 |

---

## 数据存储

LiteSSH 严格遵循「绿色便携」原则：

- **所有状态只写一个文件**：`LiteSSH.dat`，与 `LiteSSH.exe` 同目录
- 内容为 JSON 经 **Windows DPAPI（当前用户）** 加密后的密文，密码不会以明文落盘
- **不写注册表、不写 `%AppData%`、不写临时目录、不创建日志**
- 删除 `LiteSSH.dat` 即彻底恢复出厂状态

> 若把程序放在 `Program Files` 等只读目录，启动时会提示无法保存配置，请移动到可写目录使用。

---

## 项目结构

```
LiteSSH/
├─ .vscode/                 VS Code 构建与调试配置
├─ src/LiteSSH/
│  ├─ Core/                 数据模型、DPAPI 存储、SSH/SFTP/转发封装
│  │  ├─ Models.cs
│  │  ├─ L10n.cs            中英文词典
│  │  └─ Ssh.cs
│  ├─ Terminal/             VT 解析器、字符网格、渲染控件、配色
│  │  ├─ TermScreen.cs
│  │  ├─ TerminalEmulator.cs
│  │  ├─ TerminalView.cs
│  │  └─ TerminalPalette.cs
│  ├─ Miuix/                设计语言组件：窗口、对话框、主题、转换器
│  ├─ Themes/               Miuix.xaml（控件样式）/ Light.xaml / Dark.xaml
│  ├─ Views/                MainWindow、TerminalPane、SftpPanel、各对话框
│  └─ Assets/               图标
├─ THANKS.md                鸣谢
└─ LICENSE                  MIT
```

### 实现要点

- **终端渲染**：`TermScreen` 维护 `Cell[]` 线性缓冲，`TerminalEmulator` 是逐字符状态机
  （Ground / Escape / CSI / OSC / DCS），`TerminalView` 以等宽单元格为单位合并同色文本段后绘制，
  并按字形测量计算列宽，保证等宽对齐。
- **SSH 层**：SSH.NET 的 `ShellStream` 承载 PTY；窗口尺寸变化通过反射调用底层
  `SendWindowChangeRequest`（该 API 未公开），使 `vim` 等全屏程序随窗口正确重排。
- **粘贴可见性**：正确处理「待换行（pending wrap）」与 `CR` 的关系，避免 shell 重绘整行时多出空行。

---

## 常见问题

**连接后终端没有反应？**
确认服务器允许 PTY 分配，且用户名/密码正确；错误信息会直接打印在终端中。

**提示「主机密钥已变化」？**
说明服务器重装或更换了密钥，也可能是中间人攻击。确认无误后点击「信任并连接」更新指纹。

**能输入中文吗？**
可以，终端按 UTF-8 收发；宽字符按双宽渲染，不会错位。

**为什么不内置密钥生成器？**
保持体积与攻击面最小。用 `ssh-keygen` 生成后在此选择私钥文件即可。

---

## 鸣谢

第三方库、设计语言与灵感来源详见 [THANKS.md](./THANKS.md)。

## 许可证

[MIT](./LICENSE)

---

## English

**LiteSSH** is a lightweight SSH / SFTP client for Windows, rewritten from
[EasySSH](https://github.com/NeoHeee/EasySSH) in **.NET 6 + WPF** — no WebView, no installer,
no registry writes.

- Custom VT100/xterm emulator (256 / true color, alt screen, wide CJK, bracketed paste)
- Tabbed terminal, SFTP dual-pane, local / remote port forwarding
- Host key verification (`SHA256`) with strict mismatch rejection
- Sessions & secrets encrypted with Windows DPAPI into a single `LiteSSH.dat` next to the exe
- Portable: copy the folder anywhere; delete the `.dat` to reset
- Single-file build ≈ 7.5 MB (framework-dependent), Miuix-inspired UI, zh-CN / en-US

Build: `dotnet build .\src\LiteSSH\LiteSSH.csproj -c Debug` · or press `Ctrl+Shift+B` in VS Code.
