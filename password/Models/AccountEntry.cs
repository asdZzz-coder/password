using System.Text.Json.Serialization;

namespace password.Models
{
    public class AccountEntry
    {
        public string AppName { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Note { get; set; } = "";

        /// <summary>清單頭像上顯示的第一個字（僅供畫面使用，不存檔）。</summary>
        [JsonIgnore]
        public string Initial => string.IsNullOrWhiteSpace(AppName)
            ? "?"
            : AppName.Trim().EnumerateRunes().First().ToString().ToUpperInvariant();
    }
}
