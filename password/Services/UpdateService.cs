using Velopack;
using Velopack.Sources;

namespace password.Services
{
    /// <summary>
    /// 透過 GitHub Releases 做線上更新（Velopack）。
    /// 只有「安裝版」才能更新；直接從 Visual Studio 執行時 IsInstalled 為 false，會略過。
    /// </summary>
    public class UpdateService
    {
        private const string RepoUrl = "https://github.com/asdZzz-coder/password";

        private readonly UpdateManager _manager =
            new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));

        public bool IsInstalled => _manager.IsInstalled;

        public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "開發版";

        /// <summary>檢查是否有新版；沒有、或非安裝版則回傳 null。</summary>
        public Task<UpdateInfo?> CheckAsync() => _manager.CheckForUpdatesAsync();

        /// <summary>下載並重新啟動套用更新。</summary>
        public async Task DownloadAndApplyAsync(UpdateInfo info, Action<int>? progress = null)
        {
            await _manager.DownloadUpdatesAsync(info, progress);
            _manager.ApplyUpdatesAndRestart(info);
        }
    }
}
