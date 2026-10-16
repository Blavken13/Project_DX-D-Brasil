package com.newdawn.launcher;

/** No native library is loaded on the login screen. Called before UnityPlayer. */
final class NativeRuntime {
    private static boolean loaded;
    private static java.lang.ref.WeakReference<android.app.Activity> game = new java.lang.ref.WeakReference<>(null);
    private static final java.util.concurrent.atomic.AtomicBoolean notified = new java.util.concurrent.atomic.AtomicBoolean();
    static void activity(android.app.Activity activity) { game = new java.lang.ref.WeakReference<>(activity); }
    private static void onFailure() {
        if(!notified.compareAndSet(false,true))return;
        android.app.Activity activity=game.get();if(activity==null)return;
        new android.os.Handler(android.os.Looper.getMainLooper()).post(()->{
            if(activity.isFinishing()||activity.isDestroyed())return;
            CrashDiagnostics.snapshot(activity);
            new android.app.AlertDialog.Builder(activity).setTitle("Falha de compatibilidade")
                .setMessage("Não foi possível preparar a conexão do jogo. O relatório foi salvo. Feche e reabra o aplicativo e use Diagnóstico para compartilhá-lo.")
                .setPositiveButton("Fechar jogo",(d,w)->activity.finish()).setCancelable(false).show();
        });
    }
    static synchronized boolean initialize(String token, String directory, boolean compatible, boolean disableVideo) {
        if (!loaded) {
            // Preserve the original loader/JNI foundation before dlopen of the
            // game library; this initializes the class, not the UnityPlayer.
            try { Class.forName("com.unity3d.player.UnityPlayer"); }
            catch (ClassNotFoundException error) { throw new IllegalStateException(error); }
            System.loadLibrary("nd");
            System.loadLibrary("br");
            loaded = true;
        }
        return prepare(token, directory, compatible, disableVideo);
    }
    private static native boolean prepare(String token, String directory, boolean compatible, boolean disableVideo);
}
