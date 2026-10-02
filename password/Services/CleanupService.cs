using System.IO;

namespace password.Services
{
    /// <summary>
    /// 啟動時清掉更新後不再需要的舊檔案，只刪除下列明確指定的檔案，絕不碰使用者的帳號資料（data.bin）：
    ///   1. %TEMP% 內下載的舊版安裝檔 PasswordKeeper-Setup-*.exe
    ///   2. 安裝資料夾 packages 內的 *.nupkg（Velopack 的舊套件；更新改用 Setup.exe，已不會用到）
    ///   3. 資料夾內寫入中斷留下的 data.bin.tmp
    /// 任何一項刪除失敗（例如檔案還被安裝程式占用）都直接略過，下次啟動再試。
    /// </summary>
    public static class CleanupService
    {
        public const string SetupFilePattern = "PasswordKeeper-Setup-*.exe";

        public static void RunInBackground() => Task.Run(Run);

        public static void Run()
        {
            DeleteMatching(Path.GetTempPath(), SetupFilePattern);

            var installRoot = FindInstallRoot();
            if (installRoot != null)
                DeleteMatching(Path.Combine(installRoot, "packages"), "*.nupkg");

            DeleteMatching(DataStore.DataDirectory, "data.bin.tmp");
        }

        /// <summary>安裝版的執行檔位在 &lt;安裝根目錄&gt;\current\，且根目錄有 Update.exe；開發環境回傳 null。</summary>
        private static string? FindInstallRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\', '/'));
            var root = dir.Parent;
            if (dir.Name.Equals("current", StringComparison.OrdinalIgnoreCase) &&
                root != null && File.Exists(Path.Combine(root.FullName, "Update.exe")))
                return root.FullName;
            return null;
        }

        /// <summary>刪除 folder 內符合 pattern 的檔案（不遞迴、不刪資料夾）。</summary>
        public static void DeleteMatching(string folder, string pattern)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 被占用，下次再清 */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
