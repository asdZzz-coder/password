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

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
