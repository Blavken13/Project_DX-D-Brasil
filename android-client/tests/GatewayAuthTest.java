package com.newdawn.launcher;

import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import org.json.JSONObject;

/** Executes the compiled launcher adapter against an isolated copy of the real gateway. */
public final class GatewayAuthTest {
    private static int checks;
    private static void check(boolean value, String description) {
        if (!value) throw new AssertionError(description);
        checks++;
    }
    private static void failure(int expected, Action action) throws Exception {
        try { action.run(); throw new AssertionError("Expected rejection " + expected); }
        catch (NewDawnApi.ApiException error) { check(error.messageId == expected, "Error code " + expected + ", got " + error.messageId); }
    }
    interface Action { void run() throws Exception; }

    public static void main(String[] args) throws Exception {
        String user = "apk_qa_" + System.currentTimeMillis();
        String password = "QA &+á=senha_123";
        check(NewDawnApi.onlinePlayers(NewDawnApi.DEFAULT_GATEWAY) >= 0, "Public status");
        failure(23, () -> NewDawnApi.authenticate("jogador@email.com", password, false));
        failure(23, () -> NewDawnApi.authenticate("ab", password, false));
        failure(24, () -> NewDawnApi.authenticate(user, "curta", true));
        NewDawnApi.Session session = NewDawnApi.authenticate(user, password, true);
        check(session.accessToken.matches("[A-Za-z0-9_-]{43}"), "Gateway token format");
        check(session.email.equals(user), "Username retained");
        check(!session.userId.isEmpty(), "Gateway owns account identity");
        check(!session.refreshToken.contains(password), "Password not persisted");
        check(!session.expiringSoon(), "Session lifetime");
        failure(48, () -> NewDawnApi.authenticate(user, password, true));
        failure(49, () -> NewDawnApi.authenticate(user, "incorreta_123", false));
        NewDawnApi.Session login = NewDawnApi.authenticate(user, password, false);
        check(login.userId.equals(session.userId), "Registration/login share identity");
        NewDawnApi.Session restored = NewDawnApi.restore(login.refreshToken);
        check(restored.accessToken.equals(login.accessToken), "Saved session restored");
        check(NewDawnApi.createGameSession(NewDawnApi.DEFAULT_GATEWAY, login.accessToken).equals(login.accessToken),
              "Intent receives auth token, not a pre-created game session");
        JSONObject expired = new JSONObject(login.refreshToken);
        expired.put("expires_at", 0);
        failure(27, () -> NewDawnApi.restore(expired.toString()));
        failure(27, () -> NewDawnApi.restore("obsolete-provider-refresh-token"));
        failure(27, () -> NewDawnApi.createGameSession(NewDawnApi.DEFAULT_GATEWAY, "invalid"));
        failure(27, () -> NewDawnApi.createGameSession(NewDawnApi.DEFAULT_GATEWAY, "A".repeat(43)));
        failure(31, () -> NewDawnApi.createGameSession("https://other-server.example", login.accessToken));

        // Unity's original form contains an empty legacy token. The native bridge
        // appends ours last. Verify the real gateway accepts that exact body shape.
        String form = "account_provider=offline&account_id=forged&token=&locale=pt_BR&platform=Android&token=" +
            URLEncoder.encode(login.accessToken, "UTF-8");
        HttpURLConnection connection = (HttpURLConnection) new URL(NewDawnApi.DEFAULT_GATEWAY + "/sessions").openConnection();
        connection.setConnectTimeout(15000); connection.setReadTimeout(15000);
        connection.setRequestMethod("POST"); connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", "application/x-www-form-urlencoded");
        try (OutputStream stream = connection.getOutputStream()) { stream.write(form.getBytes("UTF-8")); }
        check(connection.getResponseCode() == 200, "Native form accepted by real gateway");
        JSONObject gameSession = new JSONObject(new String(connection.getInputStream().readAllBytes(), "UTF-8"));
        check(!gameSession.getString("session_token").isEmpty(), "Unity gets game-session token");
        check(!gameSession.getString("user_id").isEmpty(), "Unity gets player slot");
        connection.disconnect();
        check(NewDawnApi.news().equals("[]"), "No external news provider");
        check(NewDawnApi.normalizeGateway("https://other-server.example") == null, "Gateway pinned");
        NewDawnApi.signOut(login.accessToken);
        System.out.println("PASS: " + checks + " launcher/gateway checks");
    }
}
