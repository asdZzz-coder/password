using password.Services;

namespace password.Models
{
    /// <summary>左側資料夾清單的一列（所有項目 / 各資料夾 / 無資料夾），每次資料變動時重新產生。</summary>
    public record FolderItem(FolderFilter Filter, string Display, int Count)
    {
        public bool IsUserFolder => Filter.Kind == FolderFilterKind.Folder;

        public string Icon => Filter.Kind switch
        {
            FolderFilterKind.All => "\uE8A9",      // 全部
            FolderFilterKind.NoFolder => "\uE8A5", // 未分類（文件）
            _ => "\uE8B7",                         // 資料夾
        };

        // 螢幕閱讀器與 UI 自動化讀到的名稱
        public override string ToString() => Display;
    }

    /// <summary>編輯表單「資料夾」下拉選單的選項；Name 為空字串表示無資料夾。</summary>
    public record FolderOption(string Name, string Display)
    {
        public override string ToString() => Display;
    }
}
