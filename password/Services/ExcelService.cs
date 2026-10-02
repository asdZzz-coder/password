using System.IO;
using ClosedXML.Excel;
using password.Models;

namespace password.Services
{
    public static class ExcelService
    {
        private static readonly string[] Headers = { "App", "帳號", "密碼", "備註", "連結的 App" };

        public static void Export(IEnumerable<AccountEntry> entries, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("帳號密碼");
            for (int c = 0; c < Headers.Length; c++)
                ws.Cell(1, c + 1).Value = Headers[c];
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (var e in entries)
            {
                // 一律寫成文字，避免 Excel 把純數字密碼的前導 0 吃掉
                ws.Cell(r, 1).SetValue(Escape(e.AppName));
                ws.Cell(r, 2).SetValue(Escape(e.Username));
                ws.Cell(r, 3).SetValue(Escape(e.Password));
                ws.Cell(r, 4).SetValue(Escape(e.Note));
                // 連結的 App 一個一行，放在同一格
                ws.Cell(r, 5).SetValue(Escape(string.Join("\n", e.LinkedApps)));
                ws.Cell(r, 5).Style.Alignment.WrapText = true;
                r++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
        }

        // 開頭的 ' 會被 ClosedXML 當成 Excel 的「文字前綴」而吞掉，多加一個才能原樣保留
        private static string Escape(string s) => s.StartsWith('\'') ? "'" + s : s;

        public static List<AccountEntry> Import(string path)
        {
            // 允許讀取「正在被 Excel 開啟」的檔案，否則會因檔案被鎖定而失敗
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.FirstOrDefault();
            if (ws == null) return new List<AccountEntry>();
            var result = new List<AccountEntry>();
            foreach (var row in ws.RowsUsed().Skip(1)) // 跳過標題列
            {
                var entry = new AccountEntry
                {
                    AppName = row.Cell(1).GetFormattedString().Trim(),
                    Username = row.Cell(2).GetFormattedString().Trim(),
                    Password = row.Cell(3).GetFormattedString(),
                    Note = row.Cell(4).GetFormattedString(),
                    // 舊版匯出檔沒有第 5 欄，此時為空清單；換行或「、」都視為分隔
                    LinkedApps = row.Cell(5).GetFormattedString()
                        .Split(new[] { '\r', '\n', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                };
                if (entry.AppName.Length == 0 && entry.Username.Length == 0 && entry.Password.Length == 0)
                    continue;
                result.Add(entry);
            }
            return result;
        }
    }
}
