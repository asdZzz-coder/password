using System.IO;
using System.Text.Json;
using password.Models;
using password.Services;

namespace password.Tests
{
    /// <summary>資料夾分類的自動測試：名稱檢查、篩選、重新命名、刪除、存檔格式與 Excel 匯出匯入。</summary>
    public class FolderServiceTests
    {
        private static AccountEntry Entry(string app, string folder = "") => new() { AppName = app, Username = "demo", Folder = folder };

        // ---------- 名稱檢查 ----------

        [Theory]
        [InlineData("", FolderNameError.Empty)]
        [InlineData("   ", FolderNameError.Empty)]
        [InlineData("工作", FolderNameError.Duplicate)]
        [InlineData(" WORK ", FolderNameError.Duplicate)]
        [InlineData("Games", FolderNameError.None)]
        public void Validate_RejectsEmptyAndDuplicateNames(string name, FolderNameError expected) =>
            Assert.Equal(expected, FolderService.Validate(name, new[] { "工作", "Work" }));

        [Fact]
        public void Validate_RejectsTooLongNames()
        {
            Assert.Equal(FolderNameError.None, FolderService.Validate(new string('a', FolderService.MaxNameLength), []));
            Assert.Equal(FolderNameError.TooLong, FolderService.Validate(new string('a', FolderService.MaxNameLength + 1), []));
        }

        [Fact]
        public void Validate_RenameMayChangeOnlyCase()
        {
            var folders = new[] { "work", "Home" };
            Assert.Equal(FolderNameError.None, FolderService.Validate("Work", folders, renaming: "work"));
            Assert.Equal(FolderNameError.Duplicate, FolderService.Validate("home", folders, renaming: "work"));
        }

        // ---------- 篩選 ----------

        [Fact]
        public void Filters_MatchAllNoFolderAndNamedFolder()
        {
            var inWork = Entry("A", "Work");
            var loose = Entry("B");
            Assert.True(FolderFilter.All.Matches(inWork));
            Assert.True(FolderFilter.All.Matches(loose));
            Assert.True(FolderFilter.NoFolder.Matches(loose));
            Assert.False(FolderFilter.NoFolder.Matches(inWork));
            Assert.True(FolderFilter.Of("work").Matches(inWork)); // 不分大小寫
            Assert.False(FolderFilter.Of("Work").Matches(loose));
            Assert.True(FolderFilter.NoFolder.Matches(Entry("C", "   "))); // 只有空白也算無資料夾
        }

        [Fact]
        public void SameAs_ComparesKindAndNameIgnoringCase()
        {
            Assert.True(FolderFilter.Of("Work").SameAs(FolderFilter.Of("WORK")));
            Assert.False(FolderFilter.Of("Work").SameAs(FolderFilter.Of("Home")));
            Assert.False(FolderFilter.All.SameAs(FolderFilter.NoFolder));
        }

        // ---------- 清單整理 ----------

        [Fact]
        public void Merge_AddsFoldersUsedByEntriesAndRemovesDuplicatesAndBlanks()
        {
            var merged = FolderService.Merge(new[] { "Work", " ", "work", "Bank" }, new[] { Entry("A", "Games"), Entry("B", "WORK"), Entry("C") });
            Assert.Equal(new[] { "Bank", "Games", "Work" }, merged);
        }

        [Fact]
        public void Merge_KeepsEmptyFolders()
        {
            Assert.Equal(new[] { "Empty" }, FolderService.Merge(new[] { "Empty" }, []));
        }

        [Fact]
        public void Canonical_ReturnsStoredSpellingOrEmpty()
        {
            var folders = new[] { "Work" };
            Assert.Equal("Work", FolderService.Canonical(folders, "work"));
            Assert.Equal("", FolderService.Canonical(folders, "Missing"));
            Assert.Equal("", FolderService.Canonical(folders, null));
        }

        // ---------- 重新命名 / 刪除 ----------

