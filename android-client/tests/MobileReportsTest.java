package com.newdawn.launcher;

import com.sun.net.httpserver.HttpServer;
import java.io.*;
import java.net.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

public class MobileReportsTest {
    private static int checks;
    private static void check(boolean value,String message){if(!value)throw new AssertionError(message);checks++;System.out.println("OK "+message);}
    public static void main(String[] args)throws Exception {
        Path root=Files.createTempDirectory("lh-reports-check-");
        HttpServer server=HttpServer.create(new InetSocketAddress("127.0.0.1",0),0);
        ExecutorService executor=Executors.newCachedThreadPool();server.setExecutor(executor);
        AtomicInteger status=new AtomicInteger(503);AtomicReference<String> response=new AtomicReference<>("{}");
        CountDownLatch slowStarted=new CountDownLatch(1), releaseSlow=new CountDownLatch(1);
        AtomicBoolean active=new AtomicBoolean(true);AtomicReference<HttpURLConnection> connection=new AtomicReference<>();
        MobileReportTransport.Gate gate=new MobileReportTransport.Gate(){public boolean allowed(){return active.get();}public void connection(HttpURLConnection c){connection.set(c);}};
        server.createContext("/reports",exchange->{while(exchange.getRequestBody().read()!=-1){}byte[] out=response.get().getBytes("UTF-8");exchange.sendResponseHeaders(status.get(),out.length);exchange.getResponseBody().write(out);exchange.close();});
        server.createContext("/slow",exchange->{slowStarted.countDown();try{releaseSlow.await(5,TimeUnit.SECONDS);}catch(InterruptedException ignored){}exchange.close();});
        server.start();String origin="http://127.0.0.1:"+server.getAddress().getPort();
        try {
            MobileReportQueue queue=new MobileReportQueue(root.resolve("pending").toFile());String id=UUID.randomUUID().toString();
            check(queue.add(id,"{\"event_id\":\""+id+"\"}"),"queue writes durable report");
            check(new MobileReportQueue(root.resolve("pending").toFile()).files().length==1,"offline report survives restart");
            check(queue.add(id,"{}")&&queue.files().length==1,"same ID is queued once");
            check(!queue.add("../escape","{}"),"queue rejects invalid path");
            File report=queue.files()[0];
            try{MobileReportTransport.send(origin+"/reports",report,gate);throw new AssertionError();}catch(IOException expected){}
            check(queue.files().length==1,"503 preserves report for retry");
            status.set(200);response.set("{\"accepted\":[\""+UUID.randomUUID()+"\"]}");
            try{MobileReportTransport.send(origin+"/reports",report,gate);throw new AssertionError();}catch(IOException expected){}
            check(queue.files().length==1,"wrong acknowledgment does not delete report");
            response.set("{\"accepted\":[\""+id+"\"]}");
            check(MobileReportTransport.send(origin+"/reports",report,gate),"200 with matching UUID is confirmed");queue.acknowledge(id);
            check(queue.files().length==0,"only confirmed report is removed");
            for(int i=0;i<20;i++)check(queue.add(UUID.randomUUID().toString(),"{}"),"bounded queue accepts entry "+i);
            check(!queue.add(UUID.randomUUID().toString(),"{}")&&queue.files().length==20,"queue stops at 20 reports without deleting old ones");
            MobileReportQueue other=new MobileReportQueue(root.resolve("other").toFile());check(!other.add(UUID.randomUUID().toString(),new String(new char[49153]).replace('\0','x')),"oversized report rejected");
            active.set(false);check(!MobileReportTransport.send(origin+"/reports",queue.files()[0],gate),"game gate prevents request");active.set(true);
            Future<Boolean> slow=executor.submit(()->{try{return MobileReportTransport.send(origin+"/slow",queue.files()[0],gate);}catch(IOException expected){return false;}});
            check(slowStarted.await(3,TimeUnit.SECONDS),"in-flight request starts");long started=System.nanoTime();active.set(false);connection.get().disconnect();
            check(!slow.get(1,TimeUnit.SECONDS)&&TimeUnit.NANOSECONDS.toMillis(System.nanoTime()-started)<1000,"starting game cancels in-flight upload");
            check(queue.files().length==20,"cancellation preserves pending report");
            String rejectedId=queue.files()[0].getName().replace(".json","");queue.reject(rejectedId);
            check(queue.files().length==19&&Files.exists(root.resolve("rejected").resolve(rejectedId+".json")),"permanent rejection is quarantined without blocking later reports");
            System.out.println("PASS "+checks+" mobile queue/HTTP checks");
        }finally{releaseSlow.countDown();server.stop(0);executor.shutdownNow();try(java.util.stream.Stream<Path> paths=Files.walk(root)){paths.sorted(Comparator.reverseOrder()).forEach(p->{try{Files.delete(p);}catch(IOException error){throw new UncheckedIOException(error);}});}}
    }
}
