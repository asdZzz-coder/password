using System.Windows;
using Velopack;

namespace password
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // 必須最先執行：處理 Velopack 安裝 / 更新 / 解除安裝的 hook
            VelopackApp.Build().Run();

            // 只允許開一個視窗，避免兩個視窗互相覆蓋對方存的資料
            using var mutex = new Mutex(true, @"Local\PasswordKeeper.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show("帳號密碼紀錄已經在執行中。", "帳號密碼紀錄");
                return;
            }

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
