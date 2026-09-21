using System;
using System.IO;
using System.Net;
using System.Text;

namespace Shotlink
{
    class Shot
    {
        public string Key;
        public string Url;
    }

    static class Uploader
    {
        const int TimeoutMs = 20000;

        // Posts the PNG and returns the key and URL the Worker replied with.
        public static Shot Upload(byte[] png)
        {
            string body = Post(Config.Endpoint, "image/png", png);
            string[] lines = body.Split('\n');
            if (lines.Length < 2) throw new Exception("サーバーの応答が想定と違います");

            Shot shot = new Shot();
            shot.Key = lines[0].Trim();
            shot.Url = lines[1].Trim();
            if (shot.Key.Length == 0 || shot.Url.Length == 0)
            {
                throw new Exception("サーバーが URL を返しませんでした");
            }
            return shot;
        }

        // Best effort: without it the gallery just falls back to the full image.
        public static void UploadThumb(string key, byte[] jpeg)
        {
            Uri endpoint = new Uri(Config.Endpoint);
            string url = endpoint.GetLeftPart(UriPartial.Authority) + "/upload/thumb/" + key;
            Post(url, "image/jpeg", jpeg);
        }

        static string Post(string url, string contentType, byte[] payload)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.Expect100Continue = false;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = contentType;
            request.Headers["Authorization"] = "Bearer " + Config.Token;
            request.UserAgent = "shotlink/1.0";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.ContentLength = payload.Length;

            using (Stream body = request.GetRequestStream())
            {
                body.Write(payload, 0, payload.Length);
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                throw new Exception(Describe(ex));
            }
        }

        static string Describe(WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response == null) return "サーバーに届きませんでした (" + ex.Status + ")";

            string detail = "";
            try
            {
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    detail = reader.ReadToEnd().Trim();
                }
            }
            catch { }

            string label = (int)response.StatusCode == 401
                ? "トークンが違います"
                : "サーバーが " + (int)response.StatusCode + " を返しました";
            return detail.Length > 0 ? label + " — " + detail : label;
        }
    }
}
