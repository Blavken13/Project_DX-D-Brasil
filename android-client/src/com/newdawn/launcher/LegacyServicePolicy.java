package com.newdawn.launcher;

/** The Unity 2017 advertising ID binder is optional for the Brazilian gateway. */
final class LegacyServicePolicy {
    static boolean blocked(String action, String packageName, String componentPackage) {
        return "com.google.android.gms.ads.identifier.service.START".equals(action)
            && ("com.google.android.gms".equals(packageName)
                || "com.google.android.gms".equals(componentPackage));
    }
}
