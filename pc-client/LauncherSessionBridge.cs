using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Durango.Logic.Clusters;

namespace LostHorizon.PC
{
    public static class LauncherSessionBridge
    {
        private static bool imported;
        private static string gateway;
        public static bool Active { get { Import(); return gateway != null; } }
        public static string Gateway { get { Import(); return gateway; } }
        public static List<string[]> Servers
        {
            get { Import(); return new List<string[]> { new string[] { "Lost Horizon Brasil", gateway } }; }
        }

        private static string Take(string key)
        {
            string value = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, null);
            return value;
        }

        public static void Import()
        {
            if (imported) return;
            imported = true;
            string url = Take("LOST_HORIZON_GATEWAY");
            string account = Take("LOST_HORIZON_ACCOUNT");
            string username = Take("LOST_HORIZON_USERNAME");
            string token = Take("LOST_HORIZON_TOKEN");
            string expires = Take("LOST_HORIZON_EXPIRES");
            long ticks;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment) ||
                String.IsNullOrEmpty(account) || String.IsNullOrEmpty(username) || String.IsNullOrEmpty(token) ||
                !Regex.IsMatch(token, "^[A-Za-z0-9_-]{43}$") ||
                !Int64.TryParse(expires, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) ||
                ticks <= DateTime.UtcNow.AddMinutes(1).Ticks || ticks > DateTime.UtcNow.AddDays(7).Ticks)
                return;
            gateway = url.TrimEnd('/');
            long seconds = (ticks - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerSecond;
            OffServerLink.SetAuthentication(gateway, account, username, token, seconds);
        }
    }
}
