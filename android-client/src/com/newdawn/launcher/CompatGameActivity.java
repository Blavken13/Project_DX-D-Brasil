package com.newdawn.launcher;

import android.content.Intent;
import android.app.Activity;
import android.content.res.Configuration;
import android.os.Bundle;
import android.view.KeyEvent;
import android.view.MotionEvent;
import com.unity3d.player.UnityPlayer;

/** All code patches are installed synchronously before the UnityPlayer exists. */
public final class CompatGameActivity extends Activity {
    private UnityPlayer player;
    private static String serviceName(String name) {
        // Intent extras and arbitrary strings must never enter diagnostics.
        return name != null && name.length() <= 160 && name.matches("[A-Za-z0-9_.$/]+") ? name : "unknown";
    }
    @Override public boolean bindService(Intent intent, android.content.ServiceConnection connection, int flags) {
        android.util.Log.i("LHService", "bind action=" + serviceName(intent == null ? null : intent.getAction())
            + " package=" + serviceName(intent == null ? null : intent.getPackage())
            + " component=" + serviceName(intent == null || intent.getComponent() == null ? null : intent.getComponent().flattenToShortString())
            + " connection=" + serviceName(connection == null ? null : connection.getClass().getName()));
        if (intent != null && LegacyServicePolicy.blocked(intent.getAction(), intent.getPackage(),
                intent.getComponent() == null ? null : intent.getComponent().getPackageName())) {
            // Return the documented unavailable-service result before Android can
            // dispatch the old native ServiceConnection proxy on its main thread.
            CrashDiagnostics.breadcrumb(this, "LEGACY_AD_ID_SKIPPED");
            android.util.Log.i("LHService", "legacy advertising ID service skipped");
            return false;
        }
        CrashDiagnostics.breadcrumb(this, "SERVICE_BIND_BEGIN");
        boolean bound = super.bindService(intent, connection, flags);
        CrashDiagnostics.breadcrumb(this, bound ? "SERVICE_BIND_ACCEPTED" : "SERVICE_BIND_UNAVAILABLE");
        android.util.Log.i("LHService", "bind accepted=" + bound);
        return bound;
    }
    @Override public void onCreate(Bundle state) {
        requestWindowFeature(1);
        super.onCreate(state);
        MobileReports.stopForGame();
        NativeRuntime.activity(this);
        CrashDiagnostics.stage(this, "GAME_PREPARING");
        try {
            if (!NativeRuntime.initialize(getIntent().getStringExtra("newdawn_ticket"),
                    CrashDiagnostics.directory(this).getAbsolutePath(),
                    getIntent().getBooleanExtra("lh_compatibility", true),
                    getIntent().getBooleanExtra("lh_disable_title_video", false))) {
                failed(); return;
            }
        } catch (LinkageError | RuntimeException error) {
            CrashDiagnostics.failure(this, "NATIVE_START_FAILED", error);
            failed(); return;
        }
        CrashDiagnostics.stage(this, "UNITY_STARTING");
        player = new UnityPlayer(this);
        setContentView(player);
        player.requestFocus();
    }
    private void failed() {
        CrashDiagnostics.snapshot(this);
        Intent login = new Intent(this, OriginalAuthActivity.class);
        login.putExtra("lh_start_error", true);
        startActivity(login);
        finish();
    }
    @Override protected void onDestroy() {
        if (player != null) player.quit();
        super.onDestroy();
    }
    @Override protected void onPause(){super.onPause();if(player!=null)player.pause();}
    @Override protected void onResume(){super.onResume();if(player!=null)player.resume();}
    @Override protected void onStart(){super.onStart();if(player!=null)player.start();}
    @Override protected void onStop(){super.onStop();if(player!=null)player.stop();}
    @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);setIntent(intent);}
    @Override public void onLowMemory(){super.onLowMemory();if(player!=null)player.lowMemory();}
    @Override public void onTrimMemory(int level){super.onTrimMemory(level);if(level==15&&player!=null)player.lowMemory();}
    @Override public void onConfigurationChanged(Configuration config){super.onConfigurationChanged(config);if(player!=null)player.configurationChanged(config);}
    @Override public void onWindowFocusChanged(boolean focused){super.onWindowFocusChanged(focused);if(player!=null)player.windowFocusChanged(focused);}
    @Override public boolean dispatchKeyEvent(KeyEvent event){return event.getAction()==KeyEvent.ACTION_MULTIPLE&&player!=null?player.injectEvent(event):super.dispatchKeyEvent(event);}
    @Override public boolean onKeyDown(int key,KeyEvent event){return player!=null?player.injectEvent(event):super.onKeyDown(key,event);}
    @Override public boolean onKeyUp(int key,KeyEvent event){return player!=null?player.injectEvent(event):super.onKeyUp(key,event);}
    @Override public boolean onTouchEvent(MotionEvent event){return player!=null?player.injectEvent(event):super.onTouchEvent(event);}
    @Override public boolean onGenericMotionEvent(MotionEvent event){return player!=null?player.injectEvent(event):super.onGenericMotionEvent(event);}
}
