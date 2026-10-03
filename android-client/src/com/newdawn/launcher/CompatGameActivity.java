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
