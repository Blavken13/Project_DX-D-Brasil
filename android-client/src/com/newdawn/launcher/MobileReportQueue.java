package com.newdawn.launcher;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Small durable outbox. No Android dependencies: exercised by host tests. */
final class MobileReportQueue {
    static final int MAX_BYTES=49152, MAX_FILES=20, MAX_TOTAL=1024*1024;
    private final File directory;
    MobileReportQueue(File directory) { this.directory=directory; }
    synchronized boolean add(String id,String json) {
        if(!id.matches("[a-f0-9-]{36}"))return false;
        byte[] bytes=json.getBytes(StandardCharsets.UTF_8);
        if(bytes.length>MAX_BYTES)return false;
        if(!directory.isDirectory()&&!directory.mkdirs())return false;
        File destination=new File(directory,id+".json"), temporary=new File(directory,id+".tmp");
        if(destination.exists())return true;
        File[] files=files();long total=bytes.length;
        for(File file:files)total+=file.length();
        // Preserve already captured events when the bounded outbox is full.
        if(files.length>=MAX_FILES||total>MAX_TOTAL)return false;
        try(FileOutputStream out=new FileOutputStream(temporary)) {
            out.write(bytes);out.getFD().sync();
        }catch(IOException error){temporary.delete();return false;}
        if(!temporary.renameTo(destination)){temporary.delete();return false;}
        return true;
    }
    synchronized File[] files() {
        File[] files=directory.listFiles((dir,name)->name.matches("[a-f0-9-]{36}\\.json"));
        if(files==null)return new File[0];
        Arrays.sort(files,(a,b)->Long.compare(a.lastModified(),b.lastModified()));return files;
    }
    synchronized void acknowledge(String id) { if(id.matches("[a-f0-9-]{36}"))new File(directory,id+".json").delete(); }
    synchronized void reject(String id) {
        if(!id.matches("[a-f0-9-]{36}"))return;
        File rejected=new File(directory.getParentFile(),"rejected");
        if(!rejected.isDirectory()&&!rejected.mkdirs())return;
        if(!new File(directory,id+".json").renameTo(new File(rejected,id+".json")))return;
        File[] files=rejected.listFiles((d,n)->n.endsWith(".json"));
        if(files!=null){Arrays.sort(files,(a,b)->Long.compare(b.lastModified(),a.lastModified()));for(int i=5;i<files.length;i++)files[i].delete();}
    }
    static String read(File file) throws IOException {
        if(file.length()>MAX_BYTES)throw new IOException("Report too large");
        try(FileInputStream in=new FileInputStream(file);ByteArrayOutputStream out=new ByteArrayOutputStream()) {
            byte[] buffer=new byte[4096];int length;
            while((length=in.read(buffer))!=-1){if(out.size()+length>MAX_BYTES)throw new IOException("Report too large");out.write(buffer,0,length);}
            return new String(out.toByteArray(),StandardCharsets.UTF_8);
        }
    }
}
