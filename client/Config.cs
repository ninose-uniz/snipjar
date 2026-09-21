using System;
using System.IO;

namespace Shotlink
{
    static class Config
    {
        public static string Endpoint;
        public static string Token;

        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "shotlink");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "config.ini"); }
        }

        public static string FallbackDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "shotlink");
            }
        }

        // Returns false when the file is missing or incomplete; error explains what to fix.
        public static bool Load(out string error)
        {
            error = null;
            Directory.CreateDirectory(Dir);

            if (!File.Exists(FilePath))
            {
                WriteTemplate();
                error = "設定ファイルを作りました。endpoint と token を書いてから、もう一度実行してください。\r\n\r\n"
                    + FilePath;
                return false;
            }

            string endpoint = null;
            string token = null;
            foreach (string line in File.ReadAllLines(FilePath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
                string value = trimmed.Substring(eq + 1).Trim();
                if (key == "endpoint") endpoint = value;
                else if (key == "token") token = value;
            }

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(token))
            {
                error = "設定が足りません。endpoint と token の両方を書いてください。\r\n\r\n" + FilePath;
                return false;
            }

            Endpoint = endpoint;
            Token = token;
            return true;
        }

        static void WriteTemplate()
        {
            File.WriteAllText(FilePath,
                "# shotlink\r\n"
                + "# endpoint: Worker のアップロード先\r\n"
                + "# token   : wrangler secret put UPLOAD_TOKEN で登録したもの\r\n"
                + "endpoint=https://shotlink.example.workers.dev/upload\r\n"
                + "token=\r\n");
        }
    }
}
