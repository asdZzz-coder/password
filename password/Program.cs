using System.Windows;
using password.Services;

namespace password
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Loc.Load(); // 讀取使用者選的介面語言

            // 只允許開一個視窗，避免兩個視窗互相覆蓋對方存的資料
            using var mutex = new Mutex(true, @"Local\PasswordKeeper.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show(Loc.T("already_running"), Loc.T("app_title"));
                return;
            }

            // 安裝版使用固定的工作列身分，更新後新版視窗才會跟工作列釘選合併
            if (new UpdateService().IsInstalled) DesktopShortcutService.ApplyAppId();

            var app = new App();
            app.InitializeComponent();
            Loc.Apply(); // 必須在 App.xaml 載入之後，字串資源才不會被覆蓋
            app.Run();
        }
    }
}
