package com.newdawn.launcher;
import android.app.Application;

public final class DiagnosticApplication extends Application {
    @Override public void onCreate() {
        super.onCreate();
        CrashDiagnostics.start(this);
    }
}
