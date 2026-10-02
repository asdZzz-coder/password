using System.IO;

namespace password.Services
{
    /// <summary>
    /// 安裝版第一次開啟時，在桌面放一個「帳號密碼紀錄」捷徑。
    /// ClickOnce 雖然設定了建立桌面捷徑，但桌面被 OneDrive 接管時常常建不出來，所以由程式自己補上。
    /// 只做一次（在資料夾記一個標記檔）：之後使用者自己刪掉捷徑，就不會再被加回來。
    /// </summary>
    public static class DesktopShortcutService
    {
        private const string ManifestName = "PasswordKeeper.application";
        private static readonly string MarkerFile = Path.Combine(DataStore.DataDirectory, "desktop-shortcut.done");

        public static void EnsureOnce(bool isInstalled)
        {
            if (!isInstalled || File.Exists(MarkerFile)) return;
            try
            {
                // 桌面可能在 OneDrive 底下，一定要用系統回報的實際路徑
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!HasShortcut(desktop))
                {
                    var source = FindStartMenuShortcut();
                    if (source == null) return; // 找不到開始功能表捷徑就下次再試，不寫標記
                    File.Copy(source, Path.Combine(desktop, Path.GetFileName(source)), overwrite: false);
                }
                Directory.CreateDirectory(DataStore.DataDirectory);
                File.WriteAllText(MarkerFile, DateTime.Now.ToString("s"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 下次啟動再試 */ }
        }

        private static bool HasShortcut(string folder) =>
            Directory.Exists(folder) &&
            Directory.EnumerateFiles(folder, "*.appref-ms").Any(IsOurShortcut);

        /// <summary>ClickOnce 建立的開始功能表捷徑（.appref-ms，內容記錄安裝來源與程式名稱）。</summary>
        private static string? FindStartMenuShortcut()
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            return Directory.EnumerateFiles(programs, "*.appref-ms", SearchOption.AllDirectories).FirstOrDefault(IsOurShortcut);
        }

        private static bool IsOurShortcut(string path)
        {
            try { return File.ReadAllText(path).Contains(ManifestName, StringComparison.OrdinalIgnoreCase); }
            catch (IOException) { return false; }
        }
    }
}
