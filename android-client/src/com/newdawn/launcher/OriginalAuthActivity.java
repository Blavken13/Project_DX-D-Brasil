package com.newdawn.launcher;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.json.JSONObject;

/** Local login UI. A saved account never opens Unity without a player action. */
public final class OriginalAuthActivity extends Activity {
    private static final String PAGE = "file:///android_asset/durango-br/launcher/web/index.html";
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newFixedThreadPool(2);
    private WebView web;
    private SharedPreferences preferences;
    private NewDawnApi.Session authenticated;
    private boolean busy, opening, destroyed;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        preferences = getSharedPreferences("durango-br-original-auth-v1", MODE_PRIVATE);
        getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);
        web = new WebView(this);
        web.setBackgroundColor(0xff080c09);
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setAllowFileAccessFromFileURLs(false);
        settings.setAllowUniversalAccessFromFileURLs(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setSaveFormData(false);
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, String url) {
                return !PAGE.equals(url);
            }
        });
        web.addJavascriptInterface(new LoginBridge(), "PrimalAndroid");
        setContentView(web);
        immersive();
        web.loadUrl(PAGE);
    }

    private boolean active() { return !destroyed && !isFinishing() && !isDestroyed(); }

    private void script(String value) {
        if (active()) web.evaluateJavascript(value, null);
    }

    private void notice(String value) {
        script("PRIMAL.notice(" + JSONObject.quote(value) + ",false);");
    }

    private void setBusy(boolean value) {
        busy = value;
        script("PRIMAL.busy(" + value + ");");
    }

    private void showAccount(String name) {
        script("PRIMAL.account(" + (name == null ? "null" : JSONObject.quote(name)) + ");");
    }

    private void ready() {
        if (!active()) return;
        script("PRIMAL.version('Lost Horizon · Alfa');PRIMAL.rememberEmail(" +
            JSONObject.quote(preferences.getString("username", "")) + ");");
        String saved = preferences.getString("session", "");
        String name = null;
        if (!saved.isEmpty()) {
            try {
                JSONObject session = new JSONObject(saved);
                if (session.getString("token").matches("[A-Za-z0-9_-]{43}") &&
                    session.getLong("expires_at") > System.currentTimeMillis() + 60000)
                    name = session.getString("username");
            } catch (Exception ignored) { }
            if (name == null) preferences.edit().remove("session").apply();
        }
        // Only present the saved account. Restore/validate it after Continue is tapped.
        showAccount(name);
        setBusy(false);
        worker.execute(() -> {
            int online = NewDawnApi.onlinePlayers(NewDawnApi.DEFAULT_GATEWAY);
            ui.post(() -> script("PRIMAL.status(" + online + ");"));
        });
    }

    private void authenticate(String username, String password, boolean create) {
        if (!active() || busy || opening) return;
        setBusy(true);
        worker.execute(() -> {
            try {
                NewDawnApi.Session session = NewDawnApi.authenticate(username, password, create);
                ui.post(() -> {
                    if (!active()) return;
                    authenticated = session;
                    preferences.edit().putString("username", session.email)
                        .putString("session", session.refreshToken).apply();
                    setBusy(false);
                    script("PRIMAL.notice('',true);");
                    // The UI requests play only after this explicit login/register action.
                    showAccount(session.email);
                });
            } catch (Exception error) {
                ui.post(() -> failed(error, false));
            }
        });
    }

    private void play() {
        if (!active() || busy || opening) return;
        if (authenticated != null && !authenticated.expiringSoon()) {
            launch(authenticated);
            return;
        }
        String saved = preferences.getString("session", "");
        if (saved.isEmpty()) { showAccount(null); notice("Entre na sua conta para continuar."); return; }
        setBusy(true);
        worker.execute(() -> {
            try {
                NewDawnApi.Session session = NewDawnApi.restore(saved);
                ui.post(() -> launch(session));
            } catch (Exception error) {
                ui.post(() -> failed(error, true));
            }
        });
    }

    private void failed(Exception error, boolean restoring) {
        if (!active()) return;
        String message = "Não foi possível conectar. Verifique sua conexão e tente novamente.";
        if (error instanceof NewDawnApi.ApiException) {
            NewDawnApi.ApiException api = (NewDawnApi.ApiException)error;
            if (restoring && api.sessionLost) {
                authenticated = null;
                preferences.edit().remove("session").apply();
                showAccount(null);
                message = "Sua sessão expirou. Entre novamente na sua conta.";
            } else if (api.messageId == 49) message = "Usuário ou senha incorretos.";
            else if (api.messageId == 48) message = "Esse usuário já existe. Entre com sua senha.";
            else if (api.messageId == 23) message = "Use um usuário de 3 a 32 letras, números, ponto, hífen ou sublinhado.";
            else if (api.messageId == 24) message = "A senha deve ter pelo menos 8 caracteres.";
            else if (api.messageId == 25) message = "Muitas tentativas. Aguarde um pouco e tente novamente.";
        }
        setBusy(false);
        notice(message);
    }

    private void signOut() {
        if (!active() || busy || opening) return;
        authenticated = null;
        preferences.edit().remove("session").remove("username").apply();
        showAccount(null);
        script("PRIMAL.notice('',false);");
    }

    private void launch(NewDawnApi.Session session) {
        if (!active() || opening) return;
        Intent game = new Intent();
        game.setClassName(this, "com.unity3d.player.UnityPlayerActivity");
        game.putExtra("newdawn_ticket", session.accessToken);
        game.putExtra("newdawn_origins", NewDawnApi.DEFAULT_GATEWAY);
        script("document.querySelector('#background-video').pause();");
        startActivity(game);
        opening = true;
        finish();
    }

    private void immersive() {
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LAYOUT_STABLE |
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION |
            View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION |
            View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
    }

    @Override protected void onResume() {
        super.onResume();
        immersive();
        if (web != null) {
            web.onResume();
            script("(()=>{const v=document.querySelector('#background-video');if(v){v.muted=true;v.play().catch(()=>{});}})();");
        }
    }

    @Override protected void onPause() {
        if (web != null) {
            script("(()=>{const v=document.querySelector('#background-video');if(v)v.pause();})();");
            web.onPause();
        }
        super.onPause();
    }

    @Override protected void onDestroy() {
        destroyed = true;
        worker.shutdownNow();
        ui.removeCallbacksAndMessages(null);
        if (web != null) {
            web.removeJavascriptInterface("PrimalAndroid");
            web.stopLoading();
            web.destroy();
            web = null;
        }
        super.onDestroy();
    }

    private final class LoginBridge {
        @JavascriptInterface public void ready() { ui.post(() -> OriginalAuthActivity.this.ready()); }
        @JavascriptInterface public void signIn(String name, String password) {
            ui.post(() -> authenticate(name, password, false));
        }
        @JavascriptInterface public void signUp(String name, String password) {
            ui.post(() -> authenticate(name, password, true));
        }
        @JavascriptInterface public void signOut() { ui.post(() -> OriginalAuthActivity.this.signOut()); }
        @JavascriptInterface public void play() { ui.post(() -> OriginalAuthActivity.this.play()); }
        @JavascriptInterface public void update() { }
    }
}
