using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SteamToolManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow());
    }
}

internal sealed class ManagedGame
{
    public string AppId { get; set; } = "";
    public string Label { get; set; } = "";
    public string File { get; set; } = "";
    public string FullPath { get; set; } = "";
}

internal sealed class ScriptItem
{
    public string Path { get; set; } = "";
    public override string ToString() => System.IO.Path.GetFileName(Path);
}

internal sealed class InstallState
{
    public string SteamPath { get; set; } = "";
    public string BackupDirectory { get; set; } = "";
    public Dictionary<string, string> InstalledHashes { get; set; } = new();
    public List<string> OriginalFiles { get; set; } = new();
}

internal sealed class MainWindow : Form
{
    private static readonly string[] RequiredDlls = { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" };
    private static readonly Color Ink = Color.FromArgb(30, 44, 63);
    private static readonly Color Muted = Color.FromArgb(96, 109, 127);
    private static readonly Color Accent = Color.FromArgb(41, 107, 204);
    private static readonly Color Surface = Color.FromArgb(247, 249, 252);
    private static readonly Color Border = Color.FromArgb(219, 225, 234);
    private const string TomlName = "opensteamtool.toml";
    private const string DefaultToml = "[log]\r\nlevel = \"info\"\r\n\r\n[manifest]\r\nurl = \"opensteamtool\"\r\n\r\n[stats]\r\nenable_api = false\r\n\r\n[inject]\r\nenabled = false\r\n";

    private readonly string AppDataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamToolManager");
    private string steamPath = "";
    private string selectedScript = "";
    private bool changingSelection;
    private TextBox steamInput = null!;
    private Label steamStatus = null!;
    private Label dllStatus = null!;
    private Label stateStatus = null!;
    private Label footer = null!;
    private DataGridView gameTable = null!;
    private ListBox scripts = null!;
    private RichTextBox scriptEditor = null!;
    private TextBox tomlEditor = null!;
    private ListBox logFiles = null!;
    private RichTextBox logViewer = null!;

    internal MainWindow()
    {
        Text = "Steam Tool Manager";
        ClientSize = new Size(1030, 720);
        MinimumSize = new Size(950, 670);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Surface;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

        var header = new Panel { Dock = DockStyle.Top, Height = 97, BackColor = Color.White, Padding = new Padding(24, 13, 24, 13) };
        header.Controls.Add(Label("Steam Tool Manager", 24, 12, 760, 39, 20, Ink, true));
        header.Controls.Add(Label("Local OpenSteamTool setup and Lua configuration  •  No sign-in required", 25, 53, 850, 26, 9, Muted));
        header.Paint += (_, e) => e.Graphics.DrawLine(new Pen(Border), 0, header.Height - 1, header.Width, header.Height - 1);

        footer = Label("Ready. This utility is unofficial and does not grant game ownership.", 0, 0, 200, 24, 8.5f, Muted);
        var footerPanel = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = Color.White, Padding = new Padding(20, 5, 8, 4) };
        footer.Dock = DockStyle.Fill;
        footerPanel.Controls.Add(footer);
        footerPanel.Paint += (_, e) => e.Graphics.DrawLine(new Pen(Border), 0, 0, footerPanel.Width, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8), Font = new Font("Segoe UI", 10, FontStyle.Regular) };
        tabs.TabPages.Add(BuildHome());
        tabs.TabPages.Add(BuildGames());
        tabs.TabPages.Add(BuildScripts());
        tabs.TabPages.Add(BuildSettings());
        tabs.TabPages.Add(BuildLogs());
        tabs.SelectedIndexChanged += (_, _) => RefreshCurrentTab(tabs.SelectedIndex);

