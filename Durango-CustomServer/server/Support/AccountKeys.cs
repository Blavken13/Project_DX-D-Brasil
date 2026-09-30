using System;

namespace Durango.Online;

/// <summary>
/// Normalização da identidade persistente da conta.
///
/// O owner_key dos personagens recebe o account_id emitido pelo AccountStore após
/// autenticação por usuário e senha. Esse valor é independente do computador/dispositivo.
/// Nunca aceite account_id enviado pelo cliente como prova de identidade: /sessions e
/// /accounts resolvem a conta exclusivamente pelo auth_token do servidor.
/// </summary>
public static class AccountKeys
{
    /// <summary>Limite máximo para evitar abuso de memória/log</summary>
    private const int MaxLength = 128;

    /// <summary>
    /// Normaliza um account_id; retorna <c>null</c> quando inválido
    ///
    /// Retorno null deve ser rejeitado pelo chamador; nunca significa acesso anônimo
    ///
    /// </summary>
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string key = raw.Trim();
        if (key.Length > MaxLength) return null;

        // Aceita somente letras, números, '-' e '_' para manter logs/identificadores seguros
        foreach (char c in key)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return null;
        }
        return key;
    }

    /// <summary>Compara duas identidades; valor vazio nunca é proprietário</summary>
    public static bool Same(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>Forma reduzida para logs; não exponha o account_id completo</summary>
    public static string ForLog(string key) =>
        string.IsNullOrEmpty(key) ? "(nenhum)" : key[..Math.Min(8, key.Length)] + "…";
}
