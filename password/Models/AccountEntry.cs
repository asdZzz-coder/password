using System.Text.Json.Serialization;

namespace password.Models
{
    public class AccountEntry
    {
        public string AppName { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Note { get; set; } = "";

        /// <summary>這個帳號連結了哪些 App（手動輸入的條列清單）。舊版資料檔沒有此欄位時會是空清單。</summary>
        public List<string> LinkedApps { get; set; } = new();

        /// <summary>清單右側徽章顯示的連結數量（僅供畫面使用，不存檔）。</summary>
        [JsonIgnore]
        public int LinkCount => LinkedApps.Count;

        /// <summary>清單頭像上顯示的第一個字（僅供畫面使用，不存檔）。</summary>
        [JsonIgnore]
        public string Initial => string.IsNullOrWhiteSpace(AppName)
            ? "?"
            : AppName.Trim().EnumerateRunes().First().ToString().ToUpperInvariant();
    }
}
