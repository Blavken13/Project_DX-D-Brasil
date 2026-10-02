package com.newdawn.launcher;

import android.app.Activity;
import android.content.Intent;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.WindowManager;
import android.widget.ImageView;
import java.io.IOException;
import java.io.InputStream;

/** Displays the distribution's artwork before starting the unchanged Unity activity. */
public final class BrazilSplashActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable launch = new Runnable() {
        @Override public void run() {
            if (isFinishing() || isDestroyed()) return;
            Intent game = new Intent(BrazilSplashActivity.this.getIntent());
            game.setClassName(BrazilSplashActivity.this, "com.durango.offserver.DurangoActivity");
            startActivity(game);
            finish();
        }
    };

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().getDecorView().setSystemUiVisibility(
            View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION |
            View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN |
            View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        ImageView image = new ImageView(this);
        image.setBackgroundColor(Color.BLACK);
        image.setScaleType(ImageView.ScaleType.FIT_CENTER);
        image.setContentDescription("Vision Force — Servidor Brasileiro");
        try (InputStream stream = getAssets().open("newdawn/launcher/web/vision-force-splash.png")) {
            image.setImageBitmap(BitmapFactory.decodeStream(stream));
        } catch (IOException error) {
            // A packaging check verifies this image; keep the game accessible if an install is damaged.
        }
        setContentView(image);
        handler.postDelayed(launch, 1800);
    }

    @Override protected void onDestroy() {
        handler.removeCallbacks(launch);
        super.onDestroy();
    }
}
