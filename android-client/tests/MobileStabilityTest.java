package com.newdawn.launcher;

import java.io.*;
import java.nio.charset.StandardCharsets;

public final class MobileStabilityTest {
    private static void check(boolean condition) { if (!condition) throw new AssertionError(); }
    public static void main(String[] args) throws Exception {
        String action = "com.google.android.gms.ads.identifier.service.START";
        check(LegacyServicePolicy.blocked(action, "com.google.android.gms", null));
        check(LegacyServicePolicy.blocked(action, null, "com.google.android.gms"));
        check(!LegacyServicePolicy.blocked(action, "other.package", null));
        check(!LegacyServicePolicy.blocked("com.android.vending.billing.InAppBillingService.BIND", "com.android.vending", null));
        check(!LegacyServicePolicy.blocked("com.google.android.gms.games.service.START", "com.google.android.gms", null));
        check(!LegacyServicePolicy.blocked(null, null, null));
        String trace = "----- pid 1 at date -----\nCmd line: other.package\n\"main\" prio=5 tid=1\n"
            + "  at other.Private.frame(Secret.java:1)\n----- pid 2 at date -----\n"
            + "Cmd line: com.nexon.durango.global\nSubject: secret password token\n"
            + "\"main\" prio=5 tid=1 Native\n  | sysTid=2 state=S\n"
            + "  native: #00 pc abc /private/path/libunity.so\n"
            + "  at com.unity3d.player.UnityPlayer.pause(UnityPlayer.java:101)\n"
            + "  at android.app.Activity.performPause(Native method)\n"
            + "  at bad.frame(/private/token.txt)\n"
            + "\"worker\" prio=5 tid=2\n  at secret.Worker.run(Worker.java:1)\n";
        String expected = "com.unity3d.player.UnityPlayer.pause(UnityPlayer.java:101)\n"
            + "android.app.Activity.performPause(Native method)\n";
        check(AnrSummary.summarize(trace).equals(expected));
        check(AnrSummary.read(new ByteArrayInputStream(trace.getBytes(StandardCharsets.UTF_8))).equals(expected));
        check(AnrSummary.summarize("\"main\"\n  at unknown.Method.run(Method.java:1)").isEmpty());
        StringBuilder many = new StringBuilder("Cmd line: com.nexon.durango.global\n\"main\"\n");
        for (int i=0;i<100;i++) many.append("  at app.Method.run(Method.java:1)\n");
        check(AnrSummary.summarize(many.toString()).split("\n").length == 32);
        check(AnrSummary.read(new ByteArrayInputStream(new byte[4*1024*1024+1])).isEmpty());
        System.out.println("PASS: scoped legacy ad-ID service policy; bounded, private main-thread ANR parser.");
    }
}
