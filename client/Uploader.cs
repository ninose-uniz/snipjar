using System;
using System.IO;
using System.Net;
using System.Text;

namespace Shotlink
{
    static class Uploader
    {
        const int TimeoutMs = 20000;

        // Posts the PNG and returns the URL the Worker replied with.
        public static string Upload(byte[] png)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.Expect100Continue = false;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Config.Endpoint);
            request.Method = "POST";
            request.ContentType = "image/png";
            request.Headers["Authorization"] = "Bearer " + Config.Token;
            request.UserAgent = "shotlink/1.0";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.ContentLength = png.Length;

            using (Stream body = request.GetRequestStream())
            {
                body.Write(png, 0, png.Length);
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string url = reader.ReadToEnd().Trim();
                    if (url.Length == 0) throw new Exception("サーバーが URL を返しませんでした");
                    return url;
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
