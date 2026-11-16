using System.ComponentModel;

namespace LiteSSH.Core;

/// <summary>Two-language UI strings (zh-CN / en-US). Bind with {miuix:Tr Key}.</summary>
public sealed class L10n : INotifyPropertyChanged
{
    private static readonly Dictionary<string, (string Zh, string En)> Map = new()
    {
        ["App.Name"] = ("LiteSSH", "LiteSSH"),
        ["App.Tagline"] = ("轻量 SSH / SFTP 客户端", "Lightweight SSH / SFTP client"),
        ["Nav.Sessions"] = ("会话", "Sessions"),
        ["Nav.Search"] = ("搜索会话…", "Search sessions…"),
        ["Nav.New"] = ("新建会话", "New session"),
        ["Nav.Empty"] = ("还没有会话，点击上方 + 新建", "No sessions yet. Click + above."),
        ["Nav.NoMatch"] = ("没有匹配的会话", "No matching sessions"),
        ["Nav.All"] = ("全部", "All"),
        ["Action.Connect"] = ("连接", "Connect"),
        ["Action.Edit"] = ("编辑", "Edit"),
        ["Action.Duplicate"] = ("创建副本", "Duplicate"),
        ["Action.Delete"] = ("删除", "Delete"),
        ["Action.Save"] = ("保存", "Save"),
        ["Action.Cancel"] = ("取消", "Cancel"),
        ["Action.Close"] = ("关闭", "Close"),
        ["Action.Ok"] = ("确定", "OK"),
        ["Action.Add"] = ("添加", "Add"),
        ["Action.Remove"] = ("移除", "Remove"),
        ["Action.Browse"] = ("浏览…", "Browse…"),
        ["Action.Upload"] = ("上传", "Upload"),
        ["Action.Download"] = ("下载", "Download"),
        ["Action.Refresh"] = ("刷新", "Refresh"),
        ["Action.Rename"] = ("重命名", "Rename"),
        ["Action.NewFolder"] = ("新建文件夹", "New folder"),
        ["Action.OpenSftp"] = ("SFTP", "SFTP"),
        ["Action.Forward"] = ("端口转发", "Forwarding"),
        ["Action.Settings"] = ("设置", "Settings"),
        ["Action.Copy"] = ("复制", "Copy"),
        ["Action.Paste"] = ("粘贴", "Paste"),
        ["Action.SelectAll"] = ("全选", "Select all"),
        ["Action.Clear"] = ("清屏", "Clear"),
        ["Action.Reconnect"] = ("重新连接", "Reconnect"),
        ["Action.Disconnect"] = ("断开连接", "Disconnect"),
        ["Action.CopySelection"] = ("复制选中", "Copy selection"),
        ["Session.Title"] = ("会话", "Session"),
        ["Session.New"] = ("新建会话", "New session"),
        ["Session.Edit"] = ("编辑会话", "Edit session"),
        ["Session.Name"] = ("名称", "Name"),
        ["Session.Group"] = ("分组", "Group"),
        ["Session.Host"] = ("主机地址", "Host"),
        ["Session.Port"] = ("端口", "Port"),
        ["Session.User"] = ("用户名", "Username"),
        ["Session.Auth"] = ("认证方式", "Authentication"),
        ["Session.AuthPassword"] = ("密码", "Password"),
        ["Session.AuthKey"] = ("私钥文件", "Private key"),
        ["Session.Password"] = ("密码", "Password"),
        ["Session.KeyPath"] = ("私钥路径", "Key file"),
        ["Session.KeyPassphrase"] = ("私钥口令（可选）", "Key passphrase (optional)"),
        ["Session.SavePassword"] = ("保存密码", "Save password"),
        ["Session.Color"] = ("标签颜色", "Tag color"),
        ["Session.AutoForwards"] = ("自动启动的转发规则", "Auto-start forwarding"),
        ["Session.HostRequired"] = ("请填写主机地址", "Host is required"),
        ["Session.NameRequired"] = ("请填写会话名称", "Name is required"),
        ["Session.ValidPort"] = ("端口需在 1-65535 之间", "Port must be between 1 and 65535"),
        ["Session.KeyRequired"] = ("请选择私钥文件", "Please choose a private key file"),
        ["Term.Connecting"] = ("正在连接…", "Connecting…"),
        ["Term.Connected"] = ("已连接", "Connected"),
        ["Term.Disconnected"] = ("已断开", "Disconnected"),
        ["Term.Ready"] = ("就绪", "Ready"),
        ["Term.Failed"] = ("连接失败", "Connection failed"),
        ["Term.PressR"] = ("连接已断开，按 Ctrl+Shift+R 重新连接", "Disconnected. Press Ctrl+Shift+R to reconnect."),
        ["Term.SessionClosed"] = ("会话已关闭", "Session closed"),
        ["HostKey.Title"] = ("主机密钥验证", "Host key verification"),
        ["HostKey.Body"] = ("首次连接该主机，请确认指纹与服务器一致：", "First connection to this host. Verify the fingerprint matches the server:"),
        ["HostKey.Changed"] = ("警告：主机密钥已变化，可能存在中间人攻击！", "Warning: host key changed. Possible man-in-the-middle attack!"),
        ["HostKey.Expected"] = ("已保存的指纹", "Saved fingerprint"),
        ["HostKey.Trust"] = ("信任并连接", "Trust and connect"),
        ["Sftp.Path"] = ("远程路径", "Remote path"),
        ["Sftp.Name"] = ("名称", "Name"),
        ["Sftp.Size"] = ("大小", "Size"),
        ["Sftp.Modified"] = ("修改时间", "Modified"),
        ["Sftp.Up"] = ("上级目录", "Parent folder"),
        ["Sftp.Empty"] = ("空目录", "Empty folder"),
        ["Sftp.Loading"] = ("读取目录…", "Loading…"),
        ["Sftp.Transferring"] = ("传输中", "Transferring"),
        ["Sftp.Done"] = ("完成", "Done"),
        ["Sftp.Failed"] = ("失败", "Failed"),
        ["Sftp.DeleteConfirm"] = ("确定删除选中的项目？", "Delete the selected items?"),
        ["Sftp.RenameTitle"] = ("重命名", "Rename"),
        ["Sftp.NewFolderTitle"] = ("新建文件夹", "New folder"),
        ["Sftp.DownloadPrompt"] = ("选择保存位置", "Choose download location"),
        ["Sftp.UploadPrompt"] = ("选择要上传的文件", "Choose files to upload"),
        ["Sftp.Folder"] = ("文件夹", "Folder"),
        ["Forward.Title"] = ("端口转发", "Port forwarding"),
        ["Forward.Name"] = ("名称", "Name"),
        ["Forward.Type"] = ("类型", "Type"),
        ["Forward.Local"] = ("本地转发", "Local"),
        ["Forward.Remote"] = ("远程转发", "Remote"),
        ["Forward.Bind"] = ("监听地址", "Bind address"),
        ["Forward.BindPort"] = ("监听端口", "Bind port"),
        ["Forward.Target"] = ("目标地址", "Target host"),
        ["Forward.TargetPort"] = ("目标端口", "Target port"),
        ["Forward.AutoStart"] = ("连接后自动启动", "Start on connect"),
        ["Forward.Start"] = ("启动", "Start"),
        ["Forward.Stop"] = ("停止", "Stop"),
        ["Forward.Running"] = ("运行中", "Running"),
        ["Forward.Stopped"] = ("未启动", "Stopped"),
        ["Forward.NeedConnection"] = ("请先建立连接", "Connect first"),
        ["Forward.Empty"] = ("暂无转发规则", "No forwarding rules"),
        ["Forward.Required"] = ("请填写名称、地址与端口", "Name, address and port are required"),
        ["Settings.Title"] = ("设置", "Settings"),
        ["Settings.Appearance"] = ("外观", "Appearance"),
        ["Settings.Terminal"] = ("终端", "Terminal"),
        ["Settings.About"] = ("关于", "About"),
        ["Settings.Theme"] = ("主题", "Theme"),
        ["Settings.ThemeAuto"] = ("跟随系统", "System"),
        ["Settings.ThemeLight"] = ("浅色", "Light"),
        ["Settings.ThemeDark"] = ("深色", "Dark"),
        ["Settings.Accent"] = ("强调色", "Accent color"),
        ["Settings.Language"] = ("界面语言", "Language"),
        ["Settings.Font"] = ("字体", "Font"),
        ["Settings.FontSize"] = ("字号", "Font size"),
        ["Settings.Scrollback"] = ("回滚行数", "Scrollback"),
        ["Settings.TermTheme"] = ("终端配色", "Terminal colors"),
        ["Settings.CopyOnSelect"] = ("选中即复制", "Copy on select"),
        ["Settings.ConfirmClose"] = ("关闭标签页时确认", "Confirm tab close"),
        ["Settings.SftpDefault"] = ("连接后自动打开 SFTP 面板", "Open SFTP panel on connect"),
        ["Settings.Storage"] = ("数据存储", "Data storage"),
        ["Settings.StorageHint"] = ("会话与设置经 DPAPI 加密后保存在程序目录内的 LiteSSH.dat，不写注册表、不写用户目录。", "Sessions and settings are DPAPI-encrypted into LiteSSH.dat in the app folder. Nothing is written to the registry or your profile."),
        ["Settings.Version"] = ("版本", "Version"),
        ["Settings.MadeWith"] = ("基于 SSH.NET · Miuix 设计语言", "Built on SSH.NET · Miuix design language"),
        ["Common.Tip"] = ("提示", "Notice"),
        ["Common.Error"] = ("错误", "Error"),
        ["Common.Warning"] = ("警告", "Warning"),
        ["Common.Confirm"] = ("确认", "Confirm"),
        ["Common.Size"] = ("大小", "Size"),
        ["Common.Path"] = ("路径", "Path"),
        ["Conn.Error"] = ("连接失败：", "Connection failed: "),
        ["Conn.Fingerprint"] = ("指纹", "Fingerprint"),
        ["Conn.ConnectingTo"] = ("正在连接 {0}…", "Connecting to {0}…"),
        ["Tabs.NewTab"] = ("新标签页", "New tab"),
        ["Tabs.Welcome"] = ("选择一个会话开始，或新建会话", "Pick a session to start, or create one"),
    };

    private static L10n? _instance;

    public static L10n I => _instance ??= new L10n();

    public static event EventHandler? LanguageChanged;

    public static string Lang => AppState.Current.Settings.Language;

    public static bool IsEnglish => Lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public string this[string key]
    {
        get
        {
            if (Map.TryGetValue(key, out var pair))
            {
                return IsEnglish ? pair.En : pair.Zh;
            }
            return key;
        }
    }

    public static string T(string key) => I[key];

    public static string F(string key, params object[] args) => string.Format(I[key], args);

    public static void SetLanguage(string lang)
    {
        AppState.Current.Settings.Language = lang;
        AppState.Current.Save();
        I.PropertyChanged?.Invoke(I, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
