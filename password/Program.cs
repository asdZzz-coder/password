using System.Windows;
using password.Services;
using Velopack;

namespace password
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // 必須最先執行：處理 Velopack 安裝 / 更新 / 解除安裝的 hook。
            // 關閉「啟動時自動套用已下載更新」：更新改由 UpdateService 下載 Setup.exe 執行，
            // 不再使用未簽章的 Update.exe（會被智慧型應用程式控制封鎖）。
            VelopackApp.Build().SetAutoApplyOnStartup(false).Run();

            Loc.Load(); // 讀取使用者選的介面語言

            // 只允許開一個視窗，避免兩個視窗互相覆蓋對方存的資料
            using var mutex = new Mutex(true, @"Local\PasswordKeeper.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show(Loc.T("already_running"), Loc.T("app_title"));
                return;
            }

            var app = new App();
            app.InitializeComponent();
            Loc.Apply(); // 必須在 App.xaml 載入之後，字串資源才不會被覆蓋
            app.Run();
        }
    }
}
