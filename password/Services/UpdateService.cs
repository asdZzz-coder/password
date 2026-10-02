using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace password.Services
{
    public record UpdateInfo(Version Version, string DownloadUrl, long Size);

    /// <summary>
    /// 線上更新：向 GitHub Releases 查詢最新版，使用者同意後下載新版安裝檔（Setup.exe）並執行。
    ///
    /// 不使用 Velopack 內建的 Update.exe 來套用更新：它沒有數位簽章，
    /// 在開啟「智慧型應用程式控制 / 應用程式控制原則」的電腦上會被 Windows 封鎖。
    /// 只有「安裝版」才能更新；直接從 Visual Studio 執行時 IsInstalled 為 false，會略過。
    /// </summary>
    public class UpdateService
    {
        private const string Owner = "asdZzz-coder";
        private const string Repo = "password";
        private const string SetupAssetName = "PasswordKeeper-win-Setup.exe";

        private static readonly HttpClient Http = CreateClient();

        // 只用來判斷是否為安裝版、取得目前版本；實際的檢查與下載由本類別自行處理
        private readonly UpdateManager _manager =
            new(new GithubSource($"https://github.com/{Owner}/{Repo}", accessToken: null, prerelease: false));

        public bool IsInstalled => _manager.IsInstalled;

        public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? Loc.T("dev_version");

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("PasswordKeeper-Updater"); // GitHub API 要求有 User-Agent
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>檢查是否有新版；沒有、或非安裝版則回傳 null。</summary>
        public async Task<UpdateInfo?> CheckAsync()
        {
            if (!IsInstalled || _manager.CurrentVersion == null) return null;

            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            if (!Version.TryParse(_manager.CurrentVersion.ToString().Split('-', '+')[0], out var current)) return null;
            if (latest <= current) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == SetupAssetName)
                    return new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64());
            }
            return null; // 該版本還沒有附上安裝檔（打包尚未完成）
        }

        /// <summary>下載新版安裝檔並啟動它；呼叫端應在這之後結束程式，讓安裝程式能覆蓋檔案。</summary>
        public async Task DownloadAndLaunchAsync(UpdateInfo info, Action<int>? progress = null)
        {
            // 先清掉先前（例如失敗或中斷的更新）遺留的舊安裝檔，避免一直堆積
            CleanupService.DeleteMatching(Path.GetTempPath(), CleanupService.SetupFilePattern);
            var path = Path.Combine(Path.GetTempPath(), $"PasswordKeeper-Setup-{info.Version}.exe");

            using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? info.Size;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(path);
                var buf = new byte[81920];
                long done = 0;
                int last = -1, n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != last) { last = pct; progress?.Invoke(pct); }
                }
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
