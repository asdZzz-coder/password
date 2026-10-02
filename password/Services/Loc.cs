using System.Globalization;
using System.IO;
using System.Windows;

namespace password.Services
{
    /// <summary>
    /// 介面語言（繁體中文 / English）。XAML 用 {DynamicResource S_key} 綁定，
    /// 程式碼用 Loc.T("key")；切換語言時會即時更新資源並通知視窗重新整理。
    /// 語言選擇存在資料資料夾的 language.txt（不含任何帳號資料）。
    /// </summary>
    public static class Loc
    {
        private static readonly string SettingFile = Path.Combine(DataStore.DataDirectory, "language.txt");

        public static string Language { get; private set; } = "zh";

        public static bool IsChinese => Language == "zh";

        /// <summary>語言切換後觸發，讓視窗更新用程式碼設定的文字（標題、狀態列…）。</summary>
        public static event Action? LanguageChanged;

        /// <summary>讀取使用者上次的選擇；沒有的話依系統語言決定。不依賴 WPF，可在 Application 建立前呼叫。</summary>
        public static void Load()
        {
            string? saved = null;
            try { if (File.Exists(SettingFile)) saved = File.ReadAllText(SettingFile).Trim(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Language = saved is "zh" or "en"
                ? saved
                : CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        }

        /// <summary>把目前語言的字串寫進 Application.Resources（App.xaml 載入後呼叫）。</summary>
        public static void Apply()
        {
            var res = Application.Current.Resources;
            foreach (var (key, zh, en) in Table)
                res["S_" + key] = IsChinese ? zh : en;
        }

        public static void Toggle() => SetLanguage(IsChinese ? "en" : "zh");

        public static void SetLanguage(string lang)
        {
            if (lang == Language) return;
            Language = lang;
            try
            {
                Directory.CreateDirectory(DataStore.DataDirectory);
                File.WriteAllText(SettingFile, lang);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Apply();
            LanguageChanged?.Invoke();
        }

        /// <summary>取得目前語言的文字；有參數時以 string.Format 帶入。</summary>
        public static string T(string key, params object[] args)
        {
            var text = Lookup.TryGetValue(key, out var e) ? (IsChinese ? e.zh : e.en) : key;
            return args.Length == 0 ? text : string.Format(text, args);
        }

        private static readonly (string key, string zh, string en)[] Table =
        {
            // ----- 主視窗 -----
            ("app_title", "帳號密碼紀錄", "Password Keeper"),
            ("title_installed", "帳號密碼紀錄 v{0}", "Password Keeper v{0}"),
            ("title_dev", "帳號密碼紀錄（開發版）", "Password Keeper (dev build)"),
            ("btn_lang", "English", "繁體中文"),
            ("btn_lang_tip", "切換成英文", "Switch to Traditional Chinese"),
            ("btn_export", "匯出 Excel", "Export Excel"),
            ("btn_import", "匯入 Excel", "Import Excel"),
            ("btn_check_update", "檢查更新", "Check for Updates"),
            ("search_placeholder", "搜尋 App、帳號、備註…", "Search app, username, notes…"),
            ("empty_list", "還沒有資料\n從右邊新增第一筆吧", "No entries yet\nAdd your first one on the right"),
            ("links_suffix", " 個連結", " links"),
            ("links_tooltip", "已連結的 App 數量", "Number of linked apps"),
            ("form_title", "帳號資料", "Account details"),
            ("lbl_app", "App 名稱", "App name"),
            ("lbl_user", "帳號", "Username"),
            ("lbl_pwd", "密碼", "Password"),
            ("show_pwd", "顯示密碼", "Show password"),
            ("lbl_note", "備註", "Notes"),
            ("lbl_links", "已連結的 App", "Linked apps"),
            ("btn_add_link", "加入", "Add"),
            ("links_empty", "尚未記錄，輸入 App 名稱後按 Enter", "None yet — type an app name and press Enter"),
            ("remove_tip", "移除", "Remove"),
            ("btn_new", "新增", "Add"),
            ("btn_save", "儲存修改", "Save changes"),
            ("btn_copy", "複製密碼", "Copy password"),
            ("btn_delete", "刪除", "Delete"),
            ("btn_delete_all", "清除全部資料", "Delete all data"),

            // ----- 狀態列 / 訊息 -----
            ("count_text", "共 {0} 筆", "{0} entries"),
            ("dev_version", "開發版", "dev build"),
            ("version_text", "版本 {0}", "Version {0}"),
            ("hint_title", "提示", "Notice"),
            ("need_app_name", "請輸入 App 名稱。", "Please enter an app name."),
            ("select_first", "請先在左邊選一筆要修改的資料，或按「新增」。", "Select an entry on the left to edit, or click \"Add\"."),
            ("delete_title", "刪除", "Delete"),
            ("delete_confirm", "確定刪除「{0}」？", "Delete \"{0}\"?"),
            ("copy_title", "複製密碼", "Copy Password"),
            ("copy_done", "密碼已複製到剪貼簿", "Password copied to clipboard"),
            ("copy_busy", "剪貼簿目前被其他程式占用，請稍後再試。", "The clipboard is in use by another program. Please try again."),
            ("save_title", "儲存", "Save"),
            ("save_failed", "儲存失敗，資料尚未寫入硬碟：{0}", "Save failed — data was not written to disk: {0}"),
            ("already_running", "帳號密碼紀錄已經在執行中。", "Password Keeper is already running."),

            // ----- 清除全部資料 -----
            ("delall_title", "清除全部資料", "Delete All Data"),
            ("delall_none", "目前沒有任何資料可以清除。", "There is no data to delete."),
            ("delall_1_title", "清除全部資料（1/2）", "Delete All Data (1/2)"),
            ("delall_1", "即將刪除全部 {0} 筆帳號資料。\n\n建議先用「匯出 Excel」備份。\n\n確定要繼續嗎？",
                         "All {0} entries are about to be deleted.\n\nWe recommend backing up with \"Export Excel\" first.\n\nContinue?"),
            ("delall_2_title", "清除全部資料（2/2）", "Delete All Data (2/2)"),
            ("delall_2", "最後確認：真的要永久刪除全部 {0} 筆資料嗎？\n\n刪除後無法復原。",
                         "Final confirmation: permanently delete all {0} entries?\n\nThis cannot be undone."),
            ("delall_done", "已清除全部資料（共 {0} 筆）", "All data deleted ({0} entries)"),

            // ----- 更新 -----
            ("update_title", "檢查更新", "Check for Updates"),
            ("update_dev", "目前是開發版（非安裝版），無法線上更新。", "This is a development build (not installed), so online updates are unavailable."),
            ("update_latest", "目前已是最新版本（{0}）。", "You are on the latest version ({0})."),
            ("update_found_title", "有新版本", "Update Available"),
            ("update_found", "發現新版本 {0}（目前 {1}）。\n\n是否現在更新？更新完成後程式會自動重新啟動。",
                             "Version {0} is available (current: {1}).\n\nUpdate now? The app will restart automatically when finished."),
            ("downloading", "下載更新中…", "Downloading update…"),
            ("downloading_pct", "下載更新中… {0}%", "Downloading update… {0}%"),
            ("update_failed", "檢查更新失敗：{0}", "Update check failed: {0}"),

            // ----- Excel -----
            ("excel_filter", "Excel 檔案 (*.xlsx)|*.xlsx", "Excel files (*.xlsx)|*.xlsx"),
            ("export_filename", "帳號密碼_{0}.xlsx", "Passwords_{0}.xlsx"),
            ("export_title", "匯出 Excel", "Export Excel"),
            ("export_done", "匯出完成。\n\n注意：Excel 內的密碼是明文，請妥善保管，用完建議刪除。",
                            "Export complete.\n\nNote: passwords in the Excel file are in plain text. Keep the file safe and delete it when you are done."),
            ("export_failed", "匯出失敗：{0}", "Export failed: {0}"),
            ("import_title", "匯入 Excel", "Import Excel"),
            ("import_failed", "匯入失敗：{0}", "Import failed: {0}"),
            ("import_empty", "這個檔案裡沒有可匯入的資料（第一列需為標題列：App、帳號、密碼、備註）。",
                             "This file has no data to import (the first row must be a header row: App, Username, Password, Notes)."),
            ("import_confirm", "讀到 {0} 筆資料。\n\n是 = 合併到現有資料（App 與帳號相同者略過）\n否 = 清除現有資料，完全以 Excel 為準\n取消 = 不匯入",
                               "Found {0} entries.\n\nYes = merge into existing data (entries with the same app and username are skipped)\nNo = clear existing data and use the Excel file only\nCancel = do not import"),
            ("import_done", "匯入完成，新增 {0} 筆。", "Import complete — {0} entries added."),
            ("sheet_name", "帳號密碼", "Passwords"),
            ("hdr_app", "App", "App"),
            ("hdr_user", "帳號", "Username"),
            ("hdr_pwd", "密碼", "Password"),
            ("hdr_note", "備註", "Notes"),
            ("hdr_links", "連結的 App", "Linked apps"),
        };

        private static readonly Dictionary<string, (string zh, string en)> Lookup =
            Table.ToDictionary(t => t.key, t => (t.zh, t.en));
    }
}