        [Fact]
        public void Rename_MovesEntriesAndKeepsOthers()
        {
            var a = Entry("A", "Work");
            var b = Entry("B", "work");
            var c = Entry("C", "Home");
            var (folders, moved) = FolderService.Rename(new[] { "Work", "Home" }, new[] { a, b, c }, "Work", " Office ");
            Assert.Equal(2, moved);
            Assert.Equal(new[] { "Home", "Office" }, folders);
            Assert.Equal("Office", a.Folder);
            Assert.Equal("Office", b.Folder);
            Assert.Equal("Home", c.Folder);
        }

        [Fact]
        public void Rename_CaseOnlyChangeKeepsOneFolder()
        {
            var a = Entry("A", "work");
            var (folders, _) = FolderService.Rename(new[] { "work" }, new[] { a }, "work", "Work");
            Assert.Equal(new[] { "Work" }, folders);
            Assert.Equal("Work", a.Folder);
        }

        [Fact]
        public void Delete_MovesEntriesToNoFolderWithoutDeletingThem()
        {
            var a = Entry("A", "Work");
            var c = Entry("C", "Home");
            var entries = new List<AccountEntry> { a, c };
            var (folders, moved) = FolderService.Delete(new[] { "Work", "Home" }, entries, "work");
            Assert.Equal(1, moved);
            Assert.Equal(new[] { "Home" }, folders);
            Assert.Equal(2, entries.Count);
            Assert.Equal("", a.Folder);
            Assert.Equal("Home", c.Folder);
        }

        // ---------- 存檔格式（與舊版相容） ----------

        [Fact]
        public void Json_OldDataWithoutFolderLoadsAsNoFolder()
        {
            var old = JsonSerializer.Deserialize<List<AccountEntry>>("""[{"AppName":"A","Username":"u","Password":"p","Note":"","LinkedApps":[]}]""")!;
            Assert.Equal("", old[0].Folder);
            Assert.True(FolderFilter.NoFolder.Matches(old[0]));
        }

        [Fact]
        public void Json_FolderRoundTrips()
        {
            var json = JsonSerializer.Serialize(new[] { Entry("A", "Work") });
            Assert.Contains("\"Folder\":\"Work\"", json);
            Assert.Equal("Work", JsonSerializer.Deserialize<List<AccountEntry>>(json)![0].Folder);
        }

        // ---------- Excel ----------

        [Fact]
        public void Excel_ExportImportKeepsFolder()
        {
            var path = Path.Combine(Path.GetTempPath(), $"pk-folder-test-{Guid.NewGuid():N}.xlsx");
            try
            {
                ExcelService.Export(new[] { Entry("Demo A", "工作"), Entry("Demo B"), Entry("Demo C", "'quoted") }, path);
                var back = ExcelService.Import(path);
                Assert.Equal(new[] { "工作", "", "'quoted" }, back.Select(e => e.Folder));
                Assert.Equal(new[] { "Demo A", "Demo B", "Demo C" }, back.Select(e => e.AppName));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Excel_OldFileWithoutFolderColumnImportsAsNoFolder()
        {
            var path = Path.Combine(Path.GetTempPath(), $"pk-folder-test-{Guid.NewGuid():N}.xlsx");
            try
            {
                using (var wb = new ClosedXML.Excel.XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Sheet1");
                    ws.Cell(1, 1).Value = "App"; ws.Cell(1, 2).Value = "帳號"; ws.Cell(1, 3).Value = "密碼"; ws.Cell(1, 4).Value = "備註";
                    ws.Cell(2, 1).Value = "Demo"; ws.Cell(2, 2).Value = "demo"; ws.Cell(2, 3).Value = "fake-pass";
                    wb.SaveAs(path);
                }
                var back = ExcelService.Import(path);
                Assert.Single(back);
                Assert.Equal("", back[0].Folder);
            }
            finally { File.Delete(path); }
        }
    }
}
