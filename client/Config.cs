using System;
using System.IO;

namespace Snipjar
{
    static class Config
    {
        public static string Endpoint;
        public static string Token;
        public static bool UploadAlways = true;
        public static bool CheckUpdates = true;
        public static string SaveDir;

        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "snipjar");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "config.ini"); }
        }

        public static string DefaultSaveDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "snipjar");
            }
        }

        // The gallery lives next to the upload endpoint on the same Worker.
        public static string GalleryUrl
        {
            get
            {
                if (string.IsNullOrEmpty(Endpoint)) return null;
                try
                {
                    Uri uri = new Uri(Endpoint);
                    return uri.GetLeftPart(UriPartial.Authority) + "/gallery";
                }
                catch (UriFormatException)
                {
                    return null;
                }
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
            string upload = null;
            string saveDir = null;
            string updateCheck = null;

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
                else if (key == "upload") upload = value.ToLowerInvariant();
                else if (key == "savedir") saveDir = value;
                else if (key == "updatecheck") updateCheck = value.ToLowerInvariant();
            }

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(token))
            {
                error = "設定が足りません。endpoint と token の両方を書いてください。\r\n\r\n" + FilePath;
                return false;
            }

            Endpoint = endpoint;
            Token = token;
            UploadAlways = upload != "never" && upload != "no" && upload != "off";
            CheckUpdates = updateCheck != "off" && updateCheck != "no" && updateCheck != "never";
            SaveDir = string.IsNullOrEmpty(saveDir) ? DefaultSaveDir : saveDir;
            return true;
        }

        static void WriteTemplate()
        {
            File.WriteAllText(FilePath,
                "# Snipjar\r\n"
                + "# endpoint   : Worker のアップロード先\r\n"
                + "# token      : wrangler secret put UPLOAD_TOKEN で登録したもの\r\n"
                + "# upload     : always = 一覧に残すため裏で R2 にも上げる / never = 一切上げない\r\n"
                + "# savedir    : 「保存」の保存先 (省略時は Pictures\\snipjar)\r\n"
                + "# updatecheck: off にすると GitHub への更新確認をしない\r\n"
                + "endpoint=https://snipjar.example.workers.dev/upload\r\n"
                + "token=\r\n"
                + "upload=always\r\n");
        }
    }
}
