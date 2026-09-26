using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Durango.Online;

/// <summary>
/// Tokens de autenticacao emitidos depois de username+senha validos.
/// Ficam somente em memoria: restart do servidor encerra todas as sessoes de login.
/// Na Parte 3 estes tokens serao a fonte confiavel para resolver o account_id.
/// </summary>
public static class AuthTokenStore
{
    private sealed class Entry
    {
        public string AccountId;
        public string Username;
        public DateTimeOffset ExpiresAt;
    }

    public sealed class IssuedToken
    {
        public string Token { get; init; }
        public string AccountId { get; init; }
        public string Username { get; init; }
        public long ExpiresInSeconds { get; init; }
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Tokens = new(StringComparer.Ordinal);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public static IssuedToken Issue(string accountId, string username)
    {
        if (string.IsNullOrEmpty(accountId))
            throw new ArgumentException("accountId vazio", nameof(accountId));

        lock (Sync)
        {
            CleanupExpiredLocked();

            string token;
            do
            {
                token = Base64Url(RandomNumberGenerator.GetBytes(32));
            }
            while (Tokens.ContainsKey(token));

            DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
            Tokens[token] = new Entry
            {
                AccountId = accountId,
                Username = username ?? string.Empty,
                ExpiresAt = expiresAt
            };

            return new IssuedToken
            {
                Token = token,
                AccountId = accountId,
                Username = username ?? string.Empty,
                ExpiresInSeconds = (long)Lifetime.TotalSeconds
            };
        }
    }

    public static bool TryResolve(string token, out string accountId, out string username)
    {
        accountId = null;
        username = null;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        string normalized = token.Trim();

        lock (Sync)
        {
            if (!Tokens.TryGetValue(normalized, out Entry entry))
                return false;

            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Tokens.Remove(normalized);
                return false;
            }

            accountId = entry.AccountId;
            username = entry.Username;
            return true;
        }
    }

    public static bool Revoke(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        lock (Sync) return Tokens.Remove(token.Trim());
    }

    public static int Count
    {
        get
        {
            lock (Sync)
            {
                CleanupExpiredLocked();
                return Tokens.Count;
            }
        }
    }

    private static void CleanupExpiredLocked()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<string> expired = null;

        foreach (KeyValuePair<string, Entry> pair in Tokens)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                expired ??= new List<string>();
                expired.Add(pair.Key);
            }
        }

        if (expired == null) return;
        foreach (string token in expired) Tokens.Remove(token);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
