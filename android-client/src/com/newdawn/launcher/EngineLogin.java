package com.newdawn.launcher;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import java.io.File;
import java.lang.ref.WeakReference;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** HTTP transport for the modal rendered by Unity. Never starts a game Activity. */
public final class EngineLogin {
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();
    private static WeakReference<Activity> activity = new WeakReference<>(null);
    private static SharedPreferences preferences;
    private static boolean busy;
    private static String resultToken, notice = "";
    private static String remembered = "";
    private static int generation;

    private EngineLogin() { }

    public static synchronized boolean prepareGame(Activity game) {
        activity = new WeakReference<>(game);
        preferences = game.getSharedPreferences("durango-br-original-auth-v1", Activity.MODE_PRIVATE);
        remembered = preferences.getString("username", "");
        generation++; busy = false; resultToken = null; notice = "";
        CrashDiagnostics.stage(game, "GAME_PREPARING");
        NativeRuntime.activity(game);
        // The unchanged legacy initializer requires a 43-character buffer. This
        // bootstrap value is cleared by nativeInstall BEFORE UnityPlayer exists;
        // it is never a credential and is never sent to the gateway.
        if (!NativeRuntime.initialize("0000000000000000000000000000000000000000000",
                CrashDiagnostics.directory(game).getAbsolutePath(),
                CrashDiagnostics.compatible(game), CrashDiagnostics.disableTitleVideo(game))) return false;
        System.loadLibrary("enginelogin");
        boolean ready = nativeInstall();
        CrashDiagnostics.stage(game, ready ? "ENGINE_LOGIN_READY" : "ENGINE_LOGIN_INSTALL_FAILED");
        return ready;
    }

    public static synchronized String rememberedUsername() { return remembered; }
    public static synchronized boolean hasSavedSession() { return preferences != null && !preferences.getString("session", "").isEmpty(); }
    public static synchronized boolean isBusy() { return busy; }
    public static synchronized String message() { return notice; }
    public static synchronized String takeToken() { String token = resultToken; resultToken = null; return token; }

    public static void submit(final String username, final String password, final String confirmation, final boolean register) {
        final int request;
        synchronized (EngineLogin.class) {
            if (busy || resultToken != null) return;
            if (preferences == null) return;
            if (username == null || username.trim().isEmpty() || password == null || password.isEmpty()) {
                notice = "Preencha o usuário e a senha."; return;
            }
            if (register && !password.equals(confirmation)) { notice = "As senhas não coincidem."; return; }
            request = generation;
            busy = true; notice = register ? "Criando a conta…" : "Entrando na conta…";
        }
        WORKER.execute(() -> {
            try { accepted(NewDawnApi.authenticate(username, password, register), request); }
            catch (Exception failure) { rejected(failure, false, request); }
        });
    }

    public static void continueSaved() {
        final String saved;
        final int request;
        synchronized (EngineLogin.class) {
            if (busy || resultToken != null || preferences == null) return;
            saved = preferences.getString("session", "");
            if (saved.isEmpty()) { notice = "Entre na sua conta para continuar."; return; }
            request = generation;
            busy = true; notice = "Validando a sessão…";
        }
        WORKER.execute(() -> {
            try { accepted(NewDawnApi.restore(saved), request); }
            catch (Exception failure) { rejected(failure, true, request); }
        });
    }

    private static synchronized void accepted(NewDawnApi.Session session, int request) {
        if (request != generation) return;
        preferences.edit().putString("username", session.email).putString("session", session.refreshToken).apply();
        remembered = session.email;
        resultToken = session.accessToken;
        busy = false; notice = "Carregando os personagens…";
        Activity game = activity.get();
        if (game != null) CrashDiagnostics.stage(game, "ENGINE_AUTH_ACCEPTED");
    }

