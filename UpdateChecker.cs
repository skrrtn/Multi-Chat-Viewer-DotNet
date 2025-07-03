using System;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TwitchChatViewer
{
    public static partial class UpdateChecker
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/skrrtn/Multi-Chat-Viewer-DotNet/releases/latest";
        private const string ReleasesPageUrl = "https://github.com/skrrtn/Multi-Chat-Viewer-DotNet/releases";

        public static async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TwitchChatViewer/1.0");
                var response = await client.GetAsync(LatestReleaseApiUrl);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                // Use generated regex for performance and to fix SYSLIB1045
                var tagMatch = TagNameRegex().Match(json);
                if (!tagMatch.Success)
                    return new UpdateCheckResult(false, null, null);
                var latestTag = tagMatch.Groups[1].Value;
                var latestVersion = ParseVersionFromTag(latestTag);
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (latestVersion != null && currentVersion != null && latestVersion > currentVersion)
                {
                    return new UpdateCheckResult(true, latestTag, ReleasesPageUrl);
                }
                return new UpdateCheckResult(false, latestTag, ReleasesPageUrl);
            }
            catch
            {
                return new UpdateCheckResult(false, null, null);
            }
        }

        [GeneratedRegex(@"""tag_name""\s*:\s*""(v[0-9.]+)""")]
        private static partial Regex TagNameRegex();

        private static Version ParseVersionFromTag(string tag)
        {
            // Expects tags like v1.02 or v1.01
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                tag = tag[1..]; // Use range operator to simplify substring
            // Pad to at least 2 version parts
            var parts = tag.Split('.');
            if (parts.Length == 2)
                tag += ".0.0";
            else if (parts.Length == 3)
                tag += ".0";
            if (Version.TryParse(tag, out var version))
                return version;
            return null;
        }
    }

    public record UpdateCheckResult(bool UpdateAvailable, string LatestTag, string ReleaseUrl);
}
