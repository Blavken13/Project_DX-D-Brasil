package com.newdawn.launcher;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
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
import android.webkit.RenderProcessGoneDetail;
import android.widget.LinearLayout;
import android.widget.EditText;
import android.widget.Button;
import android.widget.TextView;
import android.widget.ScrollView;
import android.view.Gravity;
import java.io.File;
import java.io.FileInputStream;
import java.io.OutputStream;
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
    private boolean releasingVideo, nativeRegister;
    private TextView nativeNotice;
    private File savingReport;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        preferences = getSharedPreferences("durango-br-original-auth-v1", MODE_PRIVATE);
        getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);
        CrashDiagnostics.stage(this,"LOGIN_VISIBLE");
        try { initializeWeb(); }
        catch (RuntimeException | LinkageError error) {
            CrashDiagnostics.failure(this,"WEBVIEW_START_FAILED",error);
            releaseWeb(); nativeLogin(null); ready();
        }
        if(getIntent().getBooleanExtra("lh_start_error",false))
            ui.postDelayed(()->notice("O jogo não conseguiu iniciar neste aparelho. Abra Diagnóstico para compartilhar o relatório."),500);
    }

    private void initializeWeb() {
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
        web.setWebViewClient(Build.VERSION.SDK_INT>=26?new RecoveryClient():new LoginClient());
        web.addJavascriptInterface(new LoginBridge(), "PrimalAndroid");
        setContentView(web);
        immersive();
        web.loadUrl(PAGE);
    }
    private static class LoginClient extends WebViewClient {
        @Override public boolean shouldOverrideUrlLoading(WebView view,String url){return !PAGE.equals(url);}
    }
    private final class RecoveryClient extends LoginClient {
        @Override public boolean onRenderProcessGone(WebView view,RenderProcessGoneDetail detail) {
            CrashDiagnostics.stage(OriginalAuthActivity.this,"WEBVIEW_RENDERER_GONE");
            if(web==view){releaseWeb();if(!opening){nativeLogin(null);ready();}}
            return true;
        }
    }

    private boolean active() { return !destroyed && !isFinishing() && !isDestroyed(); }

    private void script(String value) {
        if (active() && web!=null) web.evaluateJavascript(value, null);
    }

    private void notice(String value) {
        if(web==null&&nativeNotice!=null){nativeNotice.setText(value);return;}
        script("PRIMAL.notice(" + JSONObject.quote(value) + ",false);");
    }

    private void setBusy(boolean value) {
        busy = value;
        script("PRIMAL.busy(" + value + ");");
    }

    private void showAccount(String name) {
        if(web==null){nativeLogin(name);return;}
        script("PRIMAL.account(" + (name == null ? "null" : JSONObject.quote(name)) + ");");
    }

    private void ready() {
        if (!active()) return;
        script("PRIMAL.version('Lost Horizon · Alfa 50209');PRIMAL.rememberEmail(" +
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
                    if(web==null)launch(session);
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
        opening=true;setBusy(true);
        CrashDiagnostics.stage(this,"LOGIN_VIDEO_RELEASING");
        if(web==null){openGame(session);return;}
        releasingVideo=true;
        // Detach the media source, then destroy WebView before creating Unity.
        // JS completion is a lifecycle handshake, not a fixed startup sleep.
        web.evaluateJavascript("(()=>{const v=document.querySelector('#background-video');if(v){v.pause();v.removeAttribute('src');v.querySelectorAll('source').forEach(s=>s.remove());v.load();}return true;})();",
            result -> {if(releasingVideo){releasingVideo=false;releaseWeb();openGame(session);}});
        ui.postDelayed(()->{if(releasingVideo){releasingVideo=false;releaseWeb();openGame(session);}},2000);
    }
    private void openGame(NewDawnApi.Session session) {
        if(!active())return;
        Intent game = new Intent();
        game.setClassName(this, "com.newdawn.launcher.CompatGameActivity");
        game.putExtra("newdawn_ticket", session.accessToken);
        game.putExtra("newdawn_origins", NewDawnApi.DEFAULT_GATEWAY);
        game.putExtra("lh_compatibility",CrashDiagnostics.compatible(this));
        game.putExtra("lh_disable_title_video",CrashDiagnostics.disableTitleVideo(this));
        startActivity(game);
        finish();
    }

    private void releaseWeb() {
        if(web==null)return;
        WebView old=web;web=null;
        try {
            if(old.getParent() instanceof android.view.ViewGroup)((android.view.ViewGroup)old.getParent()).removeView(old);
            old.removeJavascriptInterface("PrimalAndroid");old.stopLoading();old.destroy();
        }catch(RuntimeException ignored){}
    }

    /** A working login survives a missing/crashed WebView without a decoder. */
    private void nativeLogin(String account) {
        LinearLayout panel=new LinearLayout(this);panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(32,16,32,16);panel.setGravity(Gravity.CENTER);panel.setBackgroundColor(0xff101b17);
        TextView title=new TextView(this);title.setText("Lost Horizon");title.setTextColor(0xffdfc28b);title.setTextSize(24);panel.addView(title);
        if(account!=null) {
            TextView name=new TextView(this);name.setText(account);name.setTextColor(0xffeeeeee);panel.addView(name);
            Button play=new Button(this);play.setText("Continuar");play.setOnClickListener(v->play());panel.addView(play);
            Button change=new Button(this);change.setText("Trocar de conta");change.setOnClickListener(v->signOut());panel.addView(change);
        }else {
            EditText user=new EditText(this);user.setHint("Usuário");user.setSingleLine(true);user.setInputType(1);panel.addView(user);
            user.setText(preferences.getString("username",""));
            EditText password=new EditText(this);password.setHint("Senha");password.setInputType(129);password.setSingleLine(true);panel.addView(password);
            EditText confirmation=new EditText(this);confirmation.setHint("Confirmar senha");confirmation.setInputType(129);confirmation.setSingleLine(true);
            confirmation.setVisibility(nativeRegister?View.VISIBLE:View.GONE);panel.addView(confirmation);
            Button submit=new Button(this);submit.setText(nativeRegister?"Criar conta":"Entrar");
            submit.setOnClickListener(v->{
                String u=user.getText().toString().trim(),p=password.getText().toString();
                if(!u.matches("[A-Za-z0-9_.-]{3,32}")||p.isEmpty()||p.length()>128){notice("Verifique o usuário e a senha.");return;}
                if(nativeRegister&&(p.length()<8||!p.equals(confirmation.getText().toString()))){notice("Use pelo menos 8 caracteres e confirme a senha.");return;}
                authenticate(u,p,nativeRegister);
            });panel.addView(submit);
            Button change=new Button(this);change.setText(nativeRegister?"Já tenho uma conta":"Criar conta");
            change.setOnClickListener(v->{if(!busy){nativeRegister=!nativeRegister;nativeLogin(null);}});panel.addView(change);
        }
        Button diagnostics=new Button(this);diagnostics.setText("Diagnóstico");diagnostics.setOnClickListener(v->diagnostics());panel.addView(diagnostics);
        nativeNotice=new TextView(this);nativeNotice.setTextColor(0xfff1c39c);panel.addView(nativeNotice);
        ScrollView scroll=new ScrollView(this);scroll.addView(panel);setContentView(scroll);immersive();
    }

    private void diagnostics() {
        new AlertDialog.Builder(this).setTitle("Diagnóstico").setItems(new String[]{"Ver último fechamento","Compartilhar relatório","Salvar relatório","Modo de compatibilidade"},(dialog,index)->{
            if(index==0){File report=CrashDiagnostics.latest(this);TextView text=new TextView(this);text.setText(CrashDiagnostics.read(report));text.setTextIsSelectable(true);text.setPadding(20,12,20,12);
                ScrollView scroll=new ScrollView(this);scroll.addView(text);new AlertDialog.Builder(this).setTitle("Último fechamento").setView(scroll).setPositiveButton("Fechar",null).show();}
            else if(index==1)shareReport();
            else if(index==2){savingReport=CrashDiagnostics.latest(this);Intent save=new Intent(Intent.ACTION_CREATE_DOCUMENT);save.addCategory(Intent.CATEGORY_OPENABLE);save.setType("text/plain");save.putExtra(Intent.EXTRA_TITLE,savingReport.getName());
                try{startActivityForResult(save,61);}catch(RuntimeException error){notice("Nenhum aplicativo disponível para salvar. Use Compartilhar relatório.");}}
            else {boolean[] selected={CrashDiagnostics.compatible(this),CrashDiagnostics.disableTitleVideo(this)};
                new AlertDialog.Builder(this).setTitle("Modo de compatibilidade")
                    .setMultiChoiceItems(new String[]{"Gráficos reduzidos (30 FPS e resolução menor)","Desativar vídeo da seleção (teste de falhas)"},selected,(d,which,checked)->selected[which]=checked)
                    .setPositiveButton("Salvar",(d,w)->{CrashDiagnostics.compatible(this,selected[0]);CrashDiagnostics.disableTitleVideo(this,selected[1]);}).setNegativeButton("Cancelar",null).show();}
        }).setNegativeButton("Fechar",null).show();
    }
    private void shareReport() {
        File report=CrashDiagnostics.latest(this);Uri uri=Uri.parse("content://"+getPackageName()+".lh.reports/"+report.getName());
        Intent share=new Intent(Intent.ACTION_SEND);share.setType("text/plain");share.putExtra(Intent.EXTRA_STREAM,uri);
        share.setClipData(ClipData.newRawUri("Relatório Lost Horizon",uri));share.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try{startActivity(Intent.createChooser(share,"Compartilhar relatório"));}catch(RuntimeException error){notice("Nenhum aplicativo disponível para compartilhar. Use Salvar relatório.");}
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if(request!=61||result!=RESULT_OK||data==null||data.getData()==null||savingReport==null)return;
        final File report=savingReport;savingReport=null;final Uri destination=data.getData();
        worker.execute(()->{
            try(FileInputStream in=new FileInputStream(report);OutputStream out=getContentResolver().openOutputStream(destination)){
                if(out==null)throw new java.io.IOException();byte[] buffer=new byte[4096];int n;
                while((n=in.read(buffer))!=-1)out.write(buffer,0,n);ui.post(()->notice("Relatório salvo."));
            }catch(Exception error){ui.post(()->notice("Não foi possível salvar o relatório."));}
        });
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
        releaseWeb();
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
        @JavascriptInterface public void diagnostics() { ui.post(() -> OriginalAuthActivity.this.diagnostics()); }
    }
}