        Controls.Add(tabs);
        Controls.Add(footerPanel);
        Controls.Add(header);
        steamPath = DetectSteam();
        steamInput.Text = steamPath;
        RefreshAll();
    }

    private static Label Label(string text, int x, int y, int w, int h, float size = 10, Color? color = null, bool bold = false)
        => new() { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = color ?? Ink,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };

    private static Button Btn(string text, int x, int y, int w, int h, bool primary = false)
    {
        var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h),
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
        return b;
    }

    private static TextBox TextField(int x, int y, int w, int h, bool mono = false)
        => new() { Location = new Point(x, y), Size = new Size(w, h), BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(mono ? "Consolas" : "Segoe UI", mono ? 10 : 10, FontStyle.Regular) };

    private static Panel Card(int x, int y, int w, int h)
    {
        var c = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        c.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(Border), 0, 0, c.Width - 1, c.Height - 1);
        return c;
    }

    private static TabPage Page(string name) => new(name) { BackColor = Surface, Padding = new Padding(0) };

    private TabPage BuildHome()
    {
        var tab = Page("Dashboard");
        tab.Controls.Add(Label("Steam installation", 24, 17, 500, 32, 15, Ink, true));
        var c1 = Card(24, 57, 965, 172);
        c1.Controls.Add(Label("Steam directory", 17, 10, 780, 27, 9, Muted));
        steamInput = TextField(17, 43, 630, 31);
        steamInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        c1.Controls.Add(steamInput);
        var browse = Btn("Browse…", 665, 41, 120, 35);
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += (_, _) => BrowseSteam();
        c1.Controls.Add(browse);
        var refresh = Btn("Refresh", 801, 41, 135, 35, true);
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Click += (_, _) => { steamPath = steamInput.Text.Trim(); RefreshAll(); };
        c1.Controls.Add(refresh);
        steamStatus = Label("", 17, 87, 920, 25, 9.5f);
        c1.Controls.Add(steamStatus);
        var open = Btn("Open Steam folder", 17, 119, 179, 34);
        open.Click += (_, _) => OpenDirectory(steamPath);
        c1.Controls.Add(open);
        var openLua = Btn("Open Lua folder", 209, 119, 164, 34);
        openLua.Click += (_, _) => OpenDirectory(LuaDirectory);
        c1.Controls.Add(openLua);
        tab.Controls.Add(c1);

        tab.Controls.Add(Label("OpenSteamTool components", 24, 247, 600, 32, 15, Ink, true));
        var c2 = Card(24, 287, 965, 193);
        c2.Controls.Add(Label("Checks the files already present in your Steam directory.", 17, 9, 870, 28, 9, Muted));
        dllStatus = Label("", 17, 42, 920, 30, 11, Ink, true);
        c2.Controls.Add(dllStatus);
        stateStatus = Label("", 17, 76, 920, 23, 9.25f, Muted);
        c2.Controls.Add(stateStatus);
        var install = Btn("Install from DLL folder", 17, 124, 225, 38, true);
        install.Click += (_, _) => InstallDlls();
        c2.Controls.Add(install);
        var uninstall = Btn("Remove / restore files", 252, 124, 211, 38);
        uninstall.Click += (_, _) => UninstallDlls();
        c2.Controls.Add(uninstall);
        tab.Controls.Add(c2);

        tab.Controls.Add(Label("Utilities", 24, 495, 500, 32, 15, Ink, true));
        var c3 = Card(24, 535, 965, 85);
        var backup = Btn("Back up Lua + settings", 17, 23, 230, 38);
        backup.Click += (_, _) => BackupConfigs();
        c3.Controls.Add(backup);
        c3.Controls.Add(Label("DLL changes require confirmation; original files are backed up when installing.", 273, 21, 630, 42, 9.25f, Muted));
        tab.Controls.Add(c3);
        return tab;
    }

    private TabPage BuildGames()
    {
        var tab = Page("Game entries");
        tab.Controls.Add(Label("Game entries", 24, 18, 650, 32, 15, Ink, true));
        tab.Controls.Add(Label("Manage AppIDs in separate Lua files. Entries are local settings, not Steam purchases.", 24, 50, 900, 27, 9, Muted));
        var add = Btn("+ Add AppID", 24, 88, 146, 38, true);
        add.Click += (_, _) => AddGame();
        tab.Controls.Add(add);
        var remove = Btn("Remove selected", 184, 88, 165, 38);
        remove.Click += (_, _) => RemoveGame();
        tab.Controls.Add(remove);
        var open = Btn("Open Lua file", 363, 88, 148, 38);
        open.Click += (_, _) => OpenGameFile();
        tab.Controls.Add(open);
        var refresh = Btn("Refresh", 525, 88, 116, 38);
        refresh.Click += (_, _) => RefreshGames();
        tab.Controls.Add(refresh);
        gameTable = new DataGridView
        {
            Location = new Point(24, 149), Size = new Size(964, 438), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowTemplate = { Height = 33 }
        };
        gameTable.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "AppId", HeaderText = "App ID", FillWeight = 20 });
        gameTable.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Label", HeaderText = "Label", FillWeight = 39 });
        gameTable.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "File", HeaderText = "Lua file", FillWeight = 41 });
        gameTable.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 244, 250);
        gameTable.EnableHeadersVisualStyles = false;
        gameTable.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 235, 255);
        gameTable.DefaultCellStyle.SelectionForeColor = Ink;
        tab.Controls.Add(gameTable);
        return tab;
    }

    private TabPage BuildScripts()
    {
        var tab = Page("Lua scripts");
        tab.Controls.Add(Label("Lua script editor", 24, 18, 650, 32, 15, Ink, true));
        tab.Controls.Add(Label("Edits files in Steam\\config\\lua. Scripts are not executed by this manager.", 24, 49, 900, 27, 9, Muted));
        scripts = new ListBox { Location = new Point(24, 91), Size = new Size(250, 468),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.25f) };
        scripts.SelectedIndexChanged += (_, _) => LoadSelectedScript();
        tab.Controls.Add(scripts);
        scriptEditor = new RichTextBox { Location = new Point(291, 91), Size = new Size(697, 468),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 10), WordWrap = false, AcceptsTab = true, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White };
        tab.Controls.Add(scriptEditor);
        var newScript = Btn("New script", 24, 570, 143, 37);
        newScript.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        newScript.Click += (_, _) => NewScript();
        tab.Controls.Add(newScript);
        var refresh = Btn("Refresh list", 178, 570, 143, 37);
        refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        refresh.Click += (_, _) => RefreshScripts();
        tab.Controls.Add(refresh);
        var save = Btn("Save selected script", 810, 570, 179, 37, true);
        save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        save.Click += (_, _) => SaveScript();
        tab.Controls.Add(save);
        return tab;
    }

    private TabPage BuildSettings()
    {
        var tab = Page("Settings");
        tab.Controls.Add(Label("OpenSteamTool configuration", 24, 18, 700, 32, 15, Ink, true));
        tab.Controls.Add(Label("Edit opensteamtool.toml. If absent, defaults are shown until you save.", 24, 50, 920, 27, 9, Muted));
        tomlEditor = new TextBox { Location = new Point(24, 90), Size = new Size(965, 468),
            Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 10), BorderStyle = BorderStyle.FixedSingle };
        tab.Controls.Add(tomlEditor);
        var reload = Btn("Reload from disk", 24, 571, 184, 37);
        reload.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        reload.Click += (_, _) => RefreshToml();
        tab.Controls.Add(reload);
        var save = Btn("Save TOML", 829, 571, 159, 37, true);
        save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        save.Click += (_, _) => SaveToml();
        tab.Controls.Add(save);
        return tab;
    }

    private TabPage BuildLogs()
    {
        var tab = Page("Logs");
        tab.Controls.Add(Label("Diagnostic logs", 24, 18, 600, 32, 15, Ink, true));
        tab.Controls.Add(Label("Debug builds may write logs to Steam\\opensteamtool. No logs is normal for Release builds.", 24, 50, 940, 27, 9, Muted));
        logFiles = new ListBox { Location = new Point(24, 91), Size = new Size(250, 468),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.25f) };
        logFiles.SelectedIndexChanged += (_, _) => LoadLog();
        tab.Controls.Add(logFiles);
        logViewer = new RichTextBox { Location = new Point(291, 91), Size = new Size(697, 468),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true, WordWrap = false, BackColor = Color.White };
        tab.Controls.Add(logViewer);
        var refresh = Btn("Refresh logs", 24, 571, 169, 37);
        refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        refresh.Click += (_, _) => RefreshLogs();
        tab.Controls.Add(refresh);
        var open = Btn("Open log directory", 205, 571, 194, 37);
        open.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        open.Click += (_, _) => OpenDirectory(Path.Combine(steamPath, "opensteamtool"));
        tab.Controls.Add(open);
        return tab;
    }

    private string LuaDirectory => Path.Combine(steamPath, "config", "lua");
    private string StatePath => Path.Combine(AppDataDir, "install-state.json");
    private string ConfigPath => Path.Combine(steamPath, TomlName);
    private bool SteamValid => !string.IsNullOrWhiteSpace(steamPath) && File.Exists(Path.Combine(steamPath, "steam.exe"));

    private static string DetectSteam()
    {
        var candidates = new List<string>();
        foreach (var key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
        {
            foreach (var valueName in new[] { "SteamPath", "InstallPath" })
            {
                try { if (Registry.GetValue(key, valueName, null) is string value) candidates.Add(value); }
                catch { /* Registry permissions vary. */ }
            }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        candidates.Add(@"C:\Program Files (x86)\Steam");
        return candidates.Select(x => x.Replace('/', '\\')).FirstOrDefault(x => File.Exists(Path.Combine(x, "steam.exe"))) ?? "";
    }

    private void BrowseSteam()
    {
        using var picker = new FolderBrowserDialog { Description = "Select folder containing steam.exe", ShowNewFolderButton = false };
        if (Directory.Exists(steamInput.Text)) picker.SelectedPath = steamInput.Text;
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            steamPath = picker.SelectedPath;
            steamInput.Text = steamPath;
            RefreshAll();
        }
    }

    private void RefreshAll()
    {
        steamPath = steamInput.Text.Trim();
        steamStatus.Text = SteamValid ? "✓ Steam.exe detected." : "Steam.exe not detected. Select the correct folder first.";
        steamStatus.ForeColor = SteamValid ? Color.FromArgb(29, 129, 76) : Color.FromArgb(164, 90, 26);
        int present = RequiredDlls.Count(x => File.Exists(Path.Combine(steamPath, x)));
        dllStatus.Text = $"{present} / {RequiredDlls.Length} required DLL files found";
        dllStatus.ForeColor = present == RequiredDlls.Length ? Color.FromArgb(29, 129, 76) : Ink;
        stateStatus.Text = string.Join("    ", RequiredDlls.Select(x => (File.Exists(Path.Combine(steamPath, x)) ? "✓ " : "– ") + x));
        RefreshGames();
        RefreshScripts();
        RefreshToml();
        RefreshLogs();
        footer.Text = SteamValid ? $"Connected to {steamPath}" : "Choose your Steam installation folder to start.";
    }

    private void RefreshCurrentTab(int selectedIndex)
    {
        if (selectedIndex == 1) RefreshGames();
        else if (selectedIndex == 2) RefreshScripts();
        else if (selectedIndex == 3) RefreshToml();
        else if (selectedIndex == 4) RefreshLogs();
    }

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private bool ConfirmSteamClosed()
    {
        if (!SteamValid) { Warn("Choose a valid Steam installation directory first."); return false; }
        if (Process.GetProcessesByName("steam").Length > 0)
        {
            Warn("Close Steam completely, including the system tray, before changing DLL files.");
            return false;
        }
        return true;
    }

    private InstallState ReadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<InstallState>(File.ReadAllText(StatePath)) : null; }
        catch { return null; }
    }

    private void InstallDlls()
    {
        if (!ConfirmSteamClosed()) return;
        if (ReadState() is { } prior && Directory.Exists(prior.SteamPath))
        {
            Warn("This manager already has an install record. Use 'Remove / restore files' first to prevent overwriting backups.");
            return;
        }
        using var picker = new FolderBrowserDialog { Description = "Choose extracted RELEASE folder with the three DLL files", ShowNewFolderButton = false };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var source = picker.SelectedPath;
        var missing = RequiredDlls.Where(n => !File.Exists(Path.Combine(source, n))).ToArray();
        if (missing.Length != 0) { Warn("Missing files from selected folder:\n" + string.Join("\n", missing)); return; }
        if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(steamPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { Warn("Source and destination cannot be the same folder."); return; }
        var message = "This will copy three third-party DLLs into the Steam directory. " +
            "Steam may load them when it starts. These files are not verified by this utility. " +
            "Only proceed if you trust the source, understand account/security risks, and have closed Steam.\n\nContinue?";
        if (MessageBox.Show(this, message, "Confirm DLL installation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        var backupDir = Path.Combine(AppDataDir, "backups", "components", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        var state = new InstallState { SteamPath = Path.GetFullPath(steamPath), BackupDirectory = backupDir };
        var installed = new List<string>();
        try
        {
            Directory.CreateDirectory(backupDir);
            foreach (var name in RequiredDlls)
            {
                var dst = Path.Combine(steamPath, name);
                if (!File.Exists(dst)) continue;
                File.Copy(dst, Path.Combine(backupDir, name), true);
                state.OriginalFiles.Add(name);
            }
            foreach (var name in RequiredDlls)
            {
                var dst = Path.Combine(steamPath, name);
                File.Copy(Path.Combine(source, name), dst, true);
                installed.Add(name);
                state.InstalledHashes[name] = FileHash(dst);
            }
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            RefreshAll();
            Info("DLLs installed. Existing files (if any) were backed up to:\n" + backupDir);
        }
        catch (Exception ex)
        {
            foreach (var name in installed)
            {
                try
                {
                    var dst = Path.Combine(steamPath, name);
                    if (state.OriginalFiles.Contains(name)) File.Copy(Path.Combine(backupDir, name), dst, true);
                    else File.Delete(dst);
                }
                catch { /* Report the backup directory for manual recovery. */ }
            }
            Error("Installation failed: " + ex.Message + "\nBackups, if present: " + backupDir);
        }
    }

    private void UninstallDlls()
    {
        if (!ConfirmSteamClosed()) return;
        var state = ReadState();
        if (state == null || !string.Equals(Path.GetFullPath(state.SteamPath), Path.GetFullPath(steamPath), StringComparison.OrdinalIgnoreCase))
        {
            Warn("No matching installation record from this manager. For safety, files installed by another tool will not be deleted.");
            return;
        }
        if (MessageBox.Show(this, "Remove DLLs installed by this manager and restore original backed-up files?", "Confirm removal",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            var skipped = new List<string>();
            foreach (var name in RequiredDlls)
            {
                string file = Path.Combine(steamPath, name);
                if (File.Exists(file) && (!state.InstalledHashes.TryGetValue(name, out var hash) || FileHash(file) != hash))
                {
                    skipped.Add(name + " (modified since installation)");
                    continue;
                }
                if (File.Exists(file)) File.Delete(file);
                if (state.OriginalFiles.Contains(name))
                {
                    string original = Path.Combine(state.BackupDirectory, name);
                    if (File.Exists(original)) File.Copy(original, file, true);
                    else skipped.Add(name + " (original backup missing)");
                }
            }
            if (skipped.Count == 0) File.Delete(StatePath);
            RefreshAll();
            Info(skipped.Count == 0 ? "Installed DLLs removed; originals restored where present." :
                "Some files were deliberately left unchanged:\n" + string.Join("\n", skipped) + "\nResolve them manually before trying again.");
        }
        catch (Exception ex) { Error("Removal failed: " + ex.Message); }
    }

    private void RefreshGames()
    {
        var result = new BindingList<ManagedGame>();
        if (Directory.Exists(LuaDirectory))
        {
            foreach (var file in Directory.GetFiles(LuaDirectory, "ost_manager_*.lua").OrderBy(x => x))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var match = Regex.Match(content, @"(?im)^\s*addappid\s*\(\s*(\d+)\s*\)");
                    if (!match.Success) continue;
                    var name = Regex.Match(content, @"(?im)^\s*--\s*Name:\s*(.+)$");
                    result.Add(new ManagedGame { AppId = match.Groups[1].Value,
                        Label = name.Success ? name.Groups[1].Value.Trim() : "(unnamed)", File = Path.GetFileName(file), FullPath = file });
                }
                catch { /* Ignore unreadable user scripts. */ }
            }
        }
        gameTable.DataSource = result;
        gameTable.ClearSelection();
    }

    private void AddGame()
    {
        if (!SteamValid) { Warn("Choose a valid Steam installation directory first."); return; }
        using var dialog = new Form { Text = "Add AppID", ClientSize = new Size(460, 235),
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent,
            Font = Font, BackColor = Color.White };
        dialog.Controls.Add(Label("Steam App ID (numbers only)", 20, 19, 390, 25, 9, Ink, true));
        var id = TextField(20, 49, 416, 31);
        dialog.Controls.Add(id);
        dialog.Controls.Add(Label("Label (optional)", 20, 89, 390, 25, 9, Ink, true));
        var title = TextField(20, 119, 416, 31);
        dialog.Controls.Add(title);
        var cancel = Btn("Cancel", 223, 174, 100, 35);
        cancel.DialogResult = DialogResult.Cancel;
        dialog.Controls.Add(cancel);
        var save = Btn("Add entry", 335, 174, 101, 35, true);
        save.DialogResult = DialogResult.OK;
        dialog.Controls.Add(save);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var appId = id.Text.Trim();
        if (!uint.TryParse(appId, out var numericId) || numericId == 0 || !Regex.IsMatch(appId, @"^\d+$"))
        { Warn("Enter a valid positive numeric Steam AppID."); return; }
        var normalizedId = numericId.ToString();
        var filename = Path.Combine(LuaDirectory, $"ost_manager_{normalizedId}.lua");
        if (File.Exists(filename)) { Warn("That AppID already has a managed Lua file."); return; }
        try
        {
            Directory.CreateDirectory(LuaDirectory);
            string safeLabel = Regex.Replace(title.Text.Trim(), @"[\r\n]", " ");
            File.WriteAllText(filename, $"-- Created by Steam Tool Manager\r\n-- Name: {safeLabel}\r\naddappid({normalizedId})\r\n", new UTF8Encoding(false));
            RefreshGames(); RefreshScripts();
            footer.Text = $"Created local Lua entry for AppID {normalizedId}.";
        }
        catch (Exception ex) { Error("Could not create Lua file: " + ex.Message); }
    }

    private ManagedGame SelectedGame => gameTable.SelectedRows.Count == 1 ? gameTable.SelectedRows[0].DataBoundItem as ManagedGame : null;

    private void RemoveGame()
    {
        var game = SelectedGame;
        if (game == null) { Warn("Select an entry first."); return; }
        if (!Path.GetFileName(game.FullPath).StartsWith("ost_manager_", StringComparison.OrdinalIgnoreCase)) return;
        if (MessageBox.Show(this, $"Remove the managed Lua file for AppID {game.AppId}?", "Remove entry", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { File.Delete(game.FullPath); RefreshGames(); RefreshScripts(); footer.Text = "Entry removed."; }
        catch (Exception ex) { Error("Could not remove file: " + ex.Message); }
    }

    private void OpenGameFile()
    {
        var game = SelectedGame;
        if (game == null) { Warn("Select an entry first."); return; }
        try { Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = "\"" + game.FullPath + "\"", UseShellExecute = true }); }
        catch (Exception ex) { Error("Could not open Notepad: " + ex.Message); }
    }

    private void RefreshScripts()
    {
        changingSelection = true;
        string selected = selectedScript;
        scripts.Items.Clear();
        if (Directory.Exists(LuaDirectory))
        {
            foreach (var file in Directory.GetFiles(LuaDirectory, "*.lua").OrderBy(Path.GetFileName))
                scripts.Items.Add(new ScriptItem { Path = file });
        }
        changingSelection = false;
        var index = scripts.Items.Cast<ScriptItem>().ToList().FindIndex(x => x.Path == selected);
        if (index >= 0) scripts.SelectedIndex = index;
        else { selectedScript = ""; scriptEditor.Clear(); }
    }

    private void LoadSelectedScript()
    {
        if (changingSelection || scripts.SelectedItem is not ScriptItem item) return;
        try { selectedScript = item.Path; scriptEditor.Text = File.ReadAllText(selectedScript); footer.Text = "Editing " + item.Path; }
        catch (Exception ex) { Error("Could not read script: " + ex.Message); }
    }

    private void NewScript()
    {
        if (!SteamValid) { Warn("Choose a Steam directory first."); return; }
        string name = Prompt("New script", "New Lua filename (letters, numbers, hyphens, underscores):", "my_script");
        if (name == null) return;
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_-]{1,60}$")) { Warn("Invalid filename. Use only letters, numbers, underscores and hyphens."); return; }
        var file = Path.Combine(LuaDirectory, name + ".lua");
        if (File.Exists(file)) { Warn("That file already exists."); return; }
        try { Directory.CreateDirectory(LuaDirectory); File.WriteAllText(file, "-- New Lua configuration\r\n", new UTF8Encoding(false));
            selectedScript = file; RefreshScripts(); footer.Text = "New Lua script created."; }
        catch (Exception ex) { Error("Could not create script: " + ex.Message); }
    }

    private void SaveScript()
    {
        if (string.IsNullOrWhiteSpace(selectedScript)) { Warn("Select a Lua script first."); return; }
        if (MessageBox.Show(this, "Save changes to this Lua script? OpenSteamTool may reload scripts while Steam is running.",
            "Save script", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { File.WriteAllText(selectedScript, scriptEditor.Text, new UTF8Encoding(false)); RefreshGames(); footer.Text = "Lua script saved."; }
        catch (Exception ex) { Error("Could not save script: " + ex.Message); }
    }

    private void RefreshToml()
    {
        if (!SteamValid) { tomlEditor.Text = "# Select your Steam installation folder to edit settings."; return; }
        try { tomlEditor.Text = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : DefaultToml; }
        catch (Exception ex) { tomlEditor.Text = "# Error reading settings: " + ex.Message; }
    }

    private void SaveToml()
    {
        if (!SteamValid) { Warn("Choose a valid Steam directory first."); return; }
        if (MessageBox.Show(this, "Save opensteamtool.toml to Steam directory? Changes may be applied while Steam is running.",
            "Save settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            if (File.Exists(ConfigPath)) File.Copy(ConfigPath, ConfigPath + ".bak", true);
            File.WriteAllText(ConfigPath, tomlEditor.Text, new UTF8Encoding(false));
            footer.Text = "TOML settings saved. A .bak copy was created when replacing an existing file.";
        }
        catch (Exception ex) { Error("Could not save settings: " + ex.Message); }
    }

    private void RefreshLogs()
    {
        logFiles.Items.Clear();
        logViewer.Clear();
        string dir = Path.Combine(steamPath, "opensteamtool");
        if (!Directory.Exists(dir)) { logViewer.Text = "No debug log directory found. This is normal if using a Release build."; return; }
        foreach (var file in Directory.GetFiles(dir, "*.log").OrderBy(Path.GetFileName)) logFiles.Items.Add(new ScriptItem { Path = file });
        if (logFiles.Items.Count == 0) logViewer.Text = "No .log files found in " + dir;
    }

    private void LoadLog()
    {
        if (logFiles.SelectedItem is not ScriptItem item) return;
        try
        {
            using var stream = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 1024 * 1024) stream.Seek(-1024 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            logViewer.Text = (stream.Position > 0 ? "[Showing recent section only]\r\n" : "") + reader.ReadToEnd();
            logViewer.SelectionStart = logViewer.TextLength;
            logViewer.ScrollToCaret();
        }
        catch (Exception ex) { logViewer.Text = "Could not read log: " + ex.Message; }
    }

    private void BackupConfigs()
    {
        if (!SteamValid) { Warn("Choose a valid Steam directory first."); return; }
        string dir = Path.Combine(AppDataDir, "backups", "config", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        try
        {
            Directory.CreateDirectory(dir);
            if (Directory.Exists(LuaDirectory))
            {
                var luaBackup = Path.Combine(dir, "lua");
                Directory.CreateDirectory(luaBackup);
                foreach (var file in Directory.GetFiles(LuaDirectory, "*.lua"))
                    File.Copy(file, Path.Combine(luaBackup, Path.GetFileName(file)), true);
            }
            if (File.Exists(ConfigPath)) File.Copy(ConfigPath, Path.Combine(dir, TomlName), true);
            footer.Text = "Backup saved: " + dir;
            Info("Configuration backed up to:\n" + dir);
        }
        catch (Exception ex) { Error("Could not back up files: " + ex.Message); }
    }

    private static string Prompt(string heading, string label, string initial)
    {
        using var form = new Form { Text = heading, ClientSize = new Size(455, 170), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        form.Controls.Add(Label(label, 17, 15, 420, 29, 9, Ink, true));
        var field = TextField(17, 50, 421, 32);
        field.Text = initial;
        form.Controls.Add(field);
        var ok = Btn("Create", 330, 105, 107, 35, true);
        ok.DialogResult = DialogResult.OK;
        form.Controls.Add(ok);
        var cancel = Btn("Cancel", 218, 105, 103, 35);
        cancel.DialogResult = DialogResult.Cancel;
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? field.Text.Trim() : null;
    }

    private void OpenDirectory(string path)
    {
        if (!Directory.Exists(path)) { Warn("Directory does not exist yet: " + path); return; }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { Error("Could not open folder: " + ex.Message); }
    }
    private void Warn(string message) => MessageBox.Show(this, message, "Steam Tool Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void Error(string message) => MessageBox.Show(this, message, "Steam Tool Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private void Info(string message) => MessageBox.Show(this, message, "Steam Tool Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
