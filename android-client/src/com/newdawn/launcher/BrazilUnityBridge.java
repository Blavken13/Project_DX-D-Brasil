package com.newdawn.launcher;

import android.content.Context;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;

/** Uses the same Brazilian auth token while preserving the Unity 6 game activity. */
public final class BrazilUnityBridge {
    static { System.loadLibrary("durangobr"); }
    private BrazilUnityBridge() { }
    public static native void configure(String token);
    public static native boolean ready();
    private static native void configureShaderPath(String path);

    public static void prepareGame(Context context, String token) {
        if (token == null || !token.matches("[A-Za-z0-9_-]{43}"))
            throw new IllegalArgumentException("Entre novamente na sua conta.");
        configure(token);
        try {
            String config = "gateway=http://179.197.72.129:8190\naccount=DurangoBrasil\nserver=Durango Brasil|http://179.197.72.129:8190\n";
            write(context.getFilesDir(), config);
            write(context.getExternalFilesDir(null), config);
            File external = context.getExternalFilesDir(null);
            if (external != null) new File(external, "fanmod_notice_hide").createNewFile();
            File shader = new File(context.getFilesDir(), "durango-br-shaders.bundle");
            File pending = new File(context.getFilesDir(), "durango-br-shaders.bundle.tmp");
            try (InputStream input = context.getAssets().open("newdawn/compatibility/durango-br-shaders.bundle");
                 FileOutputStream output = new FileOutputStream(pending)) {
                byte[] buffer = new byte[16384];
                int bytes;
                while ((bytes = input.read(buffer)) != -1) output.write(buffer, 0, bytes);
                output.getFD().sync();
            }
            if (!pending.renameTo(shader)) throw new IOException("Falha ao preparar shader");
            configureShaderPath(shader.getAbsolutePath());
        } catch (IOException error) {
            configure(null);
            throw new IllegalStateException("Não foi possível configurar o servidor brasileiro.", error);
        }
    }

    private static void write(File directory, String text) throws IOException {
        if (directory == null) return;
        if (!directory.exists() && !directory.mkdirs()) throw new IOException("Diretório indisponível");
        File pending = new File(directory, "offserver.txt.tmp");
        try (FileOutputStream out = new FileOutputStream(pending)) {
            out.write(text.getBytes("UTF-8"));
            out.getFD().sync();
        }
        if (!pending.renameTo(new File(directory, "offserver.txt"))) throw new IOException("Falha ao salvar configuração");
    }
}
