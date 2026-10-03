using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

// Gateway.Process serializes access to this store on the server loop.
internal sealed class AdminAuth
{
    private readonly string _username;
    private readonly byte[] _salt;
    private readonly byte[] _hash;
    private readonly int _iterations;
    private readonly Dictionary<string, DateTimeOffset> _sessions = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _attempts = new();
    internal const int SessionHours = 8;

    public AdminAuth(string path)
    {
        var config = JObject.Parse(File.ReadAllText(path));
        _username = (string)config["username"];
        _salt = Convert.FromBase64String((string)config["salt"]);
        _hash = Convert.FromBase64String((string)config["password_hash"]);
        _iterations = (int)config["iterations"];
        if (string.IsNullOrWhiteSpace(_username) || _salt.Length < 16 || _hash.Length != 32
            || _iterations < 100_000 || _iterations > 1_000_000)
            throw new InvalidDataException("Configuração administrativa inválida.");
    }

    public bool AllowAttempt(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in new List<string>(_attempts.Keys))
        {
            var queue = _attempts[key];
            while (queue.Count > 0 && now - queue.Peek() >= TimeSpan.FromMinutes(1)) queue.Dequeue();
            if (queue.Count == 0) _attempts.Remove(key);
        }
        if (!_attempts.TryGetValue(ip, out var attempts))
        {
            if (_attempts.Count >= 4096) return false;
            _attempts[ip] = attempts = new Queue<DateTimeOffset>();
        }
        if (attempts.Count >= 5) return false;
        attempts.Enqueue(now);
        return true;
    }

    public string Login(string username, string password)
    {
        if (password == null || password.Length > 256) return null;
        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, _salt, _iterations, HashAlgorithmName.SHA256, 32);
        bool valid = CryptographicOperations.FixedTimeEquals(candidate, _hash);
        CryptographicOperations.ZeroMemory(candidate);
        if (!valid || !string.Equals(username, _username, StringComparison.Ordinal)) return null;
        RemoveExpired();
        if (_sessions.Count >= 128) return null;
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = DateTimeOffset.UtcNow.AddHours(SessionHours);
        return token;
    }

    public bool IsValid(string token)
    {
        RemoveExpired();
        return token != null && _sessions.ContainsKey(token);
    }

    public void Logout(string token)
    {
        if (token != null) _sessions.Remove(token);
    }

    private void RemoveExpired()
    {
        foreach (var key in new List<string>(_sessions.Keys))
            if (_sessions[key] <= DateTimeOffset.UtcNow) _sessions.Remove(key);
    }
}
