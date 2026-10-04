package com.newdawn.launcher;

import java.io.*;
import java.nio.charset.StandardCharsets;

/** Only Java frames from the main thread; never export raw ANR trace text. */
final class AnrSummary {
    static String read(InputStream input) throws IOException {
        ByteArrayOutputStream data = new ByteArrayOutputStream();
        byte[] buffer = new byte[8192]; int count;
        while ((count = input.read(buffer)) != -1) {
            if (data.size() + count > 4 * 1024 * 1024) return "";
            data.write(buffer, 0, count);
        }
        return summarize(new String(data.toByteArray(), StandardCharsets.UTF_8));
    }
    static String summarize(String trace) {
        StringBuilder result = new StringBuilder(); boolean main = false, app = false; int frames = 0;
        for (String line : trace.split("\\r?\\n")) {
            if (line.startsWith("----- pid ")) { if (main) break; app = false; }
            if (line.startsWith("Cmd line: ")) app = line.equals("Cmd line: com.nexon.durango.global");
            if (line.startsWith("\"")) {
                if (main) break;
                main = app && line.startsWith("\"main\"");
            }
            if (!main || frames >= 32) continue;
            String frame = line.trim();
            if (!frame.startsWith("at ")) continue;
            frame = frame.substring(3);
            if (frame.length() <= 256 && frame.matches("[A-Za-z0-9_.$<>]+\\([A-Za-z0-9_.$ :+\\-]*\\)")) {
                result.append(frame).append('\n'); frames++;
            }
        }
        return result.toString();
    }
}
