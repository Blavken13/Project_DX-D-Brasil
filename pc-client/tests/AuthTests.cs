using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LostHorizon.PC;

namespace LostHorizon.Tests
{
    public static class AuthTests
    {
        private static int assertions;
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            assertions++;
        }
        private static void Fails(Func<Task> operation, bool lost)
        {
            try { operation().GetAwaiter().GetResult(); }
            catch (AuthFailure failure) { Check(failure.SessionLost == lost, "Wrong session-loss status"); return; }
            throw new Exception("Expected authentication failure");
        }
        public static void Run(string gateway, string folder)
        {
            var auth = new LauncherAuth(gateway, folder);
            Check(auth.Load() == null, "No existing session in isolated gateway");
            Check(auth.Online().GetAwaiter().GetResult() == 3, "Public server status");
            var session = auth.Authenticate("player", "test&password+123", "", false).GetAwaiter().GetResult();
            Check(session.IsValid && session.AccountId == "test-account", "Successful login and encoded password");
            auth.Validate(session).GetAwaiter().GetResult();
            Check(true, "Character account validation");
            Fails(() => auth.Authenticate("player", "wrong-password", "", false), false);
            Fails(() => auth.Authenticate("??", "password", "", false), false);
            Fails(() => auth.Authenticate("player", "short", "short", true), false);
            Fails(() => auth.Authenticate("player", "password", "different", true), false);
            Fails(() => auth.Authenticate("invalidttl", "test&password+123", "", false), false);
            Fails(() => auth.Authenticate("invalidtoken", "test&password+123", "", false), false);
            Fails(() => auth.Authenticate("existing", "test&password+123", "test&password+123", true), false);
            Check(auth.Authenticate("newuser", "test&password+123", "test&password+123", true)
                .GetAwaiter().GetResult().IsValid, "Register then authenticate");
            var rejected = new LauncherSession { Gateway = gateway, AccountId = "test-account", Username = "player",
                Token = new String('B', 43), ExpiresUtcTicks = session.ExpiresUtcTicks };
            Fails(() => auth.Validate(rejected), true);
            rejected.ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(-1).Ticks;
            Fails(() => auth.Validate(rejected), true);
            auth.Save(session);
            var files = Directory.GetFiles(folder, "*.session");
            Check(files.Length == 1, "One cache file scoped to gateway");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains(session.Token), "Session encrypted with DPAPI");
            Check(auth.Load().Token == session.Token, "DPAPI round-trip");
            auth.Save(session);
            Check(auth.Load().AccountId == session.AccountId, "Atomic saved-session replacement");
            var other = new LauncherAuth(gateway + "/other", folder);
            Check(other.Load() == null, "Sessions cannot cross gateways");
            Fails(() => other.Validate(session), true);
            var start = new ProcessStartInfo { FileName = "DurangoV2.exe", UseShellExecute = false };
            session.AddToEnvironment(start);
            Check(start.EnvironmentVariables["LOST_HORIZON_TOKEN"] == session.Token, "Child-process handoff");
            Check(!start.Arguments.Contains(session.Token), "Token absent from command line");
            string unrelated = Path.Combine(folder, "keep.txt");
            File.WriteAllText(unrelated, "preserve");
            auth.Clear();
            Check(auth.Load() == null && File.Exists(unrelated), "Sign out removes only the scoped cache");
            File.Delete(unrelated);
            Console.WriteLine("PASS: " + assertions + " PC authentication/session checks; isolated HTTP gateway, no real accounts changed.");
        }
    }
}
