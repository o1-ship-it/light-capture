using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace QuickCapture
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try { Native.SetProcessDpiAwareness(2); }
            catch { Native.SetProcessDPIAware(); }
            bool first;
            using (var mutex = new Mutex(true, "Local\\QuickCapture.Tool.v1", out first))
            {
                if (!first)
                {
                    MessageBox.Show("截图工具已经在运行，请双击任务栏右下角的截图工具图标打开。", "截图工具");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] internal static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    }

    public class AppSettings
    {
        public string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "截图");
        public uint Modifiers = 3;
        public int Key = (int)Keys.A;
        public bool FullScreen;
        public bool CopyToClipboard = true;

        public static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickCapture", "settings.xml"); }
        }

        public static AppSettings Load(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var value = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(stream);
                    if (String.IsNullOrWhiteSpace(value.Folder)) value.Folder = new AppSettings().Folder;
                    if (!Hotkey.IsValid(value.Modifiers, (Keys)value.Key))
                    {
                        value.Modifiers = 3;
                        value.Key = (int)Keys.A;
                    }
                    return value;
                }
            }
            catch { return new AppSettings(); }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
                new XmlSerializer(typeof(AppSettings)).Serialize(stream, this);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    internal static class Hotkey
    {
        internal static bool IsValid(uint modifiers, Keys key)
        {
            bool supportedKey = (key >= Keys.A && key <= Keys.Z) ||
                (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.F1 && key <= Keys.F12);
            return (modifiers & 3) != 0 && (modifiers & ~7u) == 0 && supportedKey;
        }

        internal static string Format(uint modifiers, Keys key)
        {
            string result = "";
            if ((modifiers & 2) != 0) result += "Ctrl + ";
            if ((modifiers & 1) != 0) result += "Alt + ";
            if ((modifiers & 4) != 0) result += "Shift + ";
            return result + ((key >= Keys.D0 && key <= Keys.D9) ? ((int)key - (int)Keys.D0).ToString() : key.ToString());
        }
    }

    internal static class ScreenshotStorage
    {
        internal static string Save(Bitmap bitmap, string folder, DateTime time)
        {
            Directory.CreateDirectory(folder);
            string name = "截图_" + time.ToString("yyyyMMdd_HHmmss_fff");
            for (int index = 0; index < 10000; index++)
            {
                string path = Path.Combine(folder, name + (index == 0 ? "" : "_" + index) + ".png");
                FileStream stream;
                try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException) { if (File.Exists(path)) continue; throw; }
                try
                {
                    using (stream) bitmap.Save(stream, ImageFormat.Png);
                    return path;
                }
                catch { try { File.Delete(path); } catch { } throw; }
            }
            throw new IOException("同一时间的截图文件过多，请稍后重试。");
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppSettings settings;
        private readonly TextBox folderBox = new TextBox();
        private readonly TextBox hotkeyBox = new TextBox();
        private readonly ComboBox modeBox = new ComboBox();
        private readonly CheckBox clipboardBox = new CheckBox();
        private readonly Label status = new Label();
        private readonly Label shortcutLabel = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer captureTimer = new System.Windows.Forms.Timer();
        private bool exiting;
        private bool capturing;
        private bool closeHintShown;
        private int hotkeyId;
        private uint pendingModifiers;
        private Keys pendingKey;
        private string lastFile;
        private readonly Color blue = Color.FromArgb(47, 100, 245);

        internal MainForm()
        {
            settings = AppSettings.Load(AppSettings.SettingsPath);
            Text = "轻截图";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(650, 570);
            MinimumSize = new Size(650, 610);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 251);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildInterface();
            pendingModifiers = settings.Modifiers;
            pendingKey = (Keys)settings.Key;
            RefreshShortcut();
            folderBox.Text = settings.Folder;
            modeBox.SelectedIndex = settings.FullScreen ? 1 : 0;
            clipboardBox.Checked = settings.CopyToClipboard;
            modeBox.SelectedIndexChanged += delegate { settings.FullScreen = modeBox.SelectedIndex == 1; SaveSettings(); };
            clipboardBox.CheckedChanged += delegate { settings.CopyToClipboard = clipboardBox.Checked; SaveSettings(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add("立即截图", null, delegate { BeginCapture(); });
            menu.Items.Add("打开主窗口", null, delegate { ShowMain(); });
            menu.Items.Add("打开保存文件夹", null, delegate { OpenFolder(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApp(); });
            tray.Icon = Icon;
            tray.Text = "轻截图";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowMain(); };
            captureTimer.Interval = 220;
            captureTimer.Tick += delegate { captureTimer.Stop(); CaptureDesktop(); };
            Shown += delegate
            {
                if (!TryRegister(settings.Modifiers, (Keys)settings.Key))
                    SetStatus("快捷键被其他程序占用，请录入新的组合键并点击“应用”。", true);
                else SetStatus("准备就绪 · 按快捷键即可截图", false);
            };
            FormClosing += OnClosing;
            FormClosed += delegate
            {
                if (hotkeyId != 0) Native.UnregisterHotKey(Handle, hotkeyId);
                captureTimer.Dispose();
                tray.Visible = false;
                tray.Dispose();
            };
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(28, 20, 28, 16) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 87));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 111));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 167));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label { Text = "轻截图", Font = new Font(Font.FontFamily, 25F, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0), ForeColor = Color.FromArgb(25, 36, 61) });
            heading.Controls.Add(new Label { Text = "选好文件夹，按下快捷键，截图自动保存。", AutoSize = true, Location = new Point(3, 52), ForeColor = Color.FromArgb(101, 113, 135) });
            root.Controls.Add(heading, 0, 0);

            var folderPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 10, 14, 10), ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 91));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            folderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            folderPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            folderPanel.Controls.Add(new Label { Text = "保存位置", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            folderPanel.SetColumnSpan(folderPanel.GetControlFromPosition(0, 0), 3);
            folderBox.ReadOnly = true;
            folderBox.Dock = DockStyle.Fill;
            folderBox.BackColor = Color.FromArgb(247, 249, 253);
            folderBox.Margin = new Padding(0, 4, 8, 0);
            folderPanel.Controls.Add(folderBox, 0, 1);
            folderPanel.Controls.Add(Button("选择…", delegate { ChooseFolder(); }, false), 1, 1);
            folderPanel.Controls.Add(Button("打开", delegate { OpenFolder(); }, false), 2, 1);
            root.Controls.Add(folderPanel, 0, 1);

            var options = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 12, 14, 10), ColumnCount = 3, RowCount = 3, Margin = new Padding(0, 0, 0, 12) };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(new Label { Text = "截图快捷键", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }, 0, 0);
            hotkeyBox.ReadOnly = true;
            hotkeyBox.BackColor = Color.FromArgb(247, 249, 253);
            hotkeyBox.Dock = DockStyle.Fill;
            hotkeyBox.Margin = new Padding(0, 4, 10, 0);
            hotkeyBox.KeyDown += RecordHotkey;
            options.Controls.Add(hotkeyBox, 1, 0);
            options.Controls.Add(Button("应用", delegate { ApplyHotkey(); }, false), 2, 0);
            options.Controls.Add(new Label { Text = "截图方式", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }, 0, 1);
            modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modeBox.Dock = DockStyle.Fill;
            modeBox.Margin = new Padding(0, 4, 10, 0);
            modeBox.Items.AddRange(new object[] { "框选区域", "整个桌面（所有屏幕）" });
            options.Controls.Add(modeBox, 1, 1);
            options.SetColumnSpan(modeBox, 2);
            clipboardBox.Text = "截图同时复制到剪贴板";
            clipboardBox.AutoSize = true;
            clipboardBox.Margin = new Padding(0, 3, 0, 0);
            options.Controls.Add(clipboardBox, 1, 2);
            options.SetColumnSpan(clipboardBox, 2);
            root.Controls.Add(options, 0, 2);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            var capture = Button("立即截图", delegate { BeginCapture(); }, true);
            capture.Height = 44;
            actions.Controls.Add(capture, 0, 0);
            actions.Controls.Add(Button("隐藏到托盘", delegate { Hide(); }, false), 1, 0);
            actions.Controls.Add(Button("退出", delegate { ExitApp(); }, false), 2, 0);
            root.Controls.Add(actions, 0, 3);

            var footer = new Panel { Dock = DockStyle.Fill };
            status.Location = new Point(0, 0);
            status.Size = new Size(580, 39);
            status.AutoEllipsis = true;
            status.Text = "准备就绪";
            footer.Controls.Add(status);
            shortcutLabel.Location = new Point(0, 42);
            shortcutLabel.AutoSize = true;
            shortcutLabel.Font = new Font(Font.FontFamily, 9F);
            shortcutLabel.ForeColor = Color.FromArgb(101, 113, 135);
            footer.Controls.Add(shortcutLabel);
            root.Controls.Add(footer, 0, 4);
        }

        private Button Button(string text, EventHandler onClick, bool primary)
        {
            var button = new Button { Text = text, Dock = DockStyle.Top, Height = 34, Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 2, 3, 0), BackColor = primary ? blue : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(50, 64, 88) };
            button.FlatAppearance.BorderColor = primary ? blue : Color.FromArgb(219, 225, 236);
            button.Click += onClick;
            return button;
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择截图保存文件夹", SelectedPath = settings.Folder, ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                settings.Folder = dialog.SelectedPath;
                folderBox.Text = settings.Folder;
                if (SaveSettings()) SetStatus("保存位置已更新", false);
            }
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(settings.Folder);
                Process.Start(new ProcessStartInfo(settings.Folder) { UseShellExecute = true });
            }
            catch (Exception error) { ShowError("无法打开文件夹", error); }
        }

        private void RecordHotkey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            uint modifiers = (e.Control ? 2u : 0) | (e.Alt ? 1u : 0) | (e.Shift ? 4u : 0);
            if (!Hotkey.IsValid(modifiers, e.KeyCode))
            {
                SetStatus("请按 Ctrl / Alt + 字母、数字或 F1–F12，可加 Shift。", true);
                return;
            }
            pendingModifiers = modifiers;
            pendingKey = e.KeyCode;
            hotkeyBox.Text = Hotkey.Format(modifiers, e.KeyCode);
            SetStatus("已录入快捷键，点击“应用”生效", false);
        }

        private void ApplyHotkey()
        {
            if (!TryRegister(pendingModifiers, pendingKey))
            {
                SetStatus("这个快捷键已被占用，请换一个组合键。" + (hotkeyId != 0 ? "原快捷键仍然有效。" : ""), true);
                return;
            }
            settings.Modifiers = pendingModifiers;
            settings.Key = (int)pendingKey;
            RefreshShortcut();
            if (SaveSettings()) SetStatus("快捷键已生效：" + hotkeyBox.Text, false);
        }

        private bool TryRegister(uint modifiers, Keys key)
        {
            if (hotkeyId != 0 && settings.Modifiers == modifiers && settings.Key == (int)key) return true;
            int newId = hotkeyId == 1 ? 2 : 1;
            if (!Native.RegisterHotKey(Handle, newId, modifiers | 0x4000, (uint)key)) return false;
            if (hotkeyId != 0) Native.UnregisterHotKey(Handle, hotkeyId);
            hotkeyId = newId;
            return true;
        }

        private void RefreshShortcut()
        {
            hotkeyBox.Text = Hotkey.Format(settings.Modifiers, (Keys)settings.Key);
            shortcutLabel.Text = "框选：拖动鼠标并松开保存 · Esc 取消 · 关闭窗口后仍可用快捷键";
            tray.Text = "轻截图 · " + hotkeyBox.Text;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == hotkeyId)
            {
                // Let the shortcut field record an already registered combination.
                if (!Visible || !hotkeyBox.Focused) BeginCapture();
            }
            base.WndProc(ref message);
        }

        private void BeginCapture()
        {
            if (capturing || !Enabled) return;
            capturing = true;
            Hide();
            captureTimer.Start();
        }

        private void CaptureDesktop()
        {
            Bitmap desktop = null;
            try
            {
                Rectangle bounds = SystemInformation.VirtualScreen;
                desktop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(desktop))
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                if (settings.FullScreen) SaveCapture(desktop);
                else
                {
                    using (var selection = new SelectionForm(desktop, bounds))
                    {
                        if (selection.ShowDialog() == DialogResult.OK)
                        {
                            using (Bitmap crop = desktop.Clone(selection.Selection, PixelFormat.Format32bppArgb))
                                SaveCapture(crop);
                        }
                        else SetStatus("已取消截图", false);
                    }
                }
            }
            catch (Exception error) { ShowMain(); ShowError("截图失败", error); }
            finally { if (desktop != null) desktop.Dispose(); capturing = false; }
        }

        private void SaveCapture(Bitmap bitmap)
        {
            lastFile = ScreenshotStorage.Save(bitmap, settings.Folder, DateTime.Now);
            bool copied = false;
            if (settings.CopyToClipboard)
            {
                try { Clipboard.SetDataObject(bitmap, true, 5, 100); copied = true; }
                catch (ExternalException) { }
            }
            string message = "已保存：" + Path.GetFileName(lastFile);
            if (settings.CopyToClipboard && !copied) message += "（剪贴板被占用，图片已保存）";
            SetStatus(message, false);
            tray.BalloonTipTitle = "截图已保存";
            tray.BalloonTipText = lastFile + (copied ? "\n已复制到剪贴板" : "");
            tray.ShowBalloonTip(2500);
        }

        private bool SaveSettings()
        {
            try { settings.Save(AppSettings.SettingsPath); return true; }
            catch (Exception error) { SetStatus("设置本次有效，但无法记住：" + error.Message, true); return false; }
        }

        private void SetStatus(string message, bool error)
        {
            status.Text = message;
            status.ForeColor = error ? Color.FromArgb(181, 62, 52) : Color.FromArgb(65, 89, 117);
        }

        private void ShowError(string title, Exception error)
        {
            SetStatus(title + "：" + error.Message, true);
            MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ShowMain()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (exiting || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) return;
            e.Cancel = true;
            Hide();
            if (!closeHintShown)
            {
                closeHintShown = true;
                tray.BalloonTipTitle = "轻截图仍在运行";
                tray.BalloonTipText = "使用快捷键截图，双击托盘图标打开设置；右键图标可退出。";
                tray.ShowBalloonTip(3000);
            }
        }

        private void ExitApp() { exiting = true; Close(); }
    }

    internal sealed class SelectionForm : Form
    {
        private readonly Bitmap desktop;
        private Point start;
        private Point current;
        private bool dragging;
        internal Rectangle Selection { get; private set; }

        internal SelectionForm(Bitmap image, Rectangle desktopBounds)
        {
            Text = "轻截图 · 框选区域";
            desktop = image;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = desktopBounds;
            TopMost = true;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Font = new Font("Microsoft YaHei UI", 11F);
            Shown += delegate { Activate(); Focus(); };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Cancel(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(desktop, Point.Empty);
            using (var shade = new SolidBrush(Color.FromArgb(112, 9, 17, 32)))
                e.Graphics.FillRectangle(shade, ClientRectangle);
            Rectangle selection = Rectangle.Intersect(Normalize(start, current), ClientRectangle);
            if (dragging && selection.Width > 0 && selection.Height > 0)
            {
                e.Graphics.DrawImage(desktop, selection, selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(79, 148, 255), 2))
                    e.Graphics.DrawRectangle(pen, selection.X, selection.Y, Math.Max(0, selection.Width - 1), Math.Max(0, selection.Height - 1));
            }
            string hint = dragging ? selection.Width + " × " + selection.Height + "    松开鼠标保存 · Esc 取消" : "拖动鼠标框选截图区域    ·    Esc / 鼠标右键取消";
            Size textSize = TextRenderer.MeasureText(hint, Font);
            Point anchor = dragging ? current : PointToClient(Cursor.Position);
            int x = Math.Max(8, Math.Min(anchor.X + 18, ClientSize.Width - textSize.Width - 32));
            int y = Math.Max(8, Math.Min(anchor.Y + 24, ClientSize.Height - textSize.Height - 24));
            var box = new Rectangle(x, y, textSize.Width + 24, textSize.Height + 16);
            using (var brush = new SolidBrush(Color.FromArgb(238, 23, 32, 51))) e.Graphics.FillRectangle(brush, box);
            TextRenderer.DrawText(e.Graphics, hint, Font, new Point(x + 12, y + 8), Color.White);
        }

        internal static Rectangle Normalize(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { Cancel(); return; }
            if (e.Button != MouseButtons.Left) return;
            start = current = e.Location;
            dragging = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            current = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            current = e.Location;
            Selection = Rectangle.Intersect(Normalize(start, current), ClientRectangle);
            dragging = false;
            Capture = false;
            if (Selection.Width < 2 || Selection.Height < 2) { Invalidate(); return; }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Cancel() { DialogResult = DialogResult.Cancel; Close(); }
    }
}
