using System.IO;
using System.Text;
using password.Services;

namespace password.Tests
{
    /// <summary>
    /// 桌面捷徑的自動測試。全部在暫存資料夾裡模擬「桌面」與「開始功能表」，不會動到真正的桌面。
    /// </summary>
    public sealed class DesktopShortcutServiceTests : IDisposable
    {
        private const string AppRefName = "帳號密碼紀錄.appref-ms";
        private const string AppRefContent =
            "file:///C:/Users/test/AppData/Local/PasswordKeeper-Setup/PasswordKeeper.application" +
            "#PasswordKeeper.application, Culture=neutral, PublicKeyToken=0000000000000000, processorArchitecture=amd64";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "PasswordKeeper-Tests-" + Guid.NewGuid().ToString("N"));
        private string Desktop => Path.Combine(_root, "Desktop");
        private string Programs => Path.Combine(_root, "Programs");
        private string DataDir => Path.Combine(_root, "Data");

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        /// <summary>模擬 ClickOnce 安裝後在開始功能表放的 .appref-ms（UTF-16 含 BOM，與實際相同）。</summary>
        private string CreateStartMenuShortcut()
        {
            var folder = Path.Combine(Programs, "asdZzz-coder");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, AppRefName);
            File.WriteAllText(path, AppRefContent, Encoding.Unicode);
            return path;
        }

        private string[] DesktopFiles() =>
            Directory.Exists(Desktop) ? Directory.GetFiles(Desktop).Select(Path.GetFileName).ToArray()! : [];

        // ---------- 按鈕：安裝版 ----------

        [Fact]
        public void Installed_CopiesStartMenuShortcutToDesktop()
        {
            var source = CreateStartMenuShortcut();

            var result = DesktopShortcutService.Create(true, Desktop, Programs, "");

            Assert.Equal(ShortcutResult.Created, result);
            Assert.Equal([AppRefName], DesktopFiles());
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(Desktop, AppRefName)));
        }

        [Fact]
        public void Installed_WithoutStartMenuShortcut_ReportsSourceNotFound()
        {
            var result = DesktopShortcutService.Create(true, Desktop, Programs, "");

            Assert.Equal(ShortcutResult.SourceNotFound, result);
            Assert.Empty(DesktopFiles());
        }

        [Fact]
        public void Installed_ClickTwice_DoesNotDuplicate()
        {
            CreateStartMenuShortcut();

            DesktopShortcutService.Create(true, Desktop, Programs, "");
            DesktopShortcutService.Create(true, Desktop, Programs, "");

            Assert.Single(DesktopFiles());
        }

        [Fact]
        public void Installed_RenamedOrBrokenDesktopShortcut_IsRepairedInPlace()
        {
            var source = CreateStartMenuShortcut();
            Directory.CreateDirectory(Desktop);
            // 使用者改過名字、內容是舊版安裝路徑
            var renamed = Path.Combine(Desktop, "我的密碼.appref-ms");
            File.WriteAllText(renamed, "file:///D:/old/PasswordKeeper.application#PasswordKeeper.application", Encoding.Unicode);

            DesktopShortcutService.Create(true, Desktop, Programs, "");

            Assert.Equal(["我的密碼.appref-ms"], DesktopFiles());
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(renamed));
        }

        [Fact]
        public void Installed_IgnoresOtherAppsShortcuts()
        {
            CreateStartMenuShortcut();
            Directory.CreateDirectory(Desktop);
            var other = Path.Combine(Desktop, "其他程式.appref-ms");
            File.WriteAllText(other, "file:///C:/x/OtherApp.application#OtherApp.application", Encoding.Unicode);

            DesktopShortcutService.Create(true, Desktop, Programs, "");

            Assert.Equal(["其他程式.appref-ms", AppRefName], DesktopFiles().Order().ToArray());
            Assert.Contains("OtherApp.application", File.ReadAllText(other));
        }

        // ---------- 按鈕：開發版（直接執行 exe） ----------

        [Fact]
        public void DevBuild_CreatesLnkPointingToExe()
        {
            var exe = Path.Combine(_root, "bin", "PasswordKeeper.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllBytes(exe, [0x4D, 0x5A]); // 只需要檔案存在

            var result = DesktopShortcutService.Create(false, Desktop, Programs, exe);

            Assert.Equal(ShortcutResult.Created, result);
            var lnk = Path.Combine(Desktop, DesktopShortcutService.DevShortcutName);
            Assert.True(File.Exists(lnk));
            Assert.Equal(exe, DesktopShortcutService.ReadLinkTarget(lnk), ignoreCase: true);
        }

        // ---------- 安裝版第一次開啟時自動建立（只做一次） ----------

        [Fact]
        public void EnsureOnce_FirstRun_CreatesShortcutAndMarker()
        {
            CreateStartMenuShortcut();

            DesktopShortcutService.EnsureOnce(true, DataDir, Desktop, Programs);

            Assert.Equal([AppRefName], DesktopFiles());
            Assert.True(File.Exists(Path.Combine(DataDir, "desktop-shortcut.done")));
        }

        [Fact]
        public void EnsureOnce_UserDeletedShortcut_IsNotRecreated()
        {
            CreateStartMenuShortcut();
            DesktopShortcutService.EnsureOnce(true, DataDir, Desktop, Programs);
            File.Delete(Path.Combine(Desktop, AppRefName)); // 使用者自己刪掉

            DesktopShortcutService.EnsureOnce(true, DataDir, Desktop, Programs);

            Assert.Empty(DesktopFiles());
        }

        [Fact]
        public void EnsureOnce_NoStartMenuShortcutYet_RetriesNextTime()
        {
            DesktopShortcutService.EnsureOnce(true, DataDir, Desktop, Programs);
            Assert.False(File.Exists(Path.Combine(DataDir, "desktop-shortcut.done")));

            CreateStartMenuShortcut();
            DesktopShortcutService.EnsureOnce(true, DataDir, Desktop, Programs);

            Assert.Equal([AppRefName], DesktopFiles());
        }

        [Fact]
        public void EnsureOnce_DevBuild_DoesNothing()
        {
            CreateStartMenuShortcut();

            DesktopShortcutService.EnsureOnce(false, DataDir, Desktop, Programs);

            Assert.Empty(DesktopFiles());
            Assert.False(Directory.Exists(DataDir));
        }
    }
}
