package com.newdawn.launcher;

import android.content.Context;
import android.os.SystemClock;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.security.SecureRandom;

/** One-use loopback HTTP response, consumed through the engine's usual parser. */
final class AccountSnapshot {
    private final Context context;
    private final ServerSocket server;
    private final String path;
    private final long deadline;
    private volatile byte[] payload;
    private volatile boolean closed;
    AccountSnapshot(Context context, byte[] payload) throws IOException {
        this.context = context; this.payload = payload;
        server = new ServerSocket(0, 1, InetAddress.getByName("127.0.0.1"));
        server.setSoTimeout(1000);
        byte[] secret = new byte[24]; new SecureRandom().nextBytes(secret);
        StringBuilder name = new StringBuilder("/account-cache/");
        for (byte value : secret) name.append(String.format(java.util.Locale.ROOT, "%02x", value & 255));
        path = name.toString(); deadline = SystemClock.elapsedRealtime() + 120000;
    }
    boolean available() { return !closed && SystemClock.elapsedRealtime() < deadline; }
    String url() { return "http://127.0.0.1:" + server.getLocalPort() + path; }
    void start() {
        Thread worker = new Thread(new Runnable() { @Override public void run() { serve(); } }, "LH-account-cache");
        worker.setDaemon(true); worker.start();
    }
    void close() {
        closed = true; payload = null;
        try { server.close(); } catch (IOException ignored) { }
    }
    private void serve() {
        try {
            while (available()) {
                try (Socket client = server.accept()) {
                    client.setSoTimeout(5000);
                    InputStream in = client.getInputStream();
                    ByteArrayOutputStream header = new ByteArrayOutputStream();
                    int tail = 0, next;
                    while ((next = in.read()) != -1) {
                        header.write(next); tail = (tail << 8) | next;
                        if (tail == 0x0d0a0d0a) break;
                        if (header.size() > 8192) throw new IOException("large cache request");
                    }
                    String request = header.toString("US-ASCII");
                    String first = request.split("\r\n", 2)[0];
                    boolean match = first.equals("POST " + path + " HTTP/1.1") || first.equals("GET " + path + " HTTP/1.1");
                    int bodySize = 0;
                    for (String line : request.split("\r\n")) {
                        if (line.toLowerCase(java.util.Locale.ROOT).startsWith("content-length:"))
                            bodySize = Integer.parseInt(line.substring(15).trim());
                        if (line.toLowerCase(java.util.Locale.ROOT).startsWith("transfer-encoding:")) match = false;
                    }
                    if (bodySize < 0 || bodySize > 8192) match = false;
                    if (match) {
                        for (int i = 0; i < bodySize; i++) if (in.read() == -1) throw new IOException("short request");
                        byte[] data = payload;
                        if (data == null) return;
                        OutputStream out = client.getOutputStream();
                        String response = "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + data.length + "\r\n\r\n";
                        out.write(response.getBytes("US-ASCII")); out.write(data); out.flush();
                        CrashDiagnostics.stage(context, "ACCOUNT_PREFETCH_CONSUMED");
                        return;
                    }
                    client.getOutputStream().write("HTTP/1.1 404 Not Found\r\nConnection: close\r\nContent-Length: 0\r\n\r\n".getBytes("US-ASCII"));
                } catch (java.net.SocketTimeoutException retry) {
                    // Deadline, rather than a blocking accept, bounds memory lifetime.
                } catch (IOException | RuntimeException badRequest) {
                    if (closed) return;
                }
            }
            CrashDiagnostics.stage(context, "ACCOUNT_PREFETCH_EXPIRED");
        } finally { close(); }
    }
}
