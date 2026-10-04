package com.newdawn.launcher;

import android.app.ActivityManager;
import android.app.ApplicationExitInfo;
import android.content.Context;
import android.os.Build;
import android.os.Process;
import android.system.Os;
import android.system.OsConstants;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Comparator;
import java.util.List;

final class CrashDiagnostics {
    static File directory(Context context) { File d=new File(context.getFilesDir(),"diagnostics"); d.mkdirs(); return d; }
    static File reports(Context context) { File d=new File(directory(context),"reports"); d.mkdirs(); return d; }
    static synchronized void stage(Context context, String stage) {
        write(new File(directory(context),"runtime-stage.txt"),System.currentTimeMillis()/1000+" "+Process.myPid()+" "+stage+"\n");
    }
    static synchronized void breadcrumb(Context context, String event) {
        try (FileOutputStream out = new FileOutputStream(new File(directory(context), "runtime-events.txt"), true)) {
            out.write((System.currentTimeMillis()/1000 + " " + event + "\n").getBytes(StandardCharsets.UTF_8));
        } catch (IOException ignored) { }
    }
    static void start(Context context) {
        final Context app=context.getApplicationContext();
        // Capture previous stage before any startup callback overwrites it.
        String stage=read(new File(directory(app),"runtime-stage.txt"));
        String events=read(new File(directory(app),"runtime-events.txt"));
        String java=read(new File(directory(app),"java-failure.txt"));
        String graphics=read(new File(directory(app),"graphics-info.txt"));
        MobileReports.prepare(app,stage,events,java,graphics);
        stage(app,"LOGIN_STARTING");
        write(new File(directory(app),"runtime-events.txt"),"");
        write(new File(directory(app),"java-failure.txt"),"");
        write(new File(directory(app),"graphics-info.txt"),"");
        Thread.UncaughtExceptionHandler prior=Thread.getDefaultUncaughtExceptionHandler();
        Thread.setDefaultUncaughtExceptionHandler((thread,error)->{
            try { failure(app,"JAVA_CRASH",error); } catch(Throwable ignored) { }
            finally {
                if(prior!=null)prior.uncaughtException(thread,error);
                else {Process.killProcess(Process.myPid());System.exit(10);}
            }
        });
    }
    static void failure(Context context,String stage,Throwable error) {
        stage(context,stage);StringBuilder text=new StringBuilder();
        // Throwable messages/causes can contain credentials. Export frames only.
        Throwable current=error;
        for(int depth=0;current!=null&&depth<4;depth++){
            text.append(current.getClass().getName()).append('\n');
            StackTraceElement[] frames=current.getStackTrace();
            for(int i=0;i<Math.min(32,frames.length);i++)text.append(frames[i].toString()).append('\n');
            Throwable next=current.getCause();if(next==current)break;current=next;
        }
        write(new File(directory(context),"java-failure.txt"),text.toString());
    }
    static synchronized void snapshot(Context context) {
        MobileReports.startupFailure(context);
        String value=header(context)+"\nInicialização interrompida com segurança; não é um encerramento confirmado pelo Android.\n"+
            "Última etapa: "+read(new File(directory(context),"runtime-stage.txt"))+
            "\nEtapas nativas:\n"+read(new File(directory(context),"runtime-events.txt"))+
            "\nRegistro Java:\n"+read(new File(directory(context),"java-failure.txt"));
        write(new File(reports(context),"relatorio-"+System.currentTimeMillis()+".txt"),value);prune(context);
    }
    static String header(Context context) {
        String version="desconhecida";try{version=context.getPackageManager().getPackageInfo(context.getPackageName(),0).versionName;}catch(Exception ignored){}
        long page=0;try{page=Os.sysconf(OsConstants._SC_PAGESIZE);}catch(Exception ignored){}
        ActivityManager.MemoryInfo memory=new ActivityManager.MemoryInfo();
        ((ActivityManager)context.getSystemService(Context.ACTIVITY_SERVICE)).getMemoryInfo(memory);
        return "Lost Horizon — diagnóstico\nAPK: "+version+"\nModelo: "+Build.MANUFACTURER+" "+Build.MODEL+
            "\nAndroid: "+Build.VERSION.RELEASE+" (API "+Build.VERSION.SDK_INT+")\nBuild: "+Build.DISPLAY+
            "\nABI: "+Arrays.toString(Build.SUPPORTED_ABIS)+"\nPágina do kernel: "+page+
            " bytes\nRAM total: "+memory.totalMem/1048576+" MiB\nPerfil de compatibilidade: "+compatible(context)+
            "\nVídeo da seleção desativado: "+disableTitleVideo(context)+
            "\nMotor: Unity 2017.4.34f1; libunity/libmain legados, compatibilidade 16 KB dependente do Android.\n";
    }
    static boolean compatible(Context context) {
        return context.getSharedPreferences("lh-compatibility",0).getBoolean("enabled",Build.VERSION.SDK_INT>=35);
    }
    static void compatible(Context context,boolean value) {context.getSharedPreferences("lh-compatibility",0).edit().putBoolean("enabled",value).apply();}
    static boolean disableTitleVideo(Context context) {return context.getSharedPreferences("lh-compatibility",0).getBoolean("disable-title-video",false);}
    static void disableTitleVideo(Context context,boolean value) {context.getSharedPreferences("lh-compatibility",0).edit().putBoolean("disable-title-video",value).apply();}
    static synchronized void collect(Context context,String previous,String events,String java,String graphics) {
        if(previous.isEmpty())return;
        StringBuilder text=new StringBuilder(header(context));text.append("\nÚltima etapa da sessão anterior: ").append(previous);
        long timestamp=System.currentTimeMillis();boolean found=false;
        int priorPid=0;try{priorPid=Integer.parseInt(previous.trim().split(" ")[1]);}catch(Exception ignored){}
        long recorded=context.getSharedPreferences("lh-diagnostics",0).getLong("last-exit",0);
        if(Build.VERSION.SDK_INT>=30) {
            try {
                ActivityManager am=(ActivityManager)context.getSystemService(Context.ACTIVITY_SERVICE);
                List<ApplicationExitInfo> exits=am.getHistoricalProcessExitReasons(context.getPackageName(),priorPid,5);
                for(ApplicationExitInfo exit:exits) {
                    if(exit.getTimestamp()<=recorded || (priorPid!=0&&exit.getPid()!=priorPid))continue;
                    found=true;timestamp=exit.getTimestamp();
                    text.append("Data do encerramento: ").append(new java.util.Date(timestamp)).append("\nMotivo: ")
                        .append(reason(exit.getReason())).append(" (código ").append(exit.getReason())
                        .append(")\nSinal/status: ").append(exit.getStatus()).append("\nRAM amostrada: ").append(exit.getRss()).append(" KiB\n");
                    // Structured collector reads the bounded tombstone once and writes its summary.
                    context.getSharedPreferences("lh-diagnostics",0).edit().putLong("last-exit",timestamp).apply();break;
                }
            }catch(Exception ignored){text.append("Histórico de encerramentos indisponível neste sistema.\n");}
        }
        if(!found)text.append("Motivo não informado pelo Android; não é possível afirmar que houve crash.\n");
        if(!events.isEmpty())text.append("\nEtapas nativas anteriores:\n").append(events);
        if(!graphics.isEmpty())text.append("\nÚltimo dispositivo gráfico observado:\n").append(graphics);
        if(!java.isEmpty())text.append("\nRegistro Java (sem mensagens):\n").append(java);
        write(new File(reports(context),"relatorio-"+timestamp+".txt"),text.toString());prune(context);
    }
    static String reason(int reason) {
        switch(reason){case 3:return "Falta de memória";case 4:return "Exceção Java";case 5:return "Falha nativa";
            case 6:return "Aplicativo sem responder (ANR)";case 1:return "Encerramento solicitado pelo aplicativo";
            case 10:return "Encerramento pelo usuário/sistema";case 2:return "Processo recebeu sinal";
            default:return "Encerramento registrado pelo sistema";}
    }
    static synchronized File latest(Context context) {
        File[] files=reports(context).listFiles((dir,name)->name.matches("relatorio-[0-9]+\\.txt"));
        if(files!=null&&files.length>0){Arrays.sort(files,(a,b)->b.getName().compareTo(a.getName()));return files[0];}
        File f=new File(reports(context),"relatorio-0.txt");
        write(f,header(context)+"\nAinda não existe registro de fechamento anterior.\n");return f;
    }
    private static void prune(Context context) {
        File[] files=reports(context).listFiles((dir,name)->name.matches("relatorio-[0-9]+\\.txt"));
        if(files==null)return;Arrays.sort(files,(a,b)->b.getName().compareTo(a.getName()));
        for(int i=5;i<files.length;i++)files[i].delete();
    }
    static String read(File file) {
        try(FileInputStream in=new FileInputStream(file)) {
            ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] b=new byte[4096];int n;
            while((n=in.read(b))!=-1&&out.size()<65536)out.write(b,0,Math.min(n,65536-out.size()));
            return new String(out.toByteArray(),StandardCharsets.UTF_8);
        }catch(IOException ignored){return "";}
    }
    static void write(File file,String value) {
        try(FileOutputStream out=new FileOutputStream(file)){out.write(value.getBytes(StandardCharsets.UTF_8));}
        catch(IOException ignored){}
    }
}
