package com.newdawn.launcher;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.regex.Pattern;

/** Bounded HTTP transport, also exercised on the host without Android stubs. */
final class MobileReportTransport {
    static final class Rejected extends IOException {
        final int status;
        Rejected(int status){super("HTTP "+status);this.status=status;}
    }
    interface Gate {
        boolean allowed();
        void connection(HttpURLConnection value) throws IOException;
    }
    static boolean send(String endpoint,File report,Gate gate) throws IOException {
        if(!gate.allowed())return false;
        String id=report.getName().replace(".json","");
        byte[] bytes=("{\"schema_version\":1,\"report\":"+MobileReportQueue.read(report)+"}").getBytes(StandardCharsets.UTF_8);
        HttpURLConnection http=(HttpURLConnection)new URL(endpoint).openConnection();
        try {
            http.setConnectTimeout(2500);http.setReadTimeout(3500);http.setInstanceFollowRedirects(false);
            http.setRequestMethod("POST");http.setDoOutput(true);http.setRequestProperty("Content-Type","application/json; charset=utf-8");
            http.setFixedLengthStreamingMode(bytes.length);
            gate.connection(http);
            if(!gate.allowed())return false;
            try(OutputStream out=http.getOutputStream()){if(!gate.allowed())return false;out.write(bytes);}
            if(!gate.allowed())return false;
            int status=http.getResponseCode();
            if(status!=200)throw new Rejected(status);
            ByteArrayOutputStream data=new ByteArrayOutputStream();byte[] buffer=new byte[512];int n;
            try(InputStream in=http.getInputStream()){while((n=in.read(buffer))!=-1){if(!gate.allowed())return false;if(data.size()+n>4096)throw new IOException("Response too large");data.write(buffer,0,n);}}
            // Contract is deliberately minimal. Never delete a queued report for an unrelated 200 response.
            String response=new String(data.toByteArray(),StandardCharsets.UTF_8);
            if(!response.matches("\\s*\\{\\s*\"accepted\"\\s*:\\s*\\[\\s*\""+Pattern.quote(id)+"\"\\s*\\]\\s*\\}\\s*"))throw new IOException("Missing acknowledgment");
            return gate.allowed();
        }finally{http.disconnect();gate.connection(null);}
    }
}
