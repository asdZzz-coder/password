using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using password.Models;
using password.Services;

namespace password
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<AccountEntry> _entries;
        private readonly ICollectionView _view;
        private readonly UpdateService _updater = new();
        private readonly ObservableCollection<string> _formLinks = new();

        public MainWindow()
        {
            InitializeComponent();
            // 小螢幕（例如筆電）上不要讓視窗高度超出可用範圍
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 20);
            _entries = new ObservableCollection<AccountEntry>(DataStore.Load());
            _view = CollectionViewSource.GetDefaultView(_entries);
            _view.SortDescriptions.Add(new SortDescription(nameof(AccountEntry.AppName), ListSortDirection.Ascending));
            EntryList.ItemsSource = _view;
            LinksList.ItemsSource = _formLinks;
            UpdateStatus();
        }

        // ---------- 啟動時檢查更新（詢問使用者） ----------

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckForUpdateAsync(manual: false);
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdateAsync(manual: true);
        }

        private async Task CheckForUpdateAsync(bool manual)
        {
            if (!_updater.IsInstalled)
            {
                if (manual)
                    MessageBox.Show("目前是開發版（非安裝版），無法線上更新。", "檢查更新");
                return;
            }

            try
            {
                var info = await _updater.CheckAsync();
                if (info == null)
                {
                    if (manual) MessageBox.Show($"目前已是最新版本（{_updater.CurrentVersion}）。", "檢查更新");
                    return;
                }

                var answer = MessageBox.Show(
                    $"發現新版本 {info.Version}（目前 {_updater.CurrentVersion}）。\n\n是否現在更新？更新完成後程式會自動重新啟動。",
                    "有新版本", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                StatusText.Text = "下載更新中…";
                await _updater.DownloadAndLaunchAsync(info, p => Dispatcher.Invoke(() => StatusText.Text = $"下載更新中… {p}%"));
                // 安裝程式已啟動，結束本程式讓它能覆蓋檔案；安裝完成後會自動重新開啟
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateStatus();
                // 啟動時的自動檢查失敗（例如沒網路）不打擾使用者
                if (manual) MessageBox.Show("檢查更新失敗：" + ex.Message, "檢查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- 搜尋 / 選取 ----------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var keyword = SearchBox.Text.Trim();
            _view.Filter = keyword.Length == 0
                ? null
                : o => o is AccountEntry a &&
                       (a.AppName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        a.Username.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        a.Note.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        a.LinkedApps.Any(l => l.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntryList.SelectedItem is AccountEntry a)
                FillForm(a);
        }

        private void FillForm(AccountEntry a)
        {
            AppBox.Text = a.AppName;
            UserBox.Text = a.Username;
            SetPassword(a.Password);
            NoteBox.Text = a.Note;
            LinkBox.Clear();
            _formLinks.Clear();
            foreach (var l in a.LinkedApps) _formLinks.Add(l);
        }

        private void ClearForm()
        {
            AppBox.Clear();
            UserBox.Clear();
            SetPassword("");
            NoteBox.Clear();
            LinkBox.Clear();
            _formLinks.Clear();
        }

        // ---------- 已連結的 App（條列清單） ----------

        private void AddLink_Click(object sender, RoutedEventArgs e) => AddPendingLink();

        private void LinkBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            AddPendingLink();
            e.Handled = true;
        }

        /// <summary>把輸入框裡的文字加入清單；重複（不分大小寫）的略過。</summary>
        private void AddPendingLink()
        {
            var name = LinkBox.Text.Trim();
            if (name.Length == 0) return;
            if (!_formLinks.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                _formLinks.Add(name);
            LinkBox.Clear();
            LinkBox.Focus();
        }

        private void RemoveLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: string name })
                _formLinks.Remove(name);
        }

        // ---------- 密碼欄位（PasswordBox / TextBox 切換） ----------

        private string GetPassword() => ShowPwd.IsChecked == true ? PwdText.Text : PwdBox.Password;

        private void SetPassword(string value)
        {
            PwdBox.Password = value;
            PwdText.Text = value;
        }

        private void ShowPwd_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowPwd.IsChecked == true)
            {
                PwdText.Text = PwdBox.Password;
                PwdText.Visibility = Visibility.Visible;
                PwdBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                PwdBox.Password = PwdText.Text;
                PwdBox.Visibility = Visibility.Visible;
                PwdText.Visibility = Visibility.Collapsed;
            }
        }

        // ---------- 新增 / 修改 / 刪除 / 複製 ----------

        private AccountEntry? ReadForm()
        {
            if (AppBox.Text.Trim().Length == 0)
            {
                MessageBox.Show("請輸入 App 名稱。", "提示");
                return null;
            }
            AddPendingLink(); // 輸入框裡打了字但還沒按「加入」的，一併存起來
            return new AccountEntry
            {
                AppName = AppBox.Text.Trim(),
                Username = UserBox.Text.Trim(),
                Password = GetPassword(),
                Note = NoteBox.Text,
                LinkedApps = _formLinks.ToList(),
            };
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var entry = ReadForm();
            if (entry == null) return;
            _entries.Add(entry);
            PersistAndRefresh();
            EntryList.SelectedItem = entry;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (EntryList.SelectedItem is not AccountEntry selected)
            {
                MessageBox.Show("請先在左邊選一筆要修改的資料，或按「新增」。", "提示");
                return;
            }
            var edited = ReadForm();
            if (edited == null) return;

            selected.AppName = edited.AppName;
            selected.Username = edited.Username;
            selected.Password = edited.Password;
            selected.Note = edited.Note;
            selected.LinkedApps = edited.LinkedApps;
            PersistAndRefresh();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (EntryList.SelectedItem is not AccountEntry selected) return;
            var ok = MessageBox.Show($"確定刪除「{selected.AppName}」？", "刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;

            _entries.Remove(selected);
            ClearForm();
            PersistAndRefresh();
        }

        private void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            int count = _entries.Count;
            if (count == 0)
            {
                MessageBox.Show("目前沒有任何資料可以清除。", "清除全部資料");
                return;
            }

            // 兩段確認，且預設按鈕都是「否」，避免手滑按 Enter 就刪掉
            var first = MessageBox.Show(
                $"即將刪除全部 {count} 筆帳號資料。\n\n建議先用「匯出 Excel」備份。\n\n確定要繼續嗎？",
                "清除全部資料（1/2）", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (first != MessageBoxResult.Yes) return;

            var second = MessageBox.Show(
                $"最後確認：真的要永久刪除全部 {count} 筆資料嗎？\n\n刪除後無法復原。",
                "清除全部資料（2/2）", MessageBoxButton.YesNo, MessageBoxImage.Stop, MessageBoxResult.No);
            if (second != MessageBoxResult.Yes) return;

            _entries.Clear();
            SearchBox.Clear();
            ClearForm();
            PersistAndRefresh();
            StatusText.Text = $"已清除全部資料（共 {count} 筆）";
        }

        private void CopyPwd_Click(object sender, RoutedEventArgs e)
        {
            var pwd = GetPassword();
            if (pwd.Length == 0) return;
            try
            {
                Clipboard.SetText(pwd);
                StatusText.Text = "密碼已複製到剪貼簿";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 剪貼簿被其他程式占用時會丟例外
                MessageBox.Show("剪貼簿目前被其他程式占用，請稍後再試。", "複製密碼");
            }
        }

        private void PersistAndRefresh()
        {
            var selected = EntryList.SelectedItem as AccountEntry;
            try
            {
                DataStore.Save(_entries);
            }
            catch (Exception ex)
            {
                MessageBox.Show("儲存失敗，資料尚未寫入硬碟：" + ex.Message, "儲存", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            _view.Refresh();
            // Refresh 會清掉選取，重新選回同一筆（改名後排序位置會變）
            if (selected != null && _entries.Contains(selected))
                EntryList.SelectedItem = selected;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            CountText.Text = $"共 {_entries.Count} 筆";
            StatusText.Text = $"版本 {_updater.CurrentVersion}";
        }

        // ---------- Excel 匯出 / 匯入 ----------

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Excel 檔案 (*.xlsx)|*.xlsx",
                FileName = $"帳號密碼_{DateTime.Now:yyyyMMdd}.xlsx",
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ExcelService.Export(_entries, dlg.FileName);
                MessageBox.Show("匯出完成。\n\n注意：Excel 內的密碼是明文，請妥善保管，用完建議刪除。", "匯出 Excel");
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯出失敗：" + ex.Message, "匯出 Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Excel 檔案 (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;

            List<AccountEntry> imported;
            try
            {
                imported = ExcelService.Import(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("匯入失敗：" + ex.Message, "匯入 Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (imported.Count == 0)
            {
                MessageBox.Show("這個檔案裡沒有可匯入的資料（第一列需為標題列：App、帳號、密碼、備註）。", "匯入 Excel");
                return;
            }

            var mode = MessageBox.Show(
                $"讀到 {imported.Count} 筆資料。\n\n是 = 合併到現有資料（App 與帳號相同者略過）\n否 = 清除現有資料，完全以 Excel 為準\n取消 = 不匯入",
                "匯入 Excel", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (mode == MessageBoxResult.Cancel) return;

            if (mode == MessageBoxResult.No)
                _entries.Clear();

            int added = 0;
            foreach (var item in imported)
            {
                bool exists = _entries.Any(x =>
                    string.Equals(x.AppName, item.AppName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Username, item.Username, StringComparison.OrdinalIgnoreCase));
                if (exists) continue;
                _entries.Add(item);
                added++;
            }

            ClearForm();
            PersistAndRefresh();
            MessageBox.Show($"匯入完成，新增 {added} 筆。", "匯入 Excel");
        }
    }
}
