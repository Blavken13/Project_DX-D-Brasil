using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Durango.Utils;
using Newtonsoft.Json;

namespace Durango.Online;

/// <summary>
/// Persistencia das contas locais do Durango Brasil.
/// Fase 1: cadastro por username + senha + confirmacao.
/// Login/sessoes entram nas fases seguintes.
/// </summary>
public static class AccountStore
{
    private sealed class Entry
    {
        [JsonProperty("account_id")] public string AccountId;
        [JsonProperty("username")] public string Username;
        [JsonProperty("username_key")] public string UsernameKey;
        [JsonProperty("password_algorithm")] public string PasswordAlgorithm;
        [JsonProperty("password_iterations")] public int PasswordIterations;
        [JsonProperty("password_salt")] public string PasswordSalt;
        [JsonProperty("password_hash")] public string PasswordHash;
        [JsonProperty("created_at")] public double CreatedAt;
    }

    public sealed class RegisterResult
    {
        public bool Ok { get; init; }
        public string Error { get; init; }
        public string AccountId { get; init; }
        public string Username { get; init; }
    }

    public sealed class LoginResult
    {
        public bool Ok { get; init; }
        public string Error { get; init; }
        public string AccountId { get; init; }
        public string Username { get; init; }
    }

    private const int UsernameMinLength = 3;
    private const int UsernameMaxLength = 32;
    private const int PasswordMinLength = 8;
    private const int PasswordMaxLength = 128;

    private const string PasswordAlgorithm = "PBKDF2-SHA256";
    private const int PasswordIterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> ByUsername = new(StringComparer.Ordinal);
    private static string _path;

    public static void Load(string path)
    {
        lock (Sync)
        {
            _path = path;
            ByUsername.Clear();

            Entry[] loaded = SafeSave.ReadWithBackup<Entry[]>(
                path,
                "accounts",
                bytes => Json.Read<Entry[]>(bytes));

            if (loaded == null)
            {
                Console.WriteLine($"[auth] contas: nenhuma base existente em {path}");
                return;
            }

            foreach (Entry entry in loaded)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId) || string.IsNullOrWhiteSpace(entry.Username))
                    continue;

                if (!TryNormalizeUsername(entry.Username, out string display, out string key, out _))
                {
                    Console.WriteLine($"[auth] ignorando conta com username invalido: '{entry.Username}'");
                    continue;
                }

                if (ByUsername.ContainsKey(key))
                {
                    Console.WriteLine($"[auth] ignorando username duplicado na base: '{display}'");
                    continue;
                }

                entry.Username = display;
                entry.UsernameKey = key;
                ByUsername[key] = entry;
            }

            Console.WriteLine($"[auth] contas carregadas: {ByUsername.Count}");
        }
    }

    public static RegisterResult Register(string rawUsername, string password, string passwordConfirm)
    {
        if (!TryNormalizeUsername(rawUsername, out string username, out string usernameKey, out string userError))
            return Fail(userError);

        if (password == null || password.Length < PasswordMinLength)
            return Fail("password_too_short");

        if (password.Length > PasswordMaxLength)
            return Fail("password_too_long");

        if (!PasswordsMatch(password, passwordConfirm))
            return Fail("password_mismatch");

        lock (Sync)
        {
            if (string.IsNullOrEmpty(_path))
                return Fail("account_store_not_loaded");

            if (ByUsername.ContainsKey(usernameKey))
                return Fail("username_taken");

            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                PasswordIterations,
                HashAlgorithmName.SHA256,
                HashBytes);

            var entry = new Entry
            {
                AccountId = Guid.NewGuid().ToString("N"),
                Username = username,
                UsernameKey = usernameKey,
                PasswordAlgorithm = PasswordAlgorithm,
                PasswordIterations = PasswordIterations,
                PasswordSalt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            ByUsername[usernameKey] = entry;
            if (!SaveLocked())
            {
                ByUsername.Remove(usernameKey);
                return Fail("storage_error");
            }

            Console.WriteLine($"[auth] conta criada: user='{username}' id={AccountKeys.ForLog(entry.AccountId)}");

            return new RegisterResult
            {
                Ok = true,
                AccountId = entry.AccountId,
                Username = entry.Username
            };
        }
    }

    public static LoginResult Authenticate(string rawUsername, string password)
    {
        if (!TryNormalizeUsername(rawUsername, out _, out string usernameKey, out _))
            return LoginFail();

        if (password == null || password.Length == 0 || password.Length > PasswordMaxLength)
            return LoginFail();

        lock (Sync)
        {
            if (string.IsNullOrEmpty(_path))
            {
                return new LoginResult
                {
                    Ok = false,
                    Error = "account_store_not_loaded"
                };
            }

            if (!ByUsername.TryGetValue(usernameKey, out Entry entry))
                return LoginFail();

            try
            {
                if (!string.Equals(entry.PasswordAlgorithm, PasswordAlgorithm, StringComparison.Ordinal) ||
                    entry.PasswordIterations <= 0 ||
                    string.IsNullOrEmpty(entry.PasswordSalt) ||
                    string.IsNullOrEmpty(entry.PasswordHash))
                {
                    Console.WriteLine($"[auth] conta '{entry.Username}' possui credencial invalida/corrompida");
                    return LoginFail();
                }

                byte[] salt = Convert.FromBase64String(entry.PasswordSalt);
                byte[] expected = Convert.FromBase64String(entry.PasswordHash);

                if (expected.Length == 0)
                    return LoginFail();

                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    entry.PasswordIterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);

                if (actual.Length != expected.Length ||
                    !CryptographicOperations.FixedTimeEquals(actual, expected))
                {
                    return LoginFail();
                }

                return new LoginResult
                {
                    Ok = true,
                    AccountId = entry.AccountId,
                    Username = entry.Username
                };
            }
            catch (Exception e)
            {
                Console.WriteLine($"[auth] falha ao verificar credencial de '{entry.Username}': {e.Message}");
                return LoginFail();
            }
        }
    }

    public static int Count
    {
        get { lock (Sync) return ByUsername.Count; }
    }

    private static bool TryNormalizeUsername(string raw, out string display, out string key, out string error)
    {
        display = (raw ?? string.Empty).Trim();
        key = null;
        error = null;

        if (display.Length < UsernameMinLength)
        {
            error = "username_too_short";
            return false;
        }
        if (display.Length > UsernameMaxLength)
        {
            error = "username_too_long";
            return false;
        }

        foreach (char c in display)
        {
            bool asciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            bool digit = c >= '0' && c <= '9';
            if (!asciiLetter && !digit && c != '_' && c != '-' && c != '.')
            {
                error = "username_invalid_characters";
                return false;
            }
        }

        key = display.ToLowerInvariant();
        return true;
    }

    private static bool PasswordsMatch(string password, string confirmation)
    {
        if (password == null || confirmation == null) return false;
        byte[] a = Encoding.UTF8.GetBytes(password);
        byte[] b = Encoding.UTF8.GetBytes(confirmation);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static RegisterResult Fail(string error) => new() { Ok = false, Error = error };

    private static LoginResult LoginFail() => new()
    {
        Ok = false,
        Error = "invalid_credentials"
    };

    private static bool SaveLocked()
    {
        Entry[] snapshot = ByUsername.Values
            .OrderBy(x => x.UsernameKey, StringComparer.Ordinal)
            .ToArray();
        byte[] bytes = Json.WriteToBytes(snapshot, indented: true);
        return SafeSave.WriteAtomic(_path, bytes, "accounts");
    }
}
