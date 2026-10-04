using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

        // 資料夾：清單（含空資料夾）與左側目前選的篩選
        private List<string> _folders;
        private FolderFilter _folderFilter = FolderFilter.All;
        private bool _rebuildingFolders;

        public MainWindow()
        {
            InitializeComponent();
            // 小螢幕（例如筆電）上不要讓視窗超出可用範圍
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 20);
            Width = Math.Min(Width, SystemParameters.WorkArea.Width - 20);
            _entries = new ObservableCollection<AccountEntry>(DataStore.Load());
            _folders = FolderService.Merge(DataStore.LoadFolders(), _entries);
            _view = CollectionViewSource.GetDefaultView(_entries);
            _view.SortDescriptions.Add(new SortDescription(nameof(AccountEntry.AppName), ListSortDirection.Ascending));
            EntryList.ItemsSource = _view;
            LinksList.ItemsSource = _formLinks;
            RebuildFolders();
            ApplyFilter();
            UpdateStatus();
            Loc.LanguageChanged += UpdateStatus; // 切換語言時重新整理標題與狀態列
            Loc.LanguageChanged += RebuildFolders; // 「所有項目」「無資料夾」跟著換語言
            Loc.LanguageChanged += UpdateEmptyHint;
            Loc.LanguageChanged += UpdateThemeButton;
            ThemeService.ThemeChanged += OnThemeChanged; // 切換主題（或系統深淺色改變）時更新標題列與按鈕
            UpdateThemeButton();
        }

        private void Lang_Click(object sender, RoutedEventArgs e) => Loc.Toggle();

        // ---------- 主題：跟隨系統 / 淺色 / 深色 ----------

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Cycle();
            StatusText.Text = Loc.T("theme_changed", ThemeName(ThemeService.Mode));
        }

        private static string ThemeName(AppTheme mode) => Loc.T(mode switch
        {
            AppTheme.Light => "theme_light",
            AppTheme.Dark => "theme_dark",
            _ => "theme_system",
        });

        private void OnThemeChanged()
        {
            UpdateThemeButton();
            ApplyTitleBar();
        }

        private void UpdateThemeButton()
        {
            ThemeIcon.Text = ThemeService.Mode switch
            {
                AppTheme.Light => "", // 太陽
                AppTheme.Dark => "",  // 月亮
                _ => "",               // 電腦（跟隨系統）
            };
            ThemeButton.ToolTip = Loc.T("theme_tip", ThemeName(ThemeService.Mode));
        }

        // ---------- Windows 11：標題列底色與視窗背景同色，看起來是一整片 ----------

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyTitleBar();
        }

        private void ApplyTitleBar() => WindowTheme.ApplyTitleBar(this);

        // ---------- 啟動時檢查更新（詢問使用者） ----------

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CleanupService.RunInBackground(); // 清掉更新後遺留的舊檔
            DesktopShortcutService.TidyUp(_updater.IsInstalled);     // 更新後：重複捷徑只留最新的、工作列釘選改指向新版
            DesktopShortcutService.EnsureOnce(_updater.IsInstalled); // 安裝版第一次開啟時補上桌面捷徑

            await CheckForUpdateAsync(manual: false);
        }

        private void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DesktopShortcutService.Create(_updater.IsInstalled) == ShortcutResult.SourceNotFound)
                {
                    MessageBox.Show(Loc.T("shortcut_no_source"), Loc.T("shortcut_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                StatusText.Text = Loc.T("shortcut_done");
                MessageBox.Show(Loc.T("shortcut_done"), Loc.T("shortcut_title"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or COMException)
            {
                MessageBox.Show(Loc.T("shortcut_failed", ex.Message), Loc.T("shortcut_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        /// <summary>清單只顯示左側選的資料夾裡、符合搜尋文字的帳號。</summary>
        private void ApplyFilter()
        {
            var keyword = SearchBox.Text.Trim();
            var folder = _folderFilter;
            _view.Filter = o => o is AccountEntry a && folder.Matches(a) &&
                (keyword.Length == 0 ||
                 a.AppName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 a.Username.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 a.Note.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 a.LinkedApps.Any(l => l.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
            UpdateEmptyHint();
        }

        private void UpdateEmptyHint() =>
            EmptyText.Text = Loc.T(_entries.Count == 0 ? "empty_list" : "empty_filtered");

        // ---------- 資料夾 ----------

        /// <summary>依目前資料重建左側資料夾清單（含數量）與表單的資料夾下拉選單，並保留原本的選取。</summary>
        private void RebuildFolders()
        {
            var items = new List<FolderItem> { new(FolderFilter.All, Loc.T("folder_all"), _entries.Count) };
            items.AddRange(_folders.Select(f => new FolderItem(FolderFilter.Of(f), f, _entries.Count(FolderFilter.Of(f).Matches))));
            items.Add(new FolderItem(FolderFilter.NoFolder, Loc.T("folder_none"), _entries.Count(FolderFilter.NoFolder.Matches)));

            var current = items.FirstOrDefault(i => i.Filter.SameAs(_folderFilter)) ?? items[0];
            _rebuildingFolders = true;
            FolderList.ItemsSource = items;
            FolderList.SelectedItem = current;
            _rebuildingFolders = false;
            if (current.Filter != _folderFilter) // 原本選的資料夾被刪掉了 → 回到「所有項目」
            {
                _folderFilter = current.Filter;
                ApplyFilter();
            }

            var formFolder = FolderService.Canonical(_folders, FolderBox.SelectedValue as string);
            FolderBox.ItemsSource = FolderOptions();
            FolderBox.SelectedValue = formFolder;
        }

        private List<FolderOption> FolderOptions() =>
            _folders.Select(f => new FolderOption(f, f)).Prepend(new FolderOption("", Loc.T("folder_none"))).ToList();

        /// <summary>新增帳號時預設放進左側目前選的資料夾。</summary>
        private string DefaultFolder => _folderFilter.Kind == FolderFilterKind.Folder ? _folderFilter.Name : "";

        private void SetFormFolder(string folder) => FolderBox.SelectedValue = FolderService.Canonical(_folders, folder);

        private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rebuildingFolders || FolderList.SelectedItem is not FolderItem item) return;
            bool hadSelection = EntryList.SelectedItem != null;
            _folderFilter = item.Filter;
            ApplyFilter();
            if (EntryList.SelectedItem != null) return;
            // 原本選的帳號不在這個資料夾 → 清空表單，準備在這個資料夾新增
            if (hadSelection) ClearForm();
            else SetFormFolder(DefaultFolder);
        }

        private string? FolderNameMessage(string name, string? renaming = null) =>
            FolderService.Validate(name, _folders, renaming) switch
            {
                FolderNameError.Empty => Loc.T("folder_err_empty"),
                FolderNameError.TooLong => Loc.T("folder_err_long", FolderService.MaxNameLength),
                FolderNameError.Duplicate => Loc.T("folder_err_dup"),
                _ => null,
            };

        /// <summary>詢問名稱並建立資料夾；取消則回傳 null。</summary>
        private string? CreateFolder()
        {
            var name = InputDialog.Ask(this, Loc.T("folder_new_title"), Loc.T("folder_name_prompt"), "", n => FolderNameMessage(n));
            if (name == null) return null;
            _folders = FolderService.Merge(_folders.Append(name), []);
            return name;
        }

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            var name = CreateFolder();
            if (name == null) return;
            _folderFilter = FolderFilter.Of(name); // 建好就切過去
            PersistAndRefresh();
            if (EntryList.SelectedItem == null) SetFormFolder(DefaultFolder); // 接著新增的帳號直接放進這個資料夾
            StatusText.Text = Loc.T("folder_created", name);
        }

        private void RenameFolder(string oldName)
        {
            var name = InputDialog.Ask(this, Loc.T("folder_rename_title"), Loc.T("folder_name_prompt"), oldName, n => FolderNameMessage(n, oldName));
            if (name == null || name == oldName) return;
            bool viewing = _folderFilter.SameAs(FolderFilter.Of(oldName));
            var formFolder = FolderBox.SelectedValue as string;
            (_folders, _) = FolderService.Rename(_folders, _entries, oldName, name);
            if (viewing) _folderFilter = FolderFilter.Of(name);
            if (FolderService.SameName(formFolder, oldName)) SetFormFolderAfterRefresh(name);
            PersistAndRefresh();
            StatusText.Text = Loc.T("folder_renamed", name);
        }

        private void DeleteFolder(string name)
        {
            int count = _entries.Count(FolderFilter.Of(name).Matches);
            var ok = MessageBox.Show(this, Loc.T("folder_delete_confirm", name, count), Loc.T("folder_delete_tip"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;
            (_folders, _) = FolderService.Delete(_folders, _entries, name);
            PersistAndRefresh();
            StatusText.Text = Loc.T("folder_deleted", name);
        }

        // 重新整理後下拉選單才有新名稱，所以先記下來，PersistAndRefresh 重建選單後再選
        private string? _pendingFormFolder;
        private void SetFormFolderAfterRefresh(string name) => _pendingFormFolder = name;

        private void RenameFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FolderItem { IsUserFolder: true } item })
                RenameFolder(item.Filter.Name);
        }

        private void DeleteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FolderItem { IsUserFolder: true } item })
                DeleteFolder(item.Filter.Name);
        }

        /// <summary>從右鍵點到的位置往上找出清單項目（點在空白處則為 null）。</summary>
        private static T? ItemUnderMouse<T>(ContextMenuEventArgs e) where T : class
        {
            for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is ListBoxItem { DataContext: T item }) return item;
            return null;
        }

        private static MenuItem MenuEntry(string header, Action onClick, bool isChecked = false, Brush? foreground = null)
        {
            var mi = new MenuItem { Header = header, IsChecked = isChecked };
            if (foreground != null) mi.Foreground = foreground;
            mi.Click += (_, _) => onClick();
            return mi;
        }

        // 資料夾右鍵：重新命名 / 刪除（「所有項目」「無資料夾」沒有選單）
        private void FolderList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (ItemUnderMouse<FolderItem>(e) is not { IsUserFolder: true } item) { e.Handled = true; return; }
            var menu = FolderList.ContextMenu;
            menu.Items.Clear();
            menu.Items.Add(MenuEntry(Loc.T("folder_rename_tip"), () => RenameFolder(item.Filter.Name)));
            menu.Items.Add(MenuEntry(Loc.T("folder_delete_tip"), () => DeleteFolder(item.Filter.Name),
                foreground: (Brush)FindResource("DangerBrush")));
        }

        // 帳號右鍵：移到資料夾（目前所在的資料夾打勾）
        private void EntryList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (ItemUnderMouse<AccountEntry>(e) is not { } entry) { e.Handled = true; return; }
            EntryList.SelectedItem = entry;
            var menu = EntryList.ContextMenu;
            menu.Items.Clear();
            menu.Items.Add(new MenuItem { Header = Loc.T("move_to"), IsEnabled = false, FontSize = 12 });
            foreach (var option in FolderOptions())
                menu.Items.Add(MenuEntry(option.Display, () => MoveEntry(entry, option.Name),
                    isChecked: FolderService.SameName(entry.Folder, option.Name)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry(Loc.T("move_to_new"), () =>
            {
                var name = CreateFolder();
                if (name != null) MoveEntry(entry, name);
            }));
        }

        private void MoveEntry(AccountEntry entry, string folder)
        {
            entry.Folder = folder;
            if (EntryList.SelectedItem == entry) SetFormFolderAfterRefresh(folder);
            PersistAndRefresh();
            StatusText.Text = Loc.T("moved_to", entry.AppName, folder.Length == 0 ? Loc.T("folder_none") : folder);
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
            SetFormFolder(a.Folder);
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
            SetFormFolder(DefaultFolder);
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
                Folder = FolderBox.SelectedValue as string ?? "",
            };
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var entry = ReadForm();
            if (entry == null) return;
            _entries.Add(entry);
            // 存到別的資料夾時，左側切到那個資料夾，才看得到剛新增的這筆
            if (!_folderFilter.Matches(entry))
                _folderFilter = entry.Folder.Length == 0 ? FolderFilter.NoFolder : FolderFilter.Of(entry.Folder);
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
            selected.Folder = edited.Folder;
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
            // 帳號用到、但清單裡沒有的資料夾（例如匯入的）一併補進清單
            _folders = FolderService.Merge(_folders, _entries);
            try
            {
                DataStore.Save(_entries);
                DataStore.SaveFolders(_folders);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.T("save_failed", ex.Message), Loc.T("save_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RebuildFolders();
            if (_pendingFormFolder != null)
            {
                SetFormFolder(_pendingFormFolder);
                _pendingFormFolder = null;
            }
            ApplyFilter();
            // 重新篩選會清掉選取，重新選回同一筆（改名後排序位置會變）
            if (selected != null && _entries.Contains(selected))
            {
                EntryList.SelectedItem = selected;
                // 移到別的資料夾後不在目前的清單裡了 → 清空表單，避免誤按「新增」複製一筆
                if (EntryList.SelectedItem == null) ClearForm();
            }
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
