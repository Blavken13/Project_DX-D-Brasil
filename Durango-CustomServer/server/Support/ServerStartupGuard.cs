using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace DurangoServerNx;

internal static class ServerStartupGuard
{
    // Runs before loading game data or saves. The real listeners still bind exclusively.
    public static void CheckPorts(int gatewayPort, int gamePort)
    {
        if (gatewayPort < 1 || gatewayPort > 65534 || gamePort < 1 || gamePort > 65535 || gatewayPort == gamePort)
            throw new ArgumentException("Use portas TCP diferentes entre 1 e 65535; o gateway deve permitir também a porta UDP seguinte.");

        using var gateway = Bind(gatewayPort, SocketType.Stream, ProtocolType.Tcp);
        using var game = Bind(gamePort, SocketType.Stream, ProtocolType.Tcp);
        using var discovery = Bind(gatewayPort + 1, SocketType.Dgram, ProtocolType.Udp);
    }

    private static Socket Bind(int port, SocketType type, ProtocolType protocol)
    {
        var socket = new Socket(AddressFamily.InterNetwork, type, protocol);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return socket;
        }
        catch (SocketException e)
        {
            socket.Dispose();
            string reason = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? "já está em uso por outro processo. Encerre a instância anterior com Ctrl+C antes de iniciar novamente"
                : $"não pôde ser aberta: {e.Message}";
            throw new IOException($"A porta {protocol.ToString().ToUpperInvariant()} {port} {reason}.", e);
        }
    }

    // Keep the handle until shutdown finishes, even when another instance uses other ports.
    // A leftover file after a crash is harmless: the OS releases the exclusive handle.
    public static FileStream AcquireSaveLease(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            return new FileStream(Path.Combine(directory, ".server.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            throw new IOException($"Não foi possível obter acesso exclusivo aos saves em '{directory}'. " +
                "Outra instância pode estar usando esse progresso ou a pasta não permite acesso. " +
                "Encerre a instância anterior com Ctrl+C; não exclua o arquivo de bloqueio. " + e.Message, e);
        }
    }
}