    private static synchronized void rejected(Exception failure, boolean restoring, int request) {
        if (request != generation) return;
        busy = false; notice = "Não foi possível conectar. Tente novamente.";
        if (failure instanceof NewDawnApi.ApiException) {
            NewDawnApi.ApiException error = (NewDawnApi.ApiException)failure;
            if (restoring && error.sessionLost) {
                preferences.edit().remove("session").apply();
                notice = "Sua sessão expirou. Entre novamente.";
            } else if (error.messageId == 49) notice = "Usuário ou senha incorretos.";
            else if (error.messageId == 48) notice = "Esse usuário já existe.";
            else if (error.messageId == 23) notice = "Use um usuário de 3 a 32 letras, números, ponto, hífen ou sublinhado.";
            else if (error.messageId == 24) notice = "A senha deve ter pelo menos 8 caracteres.";
            else if (error.messageId == 25) notice = "Muitas tentativas. Aguarde um pouco.";
        }
        Activity game = activity.get();
        if (game != null) CrashDiagnostics.stage(game, "ENGINE_AUTH_REJECTED");
    }

    public static synchronized void forget() {
        if (busy) return;
        generation++;
        preferences.edit().remove("session").apply();
        resultToken = null; notice = "";
    }

    public static synchronized void accountFailed() {
        busy = false; resultToken = null;
        notice = "Não foi possível carregar os personagens. Tente entrar novamente.";
    }

    public static void startFailure(Activity game) {
        if (game == null) game = activity.get();
        if (game == null) return;
        activity = new WeakReference<>(game);
        final Activity owner = game;
        UI.post(() -> {
            if (owner.isFinishing() || owner.isDestroyed()) return;
            new AlertDialog.Builder(owner).setTitle("Falha ao iniciar o jogo")
                .setMessage("O motor não conseguiu preparar a tela de login. Compartilhe o diagnóstico para investigarmos.")
                .setPositiveButton("Diagnóstico", (d, w) -> diagnostics())
                .setNegativeButton("Sair", (d, w) -> owner.finish()).setCancelable(false).show();
        });
    }

    public static void diagnostics() {
        UI.post(() -> {
            Activity game = activity.get();
            if (game == null || game.isFinishing() || game.isDestroyed()) return;
            new AlertDialog.Builder(game).setTitle("Diagnóstico")
                .setItems(new String[]{"Compartilhar relatório", "Modo de compatibilidade"}, (dialog, which) -> {
                    if (which == 0) {
                        File report = CrashDiagnostics.latest(game);
                        Uri uri = Uri.parse("content://" + game.getPackageName() + ".lh.reports/" + report.getName());
                        Intent share = new Intent(Intent.ACTION_SEND).setType("application/zip");
                        share.putExtra(Intent.EXTRA_STREAM, uri);
                        share.setClipData(ClipData.newRawUri("Diagnóstico", uri));
                        share.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                        try { game.startActivity(Intent.createChooser(share, "Compartilhar relatório")); }
                        catch (RuntimeException failure) { new AlertDialog.Builder(game).setMessage("Nenhum aplicativo disponível para compartilhar.").setPositiveButton("Fechar", null).show(); }
                    } else {
                        boolean[] options = {CrashDiagnostics.compatible(game), CrashDiagnostics.disableTitleVideo(game)};
                        new AlertDialog.Builder(game).setTitle("Modo de compatibilidade")
                            .setMultiChoiceItems(new String[]{"Gráficos reduzidos", "Desativar vídeo da seleção"}, options, (d, i, selected) -> options[i] = selected)
                            .setPositiveButton("Salvar", (d, w) -> {
                                CrashDiagnostics.compatible(game, options[0]); CrashDiagnostics.disableTitleVideo(game, options[1]);
                                new AlertDialog.Builder(game).setMessage("As opções serão aplicadas ao reabrir o jogo.").setPositiveButton("Fechar", null).show();
                            }).setNegativeButton("Cancelar", null).show();
                    }
                }).setNegativeButton("Fechar", null).show();
        });
    }

    public static void quit() { UI.post(() -> { Activity game = activity.get(); if (game != null) game.finish(); }); }
    private static native boolean nativeInstall();
}
