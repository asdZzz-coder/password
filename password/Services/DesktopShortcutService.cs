using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace password.Services
{
    public enum ShortcutResult { Created, SourceNotFound }

    /// <summary>
    /// 在桌面建立「帳號密碼紀錄」捷徑。
    /// - 安裝版（ClickOnce）：複製開始功能表的 .appref-ms 到桌面，從桌面開啟才會走 ClickOnce 啟動。
    /// - 開發版 / 直接執行 exe：建立指向目前 exe 的 .lnk。
    /// 安裝版第一次開啟時也會自動補一次（ClickOnce 在桌面被 OneDrive 接管時常常建不出捷徑）；
    /// 只做一次（在資料夾記一個標記檔），之後使用者自己刪掉捷徑就不會再被加回來，要的話可按工具列的按鈕重建。
    /// </summary>
    public static class DesktopShortcutService
    {
        private const string ManifestName = "PasswordKeeper.application";
        private const string MarkerName = "desktop-shortcut.done";
        internal const string DevShortcutName = "帳號密碼紀錄 (開發版).lnk";

        // 環境變數 PASSWORDKEEPER_DESKTOP_DIR 可指定其他資料夾當作桌面（測試用）；
        // 平常不設定，桌面可能在 OneDrive 底下，一定要用系統回報的實際路徑
        private static string DesktopDirectory =>
            Environment.GetEnvironmentVariable("PASSWORDKEEPER_DESKTOP_DIR")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        private static string ProgramsDirectory => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        /// <summary>按下「桌面捷徑」按鈕：建立或重建桌面捷徑。</summary>
        public static ShortcutResult Create(bool isInstalled) =>
            Create(isInstalled, DesktopDirectory, ProgramsDirectory, Environment.ProcessPath ?? "");

        internal static ShortcutResult Create(bool isInstalled, string desktop, string programs, string exePath)
        {
            Directory.CreateDirectory(desktop);
            if (!isInstalled)
            {
                CreateLink(Path.Combine(desktop, DevShortcutName), exePath);
                return ShortcutResult.Created;
            }

            var source = FindShortcut(programs, SearchOption.AllDirectories);
            if (source == null) return ShortcutResult.SourceNotFound;

            // 桌面上已經有（可能是舊的或壞掉的）就用開始功能表的內容覆蓋，不另外多放一個
            var target = FindShortcut(desktop, SearchOption.TopDirectoryOnly) ?? Path.Combine(desktop, Path.GetFileName(source));
            File.Copy(source, target, overwrite: true);
            return ShortcutResult.Created;
        }

        /// <summary>安裝版第一次開啟時補上桌面捷徑（只做一次）。</summary>
        public static void EnsureOnce(bool isInstalled) =>
            EnsureOnce(isInstalled, DataStore.DataDirectory, DesktopDirectory, ProgramsDirectory);

        internal static void EnsureOnce(bool isInstalled, string markerDir, string desktop, string programs)
        {
            var marker = Path.Combine(markerDir, MarkerName);
            if (!isInstalled || File.Exists(marker)) return;
            try
            {
                if (FindShortcut(desktop, SearchOption.TopDirectoryOnly) == null &&
                    Create(true, desktop, programs, "") == ShortcutResult.SourceNotFound)
                    return; // 找不到開始功能表捷徑就下次再試，不寫標記
                Directory.CreateDirectory(markerDir);
                File.WriteAllText(marker, DateTime.Now.ToString("s"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 下次啟動再試 */ }
        }

        /// <summary>找本程式的 ClickOnce 捷徑（.appref-ms，內容記錄安裝來源與 PasswordKeeper.application）。</summary>
        private static string? FindShortcut(string folder, SearchOption option) =>
            Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.appref-ms", option).FirstOrDefault(IsOurShortcut)
                : null;

        private static bool IsOurShortcut(string path)
        {
            try { return File.ReadAllText(path).Contains(ManifestName, StringComparison.OrdinalIgnoreCase); }
            catch (IOException) { return false; }
        }

        // ----- .lnk（Windows Shell 的 IShellLink） -----

        private static void CreateLink(string linkPath, string targetPath)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(targetPath);
                link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
                link.SetIconLocation(targetPath, 0);
                link.SetDescription("帳號密碼紀錄");
                ((IPersistFile)link).Save(linkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        /// <summary>讀出 .lnk 指向的檔案（測試用）。</summary>
        internal static string ReadLinkTarget(string linkPath)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(linkPath, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
