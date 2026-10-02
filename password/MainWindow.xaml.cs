using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
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
            Loc.LanguageChanged += UpdateStatus; // 切換語言時重新整理標題與狀態列
        }

        private void Lang_Click(object sender, RoutedEventArgs e) => Loc.Toggle();

        // ---------- Windows 11：標題列底色與視窗背景同色，看起來是一整片 ----------

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_CAPTION_COLOR = 35;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var bg = ((SolidColorBrush)FindResource("AppBgBrush")).Color;
            int colorRef = bg.R | (bg.G << 8) | (bg.B << 16); // COLORREF = 0x00BBGGRR
            // Windows 10 不支援此屬性，呼叫會回傳錯誤碼，直接忽略即可
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
        }

        // ---------- 啟動時檢查更新（詢問使用者） ----------

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CleanupService.RunInBackground(); // 清掉更新後遺留的舊檔
            DesktopShortcutService.EnsureOnce(_updater.IsInstalled); // 安裝版第一次開啟時補上桌面捷徑

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
                    MessageBox.Show(Loc.T("update_dev"), Loc.T("update_title"));
                return;
            }

            try
            {
                var info = await _updater.CheckAsync();
                if (info == null)
                {
                    if (manual) MessageBox.Show(Loc.T("update_latest", _updater.CurrentVersion), Loc.T("update_title"));
                    return;
                }

                var answer = MessageBox.Show(
                    Loc.T("update_found", info.Version, _updater.CurrentVersion),
                    Loc.T("update_found_title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                StatusText.Text = Loc.T("downloading");
                await _updater.DownloadAndLaunchAsync(info, p => Dispatcher.Invoke(() => StatusText.Text = Loc.T("downloading_pct", p)));
                // 安裝程式已啟動，結束本程式讓它能覆蓋檔案；安裝完成後會自動重新開啟
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateStatus();
                // 啟動時的自動檢查失敗（例如沒網路）不打擾使用者
                if (manual) MessageBox.Show(Loc.T("update_failed", ex.Message), Loc.T("update_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(Loc.T("need_app_name"), Loc.T("hint_title"));
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
                MessageBox.Show(Loc.T("select_first"), Loc.T("hint_title"));
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
            var ok = MessageBox.Show(Loc.T("delete_confirm", selected.AppName), Loc.T("delete_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
                MessageBox.Show(Loc.T("delall_none"), Loc.T("delall_title"));
                return;
            }

            // 兩段確認，且預設按鈕都是「否」，避免手滑按 Enter 就刪掉
            var first = MessageBox.Show(
                Loc.T("delall_1", count),
                Loc.T("delall_1_title"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (first != MessageBoxResult.Yes) return;

            var second = MessageBox.Show(
                Loc.T("delall_2", count),
                Loc.T("delall_2_title"), MessageBoxButton.YesNo, MessageBoxImage.Stop, MessageBoxResult.No);
            if (second != MessageBoxResult.Yes) return;

            _entries.Clear();
            SearchBox.Clear();
            ClearForm();
            PersistAndRefresh();
            StatusText.Text = Loc.T("delall_done", count);
        }

        private void CopyUser_Click(object sender, RoutedEventArgs e) => CopyToClipboard(UserBox.Text, "copy_user_done");

        private void CopyPwd_Click(object sender, RoutedEventArgs e) => CopyToClipboard(GetPassword(), "copy_done");

        private void CopyToClipboard(string text, string doneKey)
        {
            if (text.Length == 0) return;
            try
            {
                Clipboard.SetText(text);
                StatusText.Text = Loc.T(doneKey);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 剪貼簿被其他程式占用時會丟例外
                MessageBox.Show(Loc.T("copy_busy"), Loc.T("copy_title"));
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
                MessageBox.Show(Loc.T("save_failed", ex.Message), Loc.T("save_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            _view.Refresh();
            // Refresh 會清掉選取，重新選回同一筆（改名後排序位置會變）
            if (selected != null && _entries.Contains(selected))
                EntryList.SelectedItem = selected;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            // 視窗標題列顯示版本：安裝版為「帳號密碼紀錄 v1.0.6」，直接從 Visual Studio 執行則標示開發版
            Title = _updater.IsInstalled ? Loc.T("title_installed", _updater.CurrentVersion) : Loc.T("title_dev");
            CountText.Text = Loc.T("count_text", _entries.Count);
            StatusText.Text = Loc.T("version_text", _updater.CurrentVersion);
        }

        // ---------- Excel 匯出 / 匯入 ----------

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = Loc.T("excel_filter"),
                FileName = Loc.T("export_filename", DateTime.Now.ToString("yyyyMMdd")),
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ExcelService.Export(_entries, dlg.FileName);
                MessageBox.Show(Loc.T("export_done"), Loc.T("export_title"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("export_failed", ex.Message), Loc.T("export_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("excel_filter") };
            if (dlg.ShowDialog() != true) return;

            List<AccountEntry> imported;
            try
            {
                imported = ExcelService.Import(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("import_failed", ex.Message), Loc.T("import_title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (imported.Count == 0)
            {
                MessageBox.Show(Loc.T("import_empty"), Loc.T("import_title"));
                return;
            }

            var mode = MessageBox.Show(
                Loc.T("import_confirm", imported.Count),
                Loc.T("import_title"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
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
            MessageBox.Show(Loc.T("import_done", added), Loc.T("import_title"));
        }
    }
}
