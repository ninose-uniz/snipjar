using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Snipjar
{
    // Checks GitHub for a newer release at most once a day and says so. It never
    // downloads or installs anything — the person decides. Off with updatecheck=off.
    static class Updater
    {
        const string LatestRelease =
            "https://api.github.com/repos/ninose-uniz/snipjar/releases/latest";
        public const string ReleasesPage =
            "https://github.com/ninose-uniz/snipjar/releases/latest";

        static string StampPath
        {
            get { return Path.Combine(Config.Dir, "update-check"); }
        }

        public static void CheckLater(Action<string> onNewer)
        {
            if (!Config.CheckUpdates) return;
            if (CheckedToday()) return;

            Thread worker = new Thread(delegate()
            {
                Thread.Sleep(20000); // let the app settle before touching the network
                string newer = null;
                try { newer = FindNewer(); }
                catch { return; } // offline, rate limited, whatever — silence is fine
                StampToday();
                if (newer != null) onNewer(newer);
            });
            worker.IsBackground = true;
            worker.Start();
        }

        static bool CheckedToday()
        {
            try
            {
                if (!File.Exists(StampPath)) return false;
                string stamp = File.ReadAllText(StampPath).Trim();
                return stamp == DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            catch { return false; }
        }

        static void StampToday()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(StampPath, DateTime.UtcNow.ToString("yyyy-MM-dd"));
            }
            catch { }
        }

        // Returns the newer version string, or null when this copy is current.
        static string FindNewer()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(LatestRelease);
            request.UserAgent = "Snipjar/" + Current();
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000;

            string body;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            // One field out of a large payload; a JSON parser would be more than this needs.
            Match tag = Regex.Match(body, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success) return null;

            string latest = tag.Groups[1].Value.TrimStart('v', 'V');
            return IsNewer(latest, Current()) ? latest : null;
        }

        public static string Current()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version.Major + "." + version.Minor + "." + version.Build;
        }

        static bool IsNewer(string candidate, string current)
        {
            Version a, b;
            if (!TryParse(candidate, out a) || !TryParse(current, out b)) return false;
            return a > b;
        }

        static bool TryParse(string text, out Version version)
        {
            version = null;
            Match match = Regex.Match(text, "^(\\d+)\\.(\\d+)(?:\\.(\\d+))?");
            if (!match.Success) return false;
            int major = int.Parse(match.Groups[1].Value);
            int minor = int.Parse(match.Groups[2].Value);
            int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            version = new Version(major, minor, patch);
            return true;
        }
    }
}
