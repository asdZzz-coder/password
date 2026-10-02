using ClosedXML.Excel;
using password.Models;

namespace password.Services
{
    public static class ExcelService
    {
        private static readonly string[] Headers = { "App", "帳號", "密碼", "備註" };

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
                ws.Cell(r, 1).SetValue(e.AppName);
                ws.Cell(r, 2).SetValue(e.Username);
                ws.Cell(r, 3).SetValue(e.Password);
                ws.Cell(r, 4).SetValue(e.Note);
                r++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
        }

        public static List<AccountEntry> Import(string path)
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();
            var result = new List<AccountEntry>();
            foreach (var row in ws.RowsUsed().Skip(1)) // 跳過標題列
            {
                var entry = new AccountEntry
                {
                    AppName = row.Cell(1).GetFormattedString().Trim(),
                    Username = row.Cell(2).GetFormattedString().Trim(),
                    Password = row.Cell(3).GetFormattedString(),
                    Note = row.Cell(4).GetFormattedString(),
                };
                if (entry.AppName.Length == 0 && entry.Username.Length == 0 && entry.Password.Length == 0)
                    continue;
                result.Add(entry);
            }
            return result;
        }
    }
}
