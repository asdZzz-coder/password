using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using password.Models;

namespace password.Services
{
    /// <summary>
    /// 把帳號資料用 Windows DPAPI（綁定目前使用者）加密後存到 %AppData%。
    /// 換電腦時請用「匯出 Excel → 匯入」搬資料。
    /// </summary>
    public static class DataStore
    {
        // 環境變數 PASSWORDKEEPER_DATA_DIR 可指定其他資料夾（測試用）；平常不設定，存在 %AppData%\PasswordKeeper
        public static readonly string DataDirectory =
            Environment.GetEnvironmentVariable("PASSWORDKEEPER_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasswordKeeper");

        private static readonly string FilePath = Path.Combine(DataDirectory, "data.bin");

        // 資料夾清單另外存一個檔：data.bin 維持原本的格式，退回舊版時舊版仍讀得懂（只是看不到資料夾）
        private static readonly string FoldersPath = Path.Combine(DataDirectory, "folders.bin");

        public static List<AccountEntry> Load()
        {
            if (!File.Exists(FilePath)) return new();
            try
            {
                return JsonSerializer.Deserialize<List<AccountEntry>>(ReadEncrypted(FilePath)) ?? new();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                // 資料無法解密（例如檔案從別台電腦複製過來），備份後以空資料開始
                File.Move(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                return new();
            }
        }

        public static void Save(IEnumerable<AccountEntry> entries) =>
            WriteEncrypted(FilePath, JsonSerializer.Serialize(entries));

        /// <summary>讀取資料夾清單；讀不到就回傳空清單（帳號上記錄的資料夾仍會被補回來）。</summary>
        public static List<string> LoadFolders()
        {
            if (!File.Exists(FoldersPath)) return new();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(ReadEncrypted(FoldersPath)) ?? new();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
            {
                return new();
            }
        }

        public static void SaveFolders(IEnumerable<string> folders) =>
            WriteEncrypted(FoldersPath, JsonSerializer.Serialize(folders));

        private static string ReadEncrypted(string path)
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        private static void WriteEncrypted(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
