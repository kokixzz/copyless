using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace Copyless
{
    class ReleaseInfo
    {
        public Version Version;
        public string Tag, DownloadUrl, Sha256, PageUrl;
        public long Size;
    }

    /// <summary>Checks the project's GitHub releases and installs newer versions.</summary>
    static class Updater
    {
        const string Repo = "kokixzz/copyless";
        const string AssetName = "Copyless-Setup.exe";

        public static readonly Version Current = Normalize(typeof(Updater).Assembly.GetName().Version);

        static Updater()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        public static string CurrentText { get { return Current.ToString(3); } }

        public static bool IsNewer(ReleaseInfo info) { return info != null && info.Version > Current; }

        static HttpWebRequest Request(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Copyless/" + CurrentText;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true;
            return req;
        }

        /// <summary>Latest published release, or null if it has no installer asset.</summary>
        public static ReleaseInfo GetLatest()
        {
            HttpWebRequest req = Request("https://api.github.com/repos/" + Repo + "/releases/latest", 15000);
            req.Accept = "application/vnd.github+json";
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                json = reader.ReadToEnd();

            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            string tag = root["tag_name"] as string;
            Version v;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out v)) return null;

            var info = new ReleaseInfo { Tag = tag, Version = Normalize(v), PageUrl = root["html_url"] as string };
            var assets = root["assets"] as object[];
            if (assets == null) return null;
            foreach (object o in assets)
            {
                var a = o as Dictionary<string, object>;
                if (a == null || !string.Equals(a["name"] as string, AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                info.DownloadUrl = a["browser_download_url"] as string;
                info.Size = Convert.ToInt64(a["size"]);
                object digest;
                if (a.TryGetValue("digest", out digest) && digest is string && ((string)digest).StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    info.Sha256 = ((string)digest).Substring(7);
            }
            return info.DownloadUrl == null ? null : info;
        }

        static string UpdateDir
        {
            // When installed, download into Program Files so only administrators can modify the file before it runs.
            get { return Installer.IsInstalledCopy() ? Path.Combine(Installer.Dir, "update") : Path.Combine(Path.GetTempPath(), "Copyless-Update"); }
        }

        /// <summary>Downloads the installer and verifies its size, SHA-256 and version.</summary>
        public static string Download(ReleaseInfo info)
        {
            string dir = UpdateDir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, AssetName);
            if (File.Exists(path)) File.Delete(path);

            HttpWebRequest req = Request(info.DownloadUrl, 60000);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                src.CopyTo(dst);

            try
            {
                if (new FileInfo(path).Length != info.Size)
                    throw new Exception("The download is incomplete.");
                if (info.Sha256 != null && !string.Equals(Sha256Of(path), info.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("The download does not match the published checksum.");
                Version fileVersion;
                if (!Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out fileVersion) || Normalize(fileVersion) != info.Version)
                    throw new Exception("The download has an unexpected version.");
            }
            catch
            {
                try { File.Delete(path); } catch { }
                throw;
            }
            return path;
        }

        static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        /// <summary>Starts the downloaded installer. It closes this instance, replaces the program and restarts it.</summary>
        public static void Install(string path)
        {
            Process.Start(new ProcessStartInfo(path, "--install --quiet") { UseShellExecute = false });
        }

        public static void CleanUp()
        {
            foreach (string dir in new[] { Path.Combine(Installer.Dir, "update"), Path.Combine(Path.GetTempPath(), "Copyless-Update") })
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
