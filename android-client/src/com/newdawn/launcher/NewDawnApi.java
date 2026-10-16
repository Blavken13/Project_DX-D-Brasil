package com.newdawn.launcher;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import org.json.JSONException;
import org.json.JSONObject;

/** Adapter for the existing launcher UI; identity comes only from the Brazilian gateway. */
final class NewDawnApi {
    static final String DEFAULT_GATEWAY = "http://179.197.72.129:8190";
    static final String DEFAULT_ORIGINS = DEFAULT_GATEWAY;
    static final String GAME_ACTIVITY = "com.unity3d.player.UnityPlayerActivity";

    static final class Session {
        final String accessToken, refreshToken, userId, email;
        final long expiresAtMillis;
        Session(String token, String saved, String id, String username, long expires) {
            accessToken = token; refreshToken = saved; userId = id;
            email = username; expiresAtMillis = expires;
        }
        boolean expiringSoon() { return System.currentTimeMillis() >= expiresAtMillis - 60000; }
    }

    static final class ApiException extends IOException {
        final int messageId;
        final boolean sessionLost;
        ApiException(int messageId, boolean sessionLost) {
            super("gateway:" + messageId);
            this.messageId = messageId; this.sessionLost = sessionLost;
        }
    }

    private static final class Response {
        final int status;
        final String body;
        Response(int status, String body) { this.status = status; this.body = body; }
    }

    static Session authenticate(String username, String password, boolean register) throws IOException {
        username = username == null ? "" : username.trim();
        if (!username.matches("[A-Za-z0-9_.-]{3,32}") || password == null || password.length() > 128)
            throw new ApiException(23, false);
        if (register && password.length() < 8) throw new ApiException(24, false);
        String form = "username=" + encode(username) + "&password=" + encode(password);
        if (register) {
            Response created = request("/auth/register", form + "&password_confirm=" + encode(password));
            requireOk(created, false);
        }
        Response response = request("/auth/login", form);
        requireOk(response, false);
        try {
            JSONObject json = new JSONObject(response.body);
            String token = json.getString("auth_token");
            String id = json.getString("account_id");
            String name = json.getString("username");
            long expires = System.currentTimeMillis() + json.getLong("expires_in") * 1000L;
            if (!validToken(token) || id.isEmpty() || expires <= System.currentTimeMillis())
                throw new ApiException(30, false);
            JSONObject saved = new JSONObject();
            saved.put("token", token); saved.put("account_id", id);
            saved.put("username", name); saved.put("expires_at", expires);
            return new Session(token, saved.toString(), id, name, expires);
        } catch (JSONException e) { throw new ApiException(30, false); }
    }

    static Session restore(String saved) throws IOException {
        try {
            JSONObject json = new JSONObject(saved);
            String token = json.getString("token");
            long expires = json.getLong("expires_at");
            if (!validToken(token) || expires <= System.currentTimeMillis() + 60000)
                throw new ApiException(27, true);
            validateAccount(token);
            return new Session(token, saved, json.getString("account_id"), json.getString("username"), expires);
        } catch (JSONException e) { throw new ApiException(27, true); }
    }

    static String createGameSession(String gateway, String token) throws IOException {
        if (normalizeGateway(gateway) == null) throw new ApiException(31, false);
        if (!validToken(token)) throw new ApiException(27, true);
        // The native request bridge adds this token to /accounts and /sessions.
        // Do not pre-create a game session here: Unity must receive its own user_id/session_token.
        validateAccount(token);
        return token;
    }

    private static void validateAccount(String token) throws IOException {
        Response response = request("/accounts", "token=" + encode(token));
        if (response.status == 401 || response.status == 403) throw new ApiException(27, true);
        if (response.status < 200 || response.status >= 300) throw new ApiException(29, false);
        try {
            JSONObject json = new JSONObject(response.body);
            if (json.has("error") || (json.has("ok") && !json.optBoolean("ok")))
                throw new ApiException(27, true);
        } catch (JSONException e) { throw new ApiException(30, false); }
    }

    static String normalizeGateway(String value) {
        return value != null && DEFAULT_GATEWAY.equals(value.trim().replaceAll("/+$", ""))
            ? DEFAULT_GATEWAY : null;
    }

    static int onlinePlayers(String gateway) {
        if (normalizeGateway(gateway) == null) return -1;
        try {
            Response response = request("/status", null);
            JSONObject json = new JSONObject(response.body);
            return response.status == 200 && json.optBoolean("ok") ? json.optInt("online", 0) : -1;
        } catch (Exception e) { return -1; }
    }

    // The community service has its own news, crash reporting and updater. This build
    // never contacts it. The existing news panel displays a local welcome message.
    static String news() { return "[]"; }
    static void signOut(String ignored) { }
    static JSONObject sendCrashReport(String gateway, String token, String form) throws IOException {
        throw new ApiException(38, false);
    }

    private static boolean validToken(String value) {
        return value != null && value.matches("[A-Za-z0-9_-]{43}");
    }

    private static String encode(String value) throws IOException {
        return URLEncoder.encode(value, "UTF-8");
    }

    private static void requireOk(Response response, boolean sessionLost) throws IOException {
        // Some HttpURLConnection implementations discard the body of a 401 when
        // streaming a POST. The login route's status already identifies this error.
        if (response.status == 401) throw new ApiException(49, sessionLost);
        if (response.status == 429) throw new ApiException(25, sessionLost);
        try {
            JSONObject json = new JSONObject(response.body);
            if (response.status >= 200 && response.status < 300 && json.optBoolean("ok")) return;
            String error = json.optString("error", "");
            int message = response.status == 429 ? 25 :
                error.equals("username_taken") ? 48 :
                error.equals("password_too_short") ? 24 :
                error.equals("invalid_credentials") ? 49 :
                error.startsWith("username_") ? 23 : 32;
            throw new ApiException(message, sessionLost);
        } catch (JSONException e) { throw new ApiException(32, sessionLost); }
    }

    private static Response request(String path, String form) throws IOException {
        HttpURLConnection connection = (HttpURLConnection) new URL(DEFAULT_GATEWAY + path).openConnection();
        try {
            connection.setConnectTimeout(15000); connection.setReadTimeout(15000);
            connection.setInstanceFollowRedirects(false);
            connection.setRequestProperty("Accept", "application/json");
            connection.setRequestProperty("User-Agent", "DurangoBrasil/Android alpha-1");
            if (form != null) {
                byte[] body = form.getBytes("UTF-8");
                connection.setRequestMethod("POST"); connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
                connection.setFixedLengthStreamingMode(body.length);
                try (OutputStream output = connection.getOutputStream()) { output.write(body); }
            }
            int status = connection.getResponseCode();
            InputStream stream = status < 400 ? connection.getInputStream() : connection.getErrorStream();
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            if (stream != null) try (InputStream input = stream) {
                byte[] buffer = new byte[4096]; int count;
                while ((count = input.read(buffer)) != -1) {
                    if (bytes.size() + count > 1024 * 1024) throw new IOException("Resposta muito grande");
                    bytes.write(buffer, 0, count);
                }
            }
            return new Response(status, bytes.toString("UTF-8"));
        } finally { connection.disconnect(); }
    }
}
