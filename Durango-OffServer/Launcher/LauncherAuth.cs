using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LostHorizon.PC
{
    public sealed class LauncherSession
    {
        public string Gateway { get; set; }
        public string AccountId { get; set; }
        public string Username { get; set; }
        public string Token { get; set; }
        public long ExpiresUtcTicks { get; set; }

        public bool IsValid
        {
            get { return !String.IsNullOrEmpty(Token) && Regex.IsMatch(Token, "^[A-Za-z0-9_-]{43}$") &&
                !String.IsNullOrEmpty(AccountId) && !String.IsNullOrEmpty(Username) &&
                ExpiresUtcTicks > DateTime.UtcNow.AddMinutes(1).Ticks; }
        }

        public void AddToEnvironment(System.Diagnostics.ProcessStartInfo start)
        {
            if (!IsValid) throw new InvalidOperationException("A sessão expirou. Entre novamente.");
            start.EnvironmentVariables["LOST_HORIZON_GATEWAY"] = Gateway;
            start.EnvironmentVariables["LOST_HORIZON_ACCOUNT"] = AccountId;
            start.EnvironmentVariables["LOST_HORIZON_USERNAME"] = Username;
            start.EnvironmentVariables["LOST_HORIZON_TOKEN"] = Token;
            start.EnvironmentVariables["LOST_HORIZON_EXPIRES"] = ExpiresUtcTicks.ToString(CultureInfo.InvariantCulture);
        }
    }

    public sealed class AuthFailure : Exception
    {
        public bool SessionLost { get; private set; }
        public AuthFailure(string message, bool lost) : base(message) { SessionLost = lost; }
    }

    public sealed class LauncherAuth
    {
        public const string DefaultGateway = "http://179.197.72.129:8190";
        private readonly string gateway;
        private readonly string storage;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LostHorizon.PC.Session.v1");
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public LauncherAuth(string url, string folder)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.UserInfo) ||
                !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Endereço de servidor inválido.");
            gateway = url.TrimEnd('/');
            using (SHA256 hash = SHA256.Create())
                storage = Path.Combine(folder, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(gateway.ToLowerInvariant())))
                    .Replace("-", "").ToLowerInvariant() + ".session");
        }

        public LauncherSession Load()
        {
            try
            {
                if (!File.Exists(storage)) return null;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(storage), Entropy, DataProtectionScope.CurrentUser);
                LauncherSession session = json.Deserialize<LauncherSession>(Encoding.UTF8.GetString(plain));
                Array.Clear(plain, 0, plain.Length);
                if (session != null && session.IsValid && String.Equals(session.Gateway, gateway, StringComparison.OrdinalIgnoreCase))
                    return session;
            }
            catch (IOException) { }
            catch (CryptographicException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return null;
        }

        public void Save(LauncherSession session)
        {
            if (session == null || !session.IsValid || !String.Equals(session.Gateway, gateway, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Sessão inválida.");
            Directory.CreateDirectory(Path.GetDirectoryName(storage));
            byte[] plain = Encoding.UTF8.GetBytes(json.Serialize(session));
            byte[] encoded = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(plain, 0, plain.Length);
            string temp = storage + ".tmp";
            File.WriteAllBytes(temp, encoded);
            if (File.Exists(storage)) File.Replace(temp, storage, null);
            else File.Move(temp, storage);
        }

        public void Clear() { if (File.Exists(storage)) File.Delete(storage); }

        public async Task<LauncherSession> Authenticate(string username, string password, string confirmation, bool register)
        {
            username = (username ?? "").Trim();
            if (!Regex.IsMatch(username, "^[A-Za-z0-9_.-]{3,32}$"))
                throw new AuthFailure("Use um usuário de 3 a 32 letras, números, ponto, hífen ou sublinhado.", false);
            if (String.IsNullOrEmpty(password) || password.Length > 128)
                throw new AuthFailure("Informe uma senha de até 128 caracteres.", false);
            if (register && password.Length < 8)
                throw new AuthFailure("A senha precisa ter pelo menos 8 caracteres.", false);
            if (register && password != confirmation)
                throw new AuthFailure("As senhas não coincidem.", false);
            string form = "username=" + Encode(username) + "&password=" + Encode(password);
            if (register) await Request("/auth/register", form + "&password_confirm=" + Encode(confirmation), true, false);
            Dictionary<string, object> result = await Request("/auth/login", form, true, false);
            long seconds;
            object value;
            if (!result.TryGetValue("expires_in", out value) ||
                !Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out seconds) || seconds <= 0 || seconds > 604800)
                throw new AuthFailure("O servidor retornou uma sessão inválida.", false);
            LauncherSession session = new LauncherSession {
                Gateway = gateway, AccountId = Text(result, "account_id"),
                Username = Text(result, "username"), Token = Text(result, "auth_token"),
                ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(seconds).Ticks };
            if (!session.IsValid) throw new AuthFailure("O servidor retornou uma sessão inválida.", false);
            return session;
        }

        public async Task Validate(LauncherSession session)
        {
            if (session == null || !session.IsValid || !String.Equals(session.Gateway, gateway, StringComparison.OrdinalIgnoreCase))
                throw new AuthFailure("Sua sessão expirou. Entre novamente.", true);
            Dictionary<string, object> result = await Request("/accounts", "token=" + Encode(session.Token), false, true);
            if (!result.ContainsKey("players")) throw new AuthFailure("Não foi possível carregar os personagens.", false);
        }

        public async Task<int> Online()
        {
            try { Dictionary<string, object> result = await Request("/status", null, true, false); return Convert.ToInt32(result["online"]); }
            catch { return -1; }
        }

        private async Task<Dictionary<string, object>> Request(string path, string form, bool requireOk, bool session)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(gateway + path);
            request.Method = form == null ? "GET" : "POST";
            request.Accept = "application/json";
            request.UserAgent = "LostHorizon/PC-alpha";
            request.AllowAutoRedirect = false;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            HttpWebResponse response = null;
            System.Threading.Timer timeout = new System.Threading.Timer(delegate { request.Abort(); }, null, 15000, System.Threading.Timeout.Infinite);
            try
            {
                if (form != null)
                {
                    byte[] body = Encoding.UTF8.GetBytes(form);
                    request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
                    request.ContentLength = body.Length;
                    using (Stream stream = await request.GetRequestStreamAsync()) await stream.WriteAsync(body, 0, body.Length);
                    Array.Clear(body, 0, body.Length);
                }
                try { response = (HttpWebResponse)await request.GetResponseAsync(); }
                catch (WebException error)
                {
                    response = error.Response as HttpWebResponse;
                    if (response == null) throw new AuthFailure("Não foi possível conectar ao servidor. Tente novamente.", false);
                }
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    Dictionary<string, object> result;
                    try { result = json.Deserialize<Dictionary<string, object>>(await reader.ReadToEndAsync()); }
                    catch { throw new AuthFailure("O servidor retornou uma resposta inválida.", false); }
                    int status = (int)response.StatusCode;
                    if (status == 401 || status == 403)
                        throw new AuthFailure(session ? "Sua sessão expirou. Entre novamente." : "Usuário ou senha incorretos.", session);
                    if (status == 429) throw new AuthFailure("Muitas tentativas. Aguarde um pouco antes de tentar novamente.", false);
                    object ok;
                    bool success = result != null && result.TryGetValue("ok", out ok) && ok is bool && (bool)ok;
                    if (status < 200 || status >= 300 || result == null || result.ContainsKey("error") || (requireOk && !success))
                    {
                        string error = result == null ? "" : Text(result, "error");
                        throw new AuthFailure(error == "username_taken" ? "Esse usuário já existe. Entre com sua senha." :
                            error == "password_too_short" ? "A senha precisa ter pelo menos 8 caracteres." :
                            "Não foi possível concluir a solicitação. Tente novamente.", false);
                    }
                    return result;
                }
            }
            catch (WebException) { throw new AuthFailure("Não foi possível conectar ao servidor. Tente novamente.", false); }
            finally { timeout.Dispose(); }
        }

        private static string Encode(string value) { return Uri.EscapeDataString(value ?? ""); }
        private static string Text(Dictionary<string, object> values, string key)
        { object value; return values.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
    }
}
