using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace QuickCapture
{
    internal static class Tests
    {
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        [STAThread]
        private static void Main()
        {
            string folder = Path.Combine(Path.GetTempPath(), "QuickCaptureTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Check(Hotkey.IsValid(3, Keys.A), "Default hotkey invalid");
                Check(!Hotkey.IsValid(0, Keys.A) && !Hotkey.IsValid(4, Keys.A), "Unsafe hotkey accepted");
                Check(Hotkey.Format(7, Keys.F8) == "Ctrl + Alt + Shift + F8", "Hotkey formatting failed");
                Check(SelectionForm.Normalize(new Point(200, 100), new Point(20, 10)) == new Rectangle(20, 10, 180, 90), "Reverse selection failed");
                using (var bitmap = new Bitmap(32, 24))
                {
                    using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
                    DateTime time = new DateTime(2026, 10, 27, 12, 30, 10);
                    string first = ScreenshotStorage.Save(bitmap, folder, time);
                    string second = ScreenshotStorage.Save(bitmap, folder, time);
                    Check(first != second && File.Exists(first) && File.Exists(second), "Collision overwrote a capture");
                    using (var image = new Bitmap(first))
                    {
                        Check(image.Size == bitmap.Size, "PNG dimensions changed");
                        Check(image.GetPixel(0, 0).ToArgb() == Color.CornflowerBlue.ToArgb(), "PNG pixel changed");
                    }
                }
                string config = Path.Combine(folder, "settings.xml");
                var settings = new AppSettings { Folder = Path.Combine(folder, "中文 文件夹"), Modifiers = 6, Key = (int)Keys.F8, FullScreen = true, CopyToClipboard = false };
                settings.Save(config);
                settings.Save(config);
                var loaded = AppSettings.Load(config);
                Check(loaded.Folder == settings.Folder && loaded.Modifiers == 6 && loaded.Key == (int)Keys.F8 && loaded.FullScreen && !loaded.CopyToClipboard, "Settings round-trip failed");
                File.WriteAllText(config, "broken xml");
                Check(AppSettings.Load(config).Modifiers == 3, "Corrupt settings did not recover");
                Console.WriteLine("PASS: hotkeys, reverse selection, PNG content, filename collisions, settings persistence and recovery.");
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
