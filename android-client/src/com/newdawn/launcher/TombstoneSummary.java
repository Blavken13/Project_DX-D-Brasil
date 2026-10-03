package com.newdawn.launcher;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/** Minimal bounded protobuf reader for AOSP debuggerd/proto/tombstone.proto.
 * Export ONLY signal, page size, library basename and stack frames. Never
 * export logs, memory dumps, command lines, abort messages or open files. */
final class TombstoneSummary {
    static final int LIMIT = 4 * 1024 * 1024;
    static final class Field {
        int number; long value; byte[] bytes;
        String text() { return bytes == null ? "" : new String(bytes, StandardCharsets.UTF_8); }
    }
    static List<Field> fields(byte[] data) throws IOException {
        List<Field> out = new ArrayList<>(); int[] offset = {0};
        while (offset[0] < data.length) {
            long tag = varint(data, offset); Field field = new Field();
            field.number = (int)(tag >>> 3); int wire = (int)(tag & 7);
            if (field.number == 0 || out.size() >= 50000) throw new IOException("Invalid proto");
            if (wire == 0) field.value = varint(data, offset);
            else if (wire == 2) {
                long length = varint(data, offset);
                if (length < 0 || length > data.length - offset[0]) throw new IOException("Invalid length");
                field.bytes = java.util.Arrays.copyOfRange(data, offset[0], offset[0]+(int)length);
                offset[0] += (int)length;
            } else if (wire == 1 || wire == 5) {
                int length = wire == 1 ? 8 : 4;
                if (length > data.length-offset[0]) throw new IOException("Truncated proto");
                offset[0] += length;
            } else throw new IOException("Unsupported wire");
            out.add(field);
        }
        return out;
    }
    private static long varint(byte[] data, int[] offset) throws IOException {
        long value = 0;
        for (int shift=0; shift<64; shift+=7) {
            if (offset[0] >= data.length) throw new IOException("Truncated varint");
            int b = data[offset[0]++] & 255; value |= (long)(b & 127) << shift;
            if ((b & 128)==0) return value;
        }
        throw new IOException("Oversized varint");
    }
    static String read(InputStream stream) throws IOException {
        ByteArrayOutputStream data = new ByteArrayOutputStream(); byte[] buffer = new byte[8192]; int count;
        while ((count=stream.read(buffer))!=-1) {
            if (data.size()+count > LIMIT) return "Registro nativo excedeu o limite; motivo do Android preservado.\n";
            data.write(buffer,0,count);
        }
        return summarize(data.toByteArray());
    }
    static String summarize(byte[] data) throws IOException {
        List<Field> top = fields(data); long crashTid = 0; StringBuilder out = new StringBuilder();
        for (Field f:top) if(f.number==6) crashTid=f.value;
        for (Field f:top) {
            if(f.number==22) out.append("Página registrada no tombstone: ").append(f.value).append('\n');
            if(f.number==10 && f.bytes!=null) for(Field s:fields(f.bytes)) {
                if(s.number==1) out.append("Sinal nativo: ").append(s.value).append('\n');
                if(s.number==3) out.append("Código do sinal: ").append(s.value).append('\n');
            }
            if(f.number!=16 || f.bytes==null) continue;
            List<Field> entry=fields(f.bytes); long key=0; byte[] thread=null;
            for(Field e:entry){if(e.number==1)key=e.value;if(e.number==2)thread=e.bytes;}
            if(key!=crashTid || thread==null)continue;
            int index=0;
            for(Field t:fields(thread)) {
                if(t.number!=4 || t.bytes==null || index>=64)continue;
                long pc=0, delta=0;String function="",file="",build="";
                for(Field b:fields(t.bytes)) {
                    if(b.number==1)pc=b.value; if(b.number==4)function=b.text();
                    if(b.number==5)delta=b.value;if(b.number==6)file=b.text();if(b.number==8)build=b.text();
                }
                file=file.substring(file.lastIndexOf('/')+1);
                out.append('#').append(index++).append(" pc ").append(Long.toHexString(pc)).append(' ')
                    .append(clean(file)).append(' ').append(clean(function)).append('+').append(delta)
                    .append(" build=").append(clean(build)).append('\n');
            }
        }
        if(out.length()==0)out.append("Tombstone sem pilha disponível.\n");
        return out.toString();
    }
    private static String clean(String value) {
        return value.replaceAll("[\\r\\n\\p{Cntrl}]", " ").replaceAll("[A-Za-z0-9_-]{43,}", "[redigido]");
    }
}
