using password.Models;

namespace password.Services
{
    public enum FolderFilterKind { All, NoFolder, Folder }

    /// <summary>左側資料夾清單目前選的是哪一個：所有項目 / 某個資料夾 / 無資料夾。</summary>
    public readonly record struct FolderFilter(FolderFilterKind Kind, string Name = "")
    {
        public static readonly FolderFilter All = new(FolderFilterKind.All);
        public static readonly FolderFilter NoFolder = new(FolderFilterKind.NoFolder);
        public static FolderFilter Of(string name) => new(FolderFilterKind.Folder, FolderService.Normalize(name));

        public bool Matches(AccountEntry entry) => Kind switch
        {
            FolderFilterKind.All => true,
            FolderFilterKind.NoFolder => FolderService.Normalize(entry.Folder).Length == 0,
            _ => FolderService.SameName(entry.Folder, Name),
        };

        /// <summary>兩個篩選是否指向同一個資料夾（名稱不分大小寫）。</summary>
        public bool SameAs(FolderFilter other) => Kind == other.Kind && FolderService.SameName(Name, other.Name);
    }

    public enum FolderNameError { None, Empty, TooLong, Duplicate }

    /// <summary>
    /// 資料夾分類（類似 Bitwarden）：每筆帳號屬於一個資料夾或「無資料夾」。
    /// 資料夾以名稱識別、不分大小寫；清單另外存一份（DataStore.SaveFolders），所以空資料夾也會保留。
    /// 這裡只放不碰畫面與檔案的邏輯，方便單元測試。
    /// </summary>
    public static class FolderService
    {
        public const int MaxNameLength = 40;

        public static string Normalize(string? name) => (name ?? "").Trim();

        public static bool SameName(string? a, string? b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>檢查新名稱；重新命名時傳入原名稱，只改大小寫（work → Work）不算重複。</summary>
        public static FolderNameError Validate(string? name, IEnumerable<string> existing, string? renaming = null)
        {
            var n = Normalize(name);
            if (n.Length == 0) return FolderNameError.Empty;
            if (n.Length > MaxNameLength) return FolderNameError.TooLong;
            if (existing.Any(f => SameName(f, n) && !(renaming != null && SameName(f, renaming))))
                return FolderNameError.Duplicate;
            return FolderNameError.None;
        }

        /// <summary>
        /// 整理資料夾清單：去掉空白與重複（不分大小寫，保留先出現的寫法），
        /// 並補上帳號用到、但清單裡沒有的資料夾（例如從 Excel 匯入的），最後依名稱排序。
        /// </summary>
        public static List<string> Merge(IEnumerable<string> folders, IEnumerable<AccountEntry> entries)
        {
            var result = new List<string>();
            foreach (var name in folders.Concat(entries.Select(e => e.Folder)).Select(Normalize))
                if (name.Length > 0 && !result.Any(f => SameName(f, name)))
                    result.Add(name);
            result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        /// <summary>清單裡與 name 同名（不分大小寫）的資料夾寫法；沒有則回傳空字串（＝無資料夾）。</summary>
        public static string Canonical(IEnumerable<string> folders, string? name) =>
            folders.FirstOrDefault(f => SameName(f, name)) ?? "";

        /// <summary>重新命名資料夾，裡面的帳號一起改過去；回傳新清單與搬動的筆數。</summary>
        public static (List<string> Folders, int Moved) Rename(
            IEnumerable<string> folders, IEnumerable<AccountEntry> entries, string oldName, string newName)
        {
            newName = Normalize(newName);
            int moved = 0;
            foreach (var e in entries)
                if (SameName(e.Folder, oldName)) { e.Folder = newName; moved++; }
            return (Merge(folders.Where(f => !SameName(f, oldName)).Append(newName), []), moved);
        }

        /// <summary>刪除資料夾：帳號不會被刪掉，而是移到「無資料夾」；回傳新清單與搬動的筆數。</summary>
        public static (List<string> Folders, int Moved) Delete(
            IEnumerable<string> folders, IEnumerable<AccountEntry> entries, string name)
        {
            int moved = 0;
            foreach (var e in entries)
                if (SameName(e.Folder, name)) { e.Folder = ""; moved++; }
            return (Merge(folders.Where(f => !SameName(f, name)), []), moved);
        }
    }
}
