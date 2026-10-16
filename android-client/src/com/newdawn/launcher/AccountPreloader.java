package com.newdawn.launcher;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.util.HashSet;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

/** Download and validate the account before constructing Unity. No disk cache. */
public final class AccountPreloader {
    private static final int LIMIT = 1024 * 1024;
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private static volatile AccountSnapshot snapshot;
    private final OriginalAuthActivity activity;
    private final NewDawnApi.Session session;
    private final LinearLayout panel;
    private final ProgressBar bar;
    private final TextView label;

    private AccountPreloader(OriginalAuthActivity activity, NewDawnApi.Session session) {
        this.activity = activity; this.session = session;
        panel = new LinearLayout(activity);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setBackgroundColor(0xe6222925);
        int padding = Math.round(18 * activity.getResources().getDisplayMetrics().density);
        panel.setPadding(padding, padding, padding, padding);
        label = new TextView(activity); label.setTextColor(Color.WHITE); label.setTextSize(16);
        label.setText("Carregando os dados da conta…");
        panel.addView(label, new LinearLayout.LayoutParams(-1, -2));
        bar = new ProgressBar(activity, null, android.R.attr.progressBarStyleHorizontal);
        bar.setMax(100); bar.setIndeterminate(true);
        panel.addView(bar, new LinearLayout.LayoutParams(-1, -2));
        FrameLayout.LayoutParams position = new FrameLayout.LayoutParams(-1, -2, Gravity.BOTTOM);
        activity.addContentView(panel, position);
    }

    public static void start(OriginalAuthActivity activity, NewDawnApi.Session session) {
        AccountPreloader loader = new AccountPreloader(activity, session);
        CrashDiagnostics.stage(activity, "ACCOUNT_PREFETCH_START");
        // Let Android draw the loading panel before starting network work.
        panelPost(loader);
    }

    private static void panelPost(final AccountPreloader loader) {
        loader.panel.postOnAnimation(new Runnable() {
            @Override public void run() {
                new Thread(new Runnable() {
                    @Override public void run() { loader.load(); }
                }, "LH-account-prefetch").start();
            }
        });
    }

    // Decode locally; the loading panel owns the single online validation.
    static NewDawnApi.Session restore(String saved) throws IOException {
        try {
            JSONObject json = new JSONObject(saved);
            String token = json.getString("token");
            long expires = json.getLong("expires_at");
            if (!token.matches("[A-Za-z0-9_-]{43}") || expires <= System.currentTimeMillis() + 60000)
                throw new NewDawnApi.ApiException(27, true);
            return new NewDawnApi.Session(token, saved, json.getString("account_id"), json.getString("username"), expires);
        } catch (JSONException failure) { throw new NewDawnApi.ApiException(27, true); }
    }

    private boolean active() { return !activity.isFinishing() && !activity.isDestroyed(); }
    private void progress(final int value, final String message) {
        UI.post(new Runnable() {
            @Override public void run() {
                if (!active()) return;
                label.setText(message);
                bar.setIndeterminate(value < 0);
                if (value >= 0) bar.setProgress(value);
            }
        });
    }

    private byte[] download() throws Exception {
        if (session.expiringSoon()) throw new NewDawnApi.ApiException(27, true);
        HttpURLConnection connection = (HttpURLConnection) new URL(NewDawnApi.DEFAULT_GATEWAY + "/accounts").openConnection();
        try {
            connection.setConnectTimeout(15000); connection.setReadTimeout(15000);
            connection.setInstanceFollowRedirects(false);
            connection.setRequestMethod("POST"); connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
            connection.setRequestProperty("Accept", "application/json");
            connection.setRequestProperty("User-Agent", "LostHorizon/AccountPrefetch-1");
            byte[] form = ("token=" + URLEncoder.encode(session.accessToken, "UTF-8")).getBytes("UTF-8");
            connection.setFixedLengthStreamingMode(form.length);
            try (OutputStream out = connection.getOutputStream()) { out.write(form); }
            int status = connection.getResponseCode();
            if (status == 401 || status == 403) throw new NewDawnApi.ApiException(27, true);
            if (status != 200) throw new NewDawnApi.ApiException(29, false);
            int length = connection.getContentLength();
            if (length > LIMIT) throw new IOException("account response too large");
            CrashDiagnostics.stage(activity, "ACCOUNT_PREFETCH_HTTP_200");
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            try (InputStream in = connection.getInputStream()) {
                byte[] chunk = new byte[4096]; int count;
                while ((count = in.read(chunk)) != -1) {
                    if (bytes.size() + count > LIMIT) throw new IOException("account response too large");
                    bytes.write(chunk, 0, count);
                    progress(length > 0 ? Math.min(80, (int)(80L * bytes.size() / length)) : -1,
                        "Baixando os dados dos personagens…");
                }
            }
            if (length >= 0 && bytes.size() != length) throw new IOException("incomplete account response");
            return bytes.toByteArray();
        } finally { connection.disconnect(); }
    }

