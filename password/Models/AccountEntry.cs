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

        private static readonly string[] AvatarColors =
        {
            "#4F46E5", "#0EA5E9", "#10B981", "#F59E0B", "#EF4444",
            "#EC4899", "#8B5CF6", "#14B8A6", "#F97316", "#6366F1",
        };

        /// <summary>依 App 名稱固定挑一個頭像底色（同名永遠同色；僅供畫面使用，不存檔）。</summary>
        [JsonIgnore]
        public string AvatarColor
        {
            get
            {
                uint h = 2166136261; // FNV-1a，跨次啟動結果一致（string.GetHashCode 每次啟動都不同）
                foreach (var c in AppName.Trim().ToUpperInvariant()) { h ^= c; h *= 16777619; }
                return AvatarColors[h % (uint)AvatarColors.Length];
            }
        }
    }
}
