package com.newdawn.launcher;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

public final class TombstoneSummaryTest {
    static byte[] number(int field,long value){ByteArrayOutputStream out=new ByteArrayOutputStream();varint(out,field*8);varint(out,value);return out.toByteArray();}
    static void varint(ByteArrayOutputStream out,long value){while((value&~127L)!=0){out.write((int)(value&127)|128);value>>>=7;}out.write((int)value);}
    static byte[] bytes(int field,byte[] value){ByteArrayOutputStream out=new ByteArrayOutputStream();varint(out,field*8+2);varint(out,value.length);out.write(value,0,value.length);return out.toByteArray();}
    static byte[] text(int field,String value){return bytes(field,value.getBytes(StandardCharsets.UTF_8));}
    static byte[] join(byte[]...values){ByteArrayOutputStream out=new ByteArrayOutputStream();for(byte[] v:values)out.write(v,0,v.length);return out.toByteArray();}
    public static void main(String[] args)throws Exception {
        String secret="abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
        byte[] frame=join(number(1,0x1234),text(4,"il2cpp_init"),number(5,12),text(6,"/private/account-secret/libil2cpp.so"),text(8,"abcd1234"));
        byte[] thread=join(number(1,123),bytes(4,frame),bytes(5,text(4,secret)),text(2,"user@example.com"));
        byte[] other=join(number(1,999),text(2,"password"),bytes(4,join(text(4,secret),text(6,"wrong-thread.so"))));
        byte[] input=join(number(6,123),number(22,16384),bytes(10,join(number(1,11),number(3,1))),
            bytes(16,join(number(1,123),bytes(2,thread))),bytes(16,join(number(1,999),bytes(2,other))),
            text(14,secret),text(9,"password"),bytes(18,text(1,"token="+secret)));
        String report=TombstoneSummary.summarize(input);
        String build="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        byte[] buildFrame=join(number(1,0x1234),text(6,"libunity.so"),text(8,build));
        String buildReport=TombstoneSummary.summarize(join(number(6,123),bytes(16,join(number(1,123),bytes(2,bytes(4,buildFrame))))));
        if(!buildReport.contains("build="+build))throw new AssertionError("BuildID was redacted: "+buildReport);
        if(!report.contains("16384")||!report.contains("1234 libil2cpp.so il2cpp_init+12")||!report.contains("Sinal nativo: 11"))throw new AssertionError(report);
        for(String forbidden:new String[]{secret,"account-secret","user@example.com","password","wrong-thread.so"})if(report.contains(forbidden))throw new AssertionError("Leak: "+forbidden);
        for(byte[] malformed:new byte[][]{{10,(byte)255},{0},{(byte)128},{15}}){
            try{TombstoneSummary.summarize(malformed);throw new AssertionError("Malformed accepted");}catch(IOException expected){}
        }
        System.out.println("PASS: native signal/page/frames extracted; logs, memory, credentials and other threads excluded; malformed proto rejected.");
    }
}