    static int validate(byte[] bytes, String owner) throws Exception {
        JSONObject account = new JSONObject(new String(bytes, "UTF-8"));
        if (account.has("error") || !owner.equals(account.getString("account_id")))
            throw new IOException("account identity mismatch");
        JSONArray players = account.getJSONArray("players");
        int slots = integer(account, "player_slot_count");
        int max = integer(account, "max_player_slot_count");
        if (players.length() > 64 || slots < 0 || max < slots || max > 64)
            throw new IOException("invalid account slots");
        HashSet<String> identities = new HashSet<String>();
        for (int i = 0; i < players.length(); i++) {
            JSONObject player = players.getJSONObject(i);
            String id = player.getString("player_entity_id");
            if (id.isEmpty() || id.length() > 256 || !identities.add(id))
                throw new IOException("invalid character identity");
            String name = player.getString("player_name");
            if (name.isEmpty() || name.length() > 256 || integer(player, "player_level") < 0)
                throw new IOException("invalid character summary");
            Object disconnected = player.get("disconnected_at");
            if (!(disconnected instanceof Number) || !finite(((Number)disconnected).doubleValue()))
                throw new IOException("invalid character timestamp");
            if (player.has("deletes_at") && !player.isNull("deletes_at")) {
                Object deleted = player.get("deletes_at");
                if (!(deleted instanceof Number) || !finite(((Number)deleted).doubleValue()))
                    throw new IOException("invalid deletion timestamp");
            }
        }
        return players.length(); // Empty accounts must reach character creation normally.
    }

    private static int integer(JSONObject json, String key) throws Exception {
        Object value = json.get(key);
        if (!(value instanceof Integer) && !(value instanceof Long)) throw new IOException("invalid integer field");
        long number = ((Number)value).longValue();
        if (number < Integer.MIN_VALUE || number > Integer.MAX_VALUE) throw new IOException("integer overflow");
        return (int)number;
    }

    private static boolean finite(double value) { return !Double.isNaN(value) && !Double.isInfinite(value); }

    private void load() {
        AccountSnapshot prepared = null;
        try {
            byte[] bytes = download();
            progress(85, "Verificando os personagens da conta…");
            int count = validate(bytes, session.userId);
            CrashDiagnostics.stage(activity, "ACCOUNT_PREFETCH_VALIDATED_" + count);
            prepared = new AccountSnapshot(activity.getApplicationContext(), bytes);
            prepared.start();
            final AccountSnapshot ready = prepared;
            progress(100, "Dados carregados. Iniciando o jogo…");
            UI.post(new Runnable() {
                @Override public void run() {
                    if (!active()) { ready.close(); return; }
                    AccountSnapshot old = snapshot; snapshot = ready;
                    if (old != null) old.close();
                    activity.accountReady(session);
                }
            });
        } catch (final Exception failure) {
            if (prepared != null) prepared.close();
            CrashDiagnostics.stage(activity, "ACCOUNT_PREFETCH_FAILED");
            UI.post(new Runnable() {
                @Override public void run() {
                    if (!active()) return;
                    ((ViewGroup)panel.getParent()).removeView(panel);
                    activity.accountFailed(failure);
                }
            });
        }
    }

    public static boolean install(Activity activity) {
        AccountSnapshot ready = snapshot;
        if (ready == null || !ready.available()) return false;
        try {
            System.loadLibrary("lhaccount");
            boolean installed = nativeInstall(ready.url());
            CrashDiagnostics.stage(activity, installed ? "ACCOUNT_CACHE_BRIDGE_READY" : "ACCOUNT_CACHE_BRIDGE_FAILED");
            return installed;
        } catch (LinkageError | RuntimeException failure) {
            CrashDiagnostics.stage(activity, "ACCOUNT_CACHE_BRIDGE_FAILED");
            return false;
        }
    }
    private static native boolean nativeInstall(String cacheUrl);
}
