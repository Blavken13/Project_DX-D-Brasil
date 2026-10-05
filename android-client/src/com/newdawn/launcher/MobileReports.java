package com.newdawn.launcher;

import android.app.ActivityManager;
import android.app.ApplicationExitInfo;
import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Process;
import android.system.Os;
import android.system.OsConstants;
import org.json.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

/** Collect once after restart, upload only in the launcher. Never read logcat or account data. */
final class MobileReports {
    private static final ScheduledExecutorService worker=Executors.newSingleThreadScheduledExecutor(r->{
        Thread t=new Thread(()->{Process.setThreadPriority(Process.THREAD_PRIORITY_BACKGROUND);r.run();},"LH-reports");t.setDaemon(true);return t;});
    private static volatile boolean visible;
    private static volatile HttpURLConnection connection;
    private static boolean gameStarting, running;
    private static ScheduledFuture<?> scheduled;
    private static volatile long generation, nextAttempt;
    private static int attemptsThisSession, failures;
    private static int attemptLimit=3, manualRetries;
    private static Runnable previousCollection;
    private static JSONObject currentMetadata;
    private static JSONObject previousMetadata;
    private static String previousGraphics="";
    private static String sessionId="";
    private static MobileReportQueue queue(Context context) {return new MobileReportQueue(new File(CrashDiagnostics.directory(context),"pending"));}
    static boolean enabled(Context context) {return context.getSharedPreferences("lh-diagnostics",0).getBoolean("automatic-upload",true);}
    static void enabled(Context context,boolean value) {
        context.getSharedPreferences("lh-diagnostics",0).edit().putBoolean("automatic-upload",value).apply();
        if(!value)pause();else loginVisible(context);
    }
    static void prepare(Context context,String stage,String events,String java,String graphics) {
        final Context app=context.getApplicationContext();
        final String previous=CrashDiagnostics.read(new File(CrashDiagnostics.directory(app),"session.json"));
        try{previousMetadata=new JSONObject(previous);}catch(Exception ignored){previousMetadata=null;}
        previousGraphics=graphics;
        sessionId=UUID.randomUUID().toString();
        try{currentMetadata=metadata(app);CrashDiagnostics.write(new File(CrashDiagnostics.directory(app),"session.json"),currentMetadata.toString());}catch(Exception ignored){}
        nextAttempt=Math.min(System.currentTimeMillis()+3600000,app.getSharedPreferences("lh-diagnostics",0).getLong("next-upload",0));
        previousCollection=()->{CrashDiagnostics.collect(app,stage,events,java,graphics);collect(app,previous,stage,events,java,graphics);};
    }
    static synchronized void loginVisible(Context context) {
        visible=true;gameStarting=false;
        final Context app=context.getApplicationContext();
        if(previousCollection!=null){Runnable collect=previousCollection;previousCollection=null;worker.execute(collect);}
        schedule(app,1500);
    }
    static synchronized void pause() {
        visible=false;generation++;
        if(scheduled!=null){scheduled.cancel(false);scheduled=null;}
        HttpURLConnection active=connection;if(active!=null)active.disconnect();
    }
    static synchronized void stopForGame() {gameStarting=true;pause();}
    private static boolean allowed(Context context) {return visible&&!gameStarting&&enabled(context);}
    private static synchronized void schedule(Context context,long delay) {
        if(running||scheduled!=null||!allowed(context)||attemptsThisSession>=attemptLimit)return;
        long ticket=generation;
        scheduled=worker.schedule(()->{
            synchronized(MobileReports.class){scheduled=null;if(ticket!=generation||!allowed(context))return;running=true;}
            try{upload(context,ticket);}
            finally{synchronized(MobileReports.class){running=false;if(allowed(context)&&queue(context).files().length>0&&attemptsThisSession<attemptLimit)schedule(context,Math.max(1500,nextAttempt-System.currentTimeMillis()));}}
        },Math.max(delay,nextAttempt-System.currentTimeMillis()),TimeUnit.MILLISECONDS);
    }
    private static String endpoint(Context context) throws Exception {
        android.os.Bundle meta=context.getPackageManager().getApplicationInfo(context.getPackageName(),PackageManager.GET_META_DATA).metaData;
        String value=meta==null?"":meta.getString("lh.diagnostics.endpoint","");
        if(value.isEmpty())return "";
        URL url=new URL(value);
        if(url.getUserInfo()!=null||url.getQuery()!=null||url.getRef()!=null)throw new IOException();
        // Production requires HTTPS. Clear HTTP is limited to explicit local test builds.
        if(!url.getProtocol().equals("https")&&!(url.getProtocol().equals("http")&&meta.getBoolean("lh.diagnostics.local",false)))throw new IOException();
        return value;
    }
    private static void upload(Context context,long ticket) {
        String sendingId="";
        try {
            String endpoint=endpoint(context);if(endpoint.isEmpty()){attemptsThisSession=attemptLimit;uploadStatus(context,"Serviço de envio não configurado neste APK.");return;}
            File[] files=queue(context).files();if(files.length==0)return;
            File file=files[0];String id=file.getName().replace(".json","");
            sendingId=id;
            if(!allowed(context)||ticket!=generation)return;
            attemptsThisSession++;
            context.getSharedPreferences("lh-diagnostics",0).edit().putLong("last-attempt",System.currentTimeMillis()).apply();
            if(MobileReportTransport.send(endpoint,file,new MobileReportTransport.Gate(){
                public boolean allowed(){return MobileReports.allowed(context)&&ticket==generation;}
                public void connection(HttpURLConnection value)throws IOException{synchronized(MobileReports.class){if(value!=null&&!allowed())throw new IOException();connection=value;}}
            })) {
                queue(context).acknowledge(id);failures=0;nextAttempt=0;
                context.getSharedPreferences("lh-diagnostics",0).edit().putLong("last-upload",System.currentTimeMillis()).putLong("next-upload",0).apply();
                uploadStatus(context,"Recebimento confirmado pelo servidor.");
            }else{
                uploadStatus(context,"Envio interrompido ao sair da tela de login; relatório preservado.");
            }
        }catch(MobileReportTransport.Rejected error){
            uploadStatus(context,"Servidor respondeu HTTP "+error.status+(error.status==400||error.status==409||error.status==413||error.status==415?"; relatório separado para revisão.":"; aguardando nova tentativa."));
            if(error.status==400||error.status==409||error.status==413||error.status==415){queue(context).reject(sendingId);nextAttempt=0;}
            else backoff(context);
        }catch(Exception error){
            if(!allowed(context)||ticket!=generation){uploadStatus(context,"Envio interrompido ao sair da tela de login; relatório preservado.");return;}
            uploadStatus(context,error instanceof java.net.SocketTimeoutException?"Tempo de conexão esgotado. Verifique o acesso ao servidor pela rede Wi-Fi.":
                error instanceof java.net.ConnectException?"Não foi possível conectar ao servidor. Verifique a rede Wi-Fi e o firewall do computador.":
                error instanceof java.net.UnknownHostException?"Endereço do servidor não encontrado.":
                error instanceof javax.net.ssl.SSLException?"Conexão segura não pôde ser estabelecida.":"Envio falhou ("+error.getClass().getSimpleName()+"); relatório preservado.");
            backoff(context);
        }
    }
    private static void uploadStatus(Context context,String message) {context.getSharedPreferences("lh-diagnostics",0).edit().putString("last-upload-result",message).apply();}
    private static void backoff(Context context) {
        failures=Math.min(failures+1,8);nextAttempt=System.currentTimeMillis()+Math.min(3600000,15000L*(1L<<failures));
            context.getSharedPreferences("lh-diagnostics",0).edit().putLong("next-upload",nextAttempt).apply();
    }
    private static JSONObject metadata(Context context) throws Exception {
        JSONObject report=new JSONObject();
        report.put("session_started",System.currentTimeMillis());
        String install=context.getSharedPreferences("lh-diagnostics",0).getString("installation-id","");
        if(install.isEmpty()){install=UUID.randomUUID().toString();context.getSharedPreferences("lh-diagnostics",0).edit().putString("installation-id",install).apply();}
        PackageInfo p=context.getPackageManager().getPackageInfo(context.getPackageName(),0);
        ActivityManager.MemoryInfo m=new ActivityManager.MemoryInfo();((ActivityManager)context.getSystemService(Context.ACTIVITY_SERVICE)).getMemoryInfo(m);
        report.put("installation_id",install).put("session_id",sessionId).put("platform","Android").put("metadata_available",true)
            .put("version_name",bounded(p.versionName,160)).put("version_code",p.versionCode).put("manufacturer",bounded(Build.MANUFACTURER,160))
            .put("model",bounded(Build.MODEL,160)).put("android_api",Build.VERSION.SDK_INT).put("android_version",bounded(Build.VERSION.RELEASE,160))
            .put("android_build",bounded(Build.DISPLAY,160)).put("abi",bounded(Arrays.toString(Build.SUPPORTED_ABIS),160))
            .put("ram_mib",m.totalMem/1048576).put("compatibility",CrashDiagnostics.compatible(context)).put("title_video_disabled",CrashDiagnostics.disableTitleVideo(context));
        try{report.put("page_size",Os.sysconf(OsConstants._SC_PAGESIZE));}catch(Exception ignored){}
        return report;
    }
    private static String bounded(String value,int limit){if(value==null)return "";value=value.replaceAll("[\\p{Cntrl}]","");return value.substring(0,Math.min(limit,value.length()));}
    private static JSONArray lines(String text,int count,int length,String pattern) {
        JSONArray result=new JSONArray();for(String line:text.split("\\r?\\n")){if(result.length()>=count)break;if(pattern==null||line.matches(pattern))result.put(bounded(line,length));}return result;
    }
    private static JSONObject event(Context context,JSONObject meta,String type,long timestamp,String stage,String events,String java,String graphics,String nativeTrace,int reason,int status,long rss) throws Exception {
        JSONObject report=new JSONObject(meta.toString());
        String[] parts=stage.trim().split(" ");String last=parts.length>=3?parts[2]:"UNKNOWN";
        if(!last.matches("[A-Z0-9_]{1,160}"))last="UNKNOWN";
        String exception="";for(String line:java.split("\\r?\\n"))if(line.matches("[A-Za-z0-9_.$]+")){exception=line;break;}
        report.put("event_id",UUID.randomUUID().toString()).put("event_type",type).put("occurred_at",timestamp).put("last_stage",last)
            .put("exit_reason",reason).put("exit_status",Math.max(0,status)).put("rss_kib",rss).put("exception_class",bounded(exception,160))
            .put("java_frames",lines(java,128,256,"[A-Za-z0-9_.$<>]+\\([A-Za-z0-9_.$ :+\\-]*\\)"))
            .put("native_frames",lines(nativeTrace,64,512,"#.*"))
            .put("breadcrumbs",lines(events,64,96,"[0-9 ]*[A-Z][A-Z0-9_]*"))
            .put("graphics",lines(graphics,8,256,null));
        return report;
    }
    private static void collect(Context context,String metadata,String stage,String events,String java,String graphics) {
        if(stage.isEmpty())return;
        try {
            JSONObject meta;
            try{meta=new JSONObject(metadata);if(!meta.optString("session_id").matches("[a-f0-9-]{36}"))return;}catch(Exception unavailable){return;}
            int pid=Integer.parseInt(stage.trim().split(" ")[1]);
            long cursor=context.getSharedPreferences("lh-diagnostics",0).getLong("upload-exit-cursor",0);
            boolean confirmed=false;
            if(Build.VERSION.SDK_INT>=30) {
                List<ApplicationExitInfo> exits=((ActivityManager)context.getSystemService(Context.ACTIVITY_SERVICE)).getHistoricalProcessExitReasons(context.getPackageName(),pid,5);
                Collections.sort(exits,(a,b)->Long.compare(a.getTimestamp(),b.getTimestamp()));
                for(ApplicationExitInfo exit:exits) {
                    if(exit.getTimestamp()<=cursor||exit.getPid()!=pid||exit.getTimestamp()<meta.optLong("session_started",0))continue;
                    confirmed=true;int reason=exit.getReason();String type=reason==3?"low_memory":reason==4?"java_crash":reason==5?"native_crash":reason==6?"anr":reason==2?"signal":"";
                    if(!type.isEmpty()) {
                        String trace="", anrFrames="";
                        if(visible&&Build.VERSION.SDK_INT>=31&&(reason==5 || (reason==2 &&
                            (exit.getStatus()==android.system.OsConstants.SIGSEGV || exit.getStatus()==android.system.OsConstants.SIGBUS))))try(InputStream in=exit.getTraceInputStream()) {
                            if(in!=null)trace=TombstoneSummary.read(new FilterInputStream(in){@Override public int read(byte[] b,int off,int len)throws IOException{if(!visible)throw new IOException();return super.read(b,off,len);}});
                        }catch(Exception ignored){}
                        if(visible&&reason==6)try(InputStream in=exit.getTraceInputStream()) {
                            if(in!=null)anrFrames=AnrSummary.read(new FilterInputStream(in){@Override public int read(byte[] b,int off,int len)throws IOException{if(!visible)throw new IOException();return super.read(b,off,len);}});
                        }catch(Exception ignored){}
                        JSONObject report=event(context,meta,type,exit.getTimestamp(),stage,events,java+anrFrames,graphics,trace,reason,exit.getStatus(),exit.getRss());
                        if(!queue(context).add(report.getString("event_id"),report.toString()))return;
                        CrashDiagnostics.write(new File(CrashDiagnostics.reports(context),"relatorio-"+exit.getTimestamp()+".txt"),"Lost Horizon — diagnóstico mobile\n"+report.toString(2));
                    }
                    cursor=exit.getTimestamp();context.getSharedPreferences("lh-diagnostics",0).edit().putLong("upload-exit-cursor",cursor).commit();
                }
            }
            // Older Androids can confirm Java exceptions from our private handler only.
            if(!confirmed&&!java.isEmpty()) {
                long timestamp=Long.parseLong(stage.trim().split(" ")[0])*1000;
                if(timestamp>cursor){String type=stage.contains("JAVA_CRASH")?"java_crash":"startup_failure";
                    JSONObject report=event(context,meta,type,timestamp,stage,events,java,graphics,"",type.equals("java_crash")?4:0,0,0);
                    if(queue(context).add(report.getString("event_id"),report.toString()))context.getSharedPreferences("lh-diagnostics",0).edit().putLong("upload-exit-cursor",timestamp).commit();}
            }
        }catch(Exception ignored){}
    }
    static void startupFailure(Context context) {
        final Context app=context.getApplicationContext();worker.execute(()->{
            try {if(currentMetadata==null)return;JSONObject report=event(app,currentMetadata,"startup_failure",System.currentTimeMillis(),
                CrashDiagnostics.read(new File(CrashDiagnostics.directory(app),"runtime-stage.txt")),CrashDiagnostics.read(new File(CrashDiagnostics.directory(app),"runtime-events.txt")),
                CrashDiagnostics.read(new File(CrashDiagnostics.directory(app),"java-failure.txt")),CrashDiagnostics.read(new File(CrashDiagnostics.directory(app),"graphics-info.txt")),"",0,0,0);
                queue(app).add(report.getString("event_id"),report.toString());}catch(Exception ignored){}
        });
    }
    static void visual(Context context,String category,StatusCallback result) {
        final Context app=context.getApplicationContext();worker.execute(()->{
            try{String graphics=CrashDiagnostics.read(new File(CrashDiagnostics.directory(app),"graphics-info.txt"));
                JSONObject meta=graphics.isEmpty()&&previousMetadata!=null?previousMetadata:metadata(app);
                JSONObject report=event(app,meta,"visual",System.currentTimeMillis(),CrashDiagnostics.read(new File(CrashDiagnostics.directory(app),"runtime-stage.txt")),"","",
                graphics.isEmpty()?previousGraphics:graphics,"",0,0,0);report.put("visual_category",category);
                if(queue(app).add(report.getString("event_id"),report.toString())){schedule(app,0);result.accept("Relatório visual adicionado à fila. Aguarde na tela de login para enviar.");}
                else result.accept("Não foi possível guardar o relatório. A fila pode estar cheia ou o armazenamento indisponível.");
            }catch(Exception ignored){result.accept("Não foi possível preparar o relatório visual.");}
        });
    }
    interface StatusCallback { void accept(String status); }
    static void retryNow(Context context,StatusCallback result) {
        final Context app=context.getApplicationContext();worker.execute(()->{
            synchronized(MobileReports.class){
                if(!allowed(app)){result.accept("Ative o envio automático e permaneça na tela de login para tentar novamente.");return;}
                if(queue(app).files().length==0){result.accept("Nenhum relatório pendente. Use Reportar problema visual para testar o envio.");return;}
                if(manualRetries>=3){result.accept("Limite de tentativas manuais desta abertura atingido. Feche e reabra o aplicativo.");return;}
                if(scheduled!=null){scheduled.cancel(false);scheduled=null;}
                manualRetries++;attemptLimit=Math.max(attemptLimit,attemptsThisSession+1);nextAttempt=0;
                app.getSharedPreferences("lh-diagnostics",0).edit().putLong("next-upload",0).apply();schedule(app,0);
                result.accept("Nova tentativa solicitada. Aguarde alguns segundos na tela de login e consulte o último resultado.");
            }
        });
    }
    static void status(Context context,StatusCallback result) {
        final Context app=context.getApplicationContext();worker.execute(()->{
            long last=app.getSharedPreferences("lh-diagnostics",0).getLong("last-upload",0);String destination="";try{destination=endpoint(app);}catch(Exception ignored){}
            String version="desconhecida";try{version=app.getPackageManager().getPackageInfo(app.getPackageName(),0).versionName;}catch(Exception ignored){}
            long attempt=app.getSharedPreferences("lh-diagnostics",0).getLong("last-attempt",0);
            String state=app.getSharedPreferences("lh-diagnostics",0).getString("last-upload-result","Ainda não houve tentativa de envio.");
            result.accept("APK: "+version+"\nDestino dos relatórios: "+(destination.isEmpty()?"não configurado":destination)+
                "\nEnvio automático: "+(enabled(app)?"ativado":"desativado")+"\nRelatórios pendentes: "+queue(app).files().length+" / 20\nÚltimo envio: "+(last==0?"nenhum":new java.util.Date(last).toString())+
                "\nÚltima tentativa: "+(attempt==0?"nenhuma":new java.util.Date(attempt).toString())+"\nResultado: "+state+
                (nextAttempt>System.currentTimeMillis()?"\nNova tentativa após: "+new java.util.Date(nextAttempt).toString():"")+
                "\nAté 3 tentativas automáticas por abertura, durante a tela de login. O jogo continua conectado ao staging.");
        });
    }
}
