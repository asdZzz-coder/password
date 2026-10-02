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
        private static readonly string FilePath = Path.Combine(
            Environment.GetEnvironmentVariable("PASSWORDKEEPER_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasswordKeeper"),
            "data.bin");

        public static List<AccountEntry> Load()
        {
            if (!File.Exists(FilePath)) return new();
            try
            {
                var cipher = File.ReadAllBytes(FilePath);
                var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<List<AccountEntry>>(Encoding.UTF8.GetString(plain)) ?? new();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                // 資料無法解密（例如檔案從別台電腦複製過來），備份後以空資料開始
                File.Move(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                return new();
            }
        }

        public static void Save(IEnumerable<AccountEntry> entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(entries);
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, cipher);
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
