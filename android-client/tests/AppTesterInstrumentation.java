package com.losthorizon.tester.tests;

import android.app.Activity;
import android.app.Instrumentation;
import android.app.Dialog;
import android.content.Intent;
import android.os.Bundle;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;
import android.view.MotionEvent;
import android.os.SystemClock;
import android.graphics.Bitmap;
import java.io.File;
import java.io.FileOutputStream;
import org.json.JSONObject;
import java.lang.reflect.Field;

/** Runs inside the tester application; does not inject events into other apps. */
public final class AppTesterInstrumentation extends Instrumentation {
    private Bundle options;
    private Class<?> login;
    @Override public void onCreate(Bundle args) { options=args==null?new Bundle():args;start(); }
    private Object get(String name) throws Exception {Field f=login.getDeclaredField(name);f.setAccessible(true);return f.get(null);}
    private boolean flag(String name) throws Exception {return (Boolean)get(name);}
    private void main(Runnable r){runOnMainSync(r);}
    private void waitFor(String name) throws Exception {
        long deadline=System.currentTimeMillis()+65000;
        while(System.currentTimeMillis()<deadline){
            final boolean[] yes={false};main(() -> {try {Object v=get(name);yes[0]=v instanceof Dialog?((Dialog)v).isShowing():v!=null;}catch(Exception ignored){}});
            if(yes[0])return;Thread.sleep(300);
        }throw new AssertionError("Timed out waiting for "+name);
    }
    private void require(boolean condition,String message){if(!condition)throw new AssertionError(message);}
    private void photo(String name)throws Exception {
        Bitmap image=getUiAutomation().takeScreenshot();require(image!=null,"Screenshot unavailable");
        File folder=getTargetContext().getExternalFilesDir(null);
        try(FileOutputStream out=new FileOutputStream(new File(folder,"app-tester-"+name+".png"))){image.compress(Bitmap.CompressFormat.PNG,100,out);}image.recycle();
    }
    private void tap(Activity game,float x,float y){
        long down=SystemClock.uptimeMillis();main(() -> {
            MotionEvent event=MotionEvent.obtain(down,down,MotionEvent.ACTION_DOWN,x*game.getWindow().getDecorView().getWidth(),y*game.getWindow().getDecorView().getHeight(),0);
            event.setSource(android.view.InputDevice.SOURCE_TOUCHSCREEN);
            game.dispatchTouchEvent(event);event.recycle();
        });
        try{Thread.sleep(100);}catch(InterruptedException ignored){}
        main(() -> {MotionEvent event=MotionEvent.obtain(down,SystemClock.uptimeMillis(),MotionEvent.ACTION_UP,x*game.getWindow().getDecorView().getWidth(),y*game.getWindow().getDecorView().getHeight(),0);event.setSource(android.view.InputDevice.SOURCE_TOUCHSCREEN);game.dispatchTouchEvent(event);event.recycle();});
    }
    @Override public void onStart(){
        Bundle result=new Bundle();
        try {
            new File(getTargetContext().getExternalFilesDir(null),"app-tester-command.json").delete();
            login=getTargetContext().getClassLoader().loadClass("com.newdawn.launcher.TesterLogin");
            Intent intent=new Intent();intent.setClassName("com.losthorizon.tester","com.durango.offserver.DurangoActivity");intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            Activity game=startActivitySync(intent);
            waitFor("dialog");
            File folder=getTargetContext().getExternalFilesDir(null);
            String config=new String(java.nio.file.Files.readAllBytes(new File(folder,"offserver.txt").toPath()),"UTF-8");
            require(config.equals("gateway=http://179.197.72.129:8190\nname=Lost Horizon Brasil\nserver=Lost Horizon Brasil|http://179.197.72.129:8190\n"),"Unexpected server injected into configuration");
            JSONObject cache=new JSONObject(new String(java.nio.file.Files.readAllBytes(new File(folder,"offserver.servers.cache.json").toPath()),"UTF-8"));
            require(cache.getJSONArray("servers").length()==1,"Unexpected cached server");
            String mode=options.getString("mode","ui");
            final String[] failure={null};
            main(() -> {try {
                require(((Dialog)get("dialog")).isShowing(),"Login modal missing");
            }catch(Exception e){failure[0]=e.toString();}});
            if(mode.equals("ui")){
                main(() -> {try {
                    ((EditText)get("user")).setText("");((EditText)get("password")).setText("");((Button)get("submit")).performClick();
                }catch(Exception e){failure[0]=e.toString();}});
                Thread.sleep(900);
                main(() -> {try {
                    require(((TextView)get("status")).getText().toString().contains("3 a 32"),"Invalid username was not rejected");
                    ((Button)get("register")).performClick();((EditText)get("user")).setText("tester_validation");
                    ((EditText)get("password")).setText("ExampleValidationPass1");((EditText)get("confirmation")).setText("MismatchValidationPass1");
                    ((Button)get("submit")).performClick();require(((TextView)get("status")).getText().toString().contains("não conferem"),"Password confirmation was not rejected");
                    ((Button)get("register")).performClick();((EditText)get("password")).setText("");
                    require(!flag("authenticated"),"Validation must not grant a game identity");
                }catch(Throwable e){failure[0]=e.toString();}});
            }else if(mode.equals("inspect")||mode.equals("selection")){
                photo("login");main(() -> {try {((Button)get("resume")).performClick();}catch(Exception e){failure[0]=e.toString();}});
                long deadline=System.currentTimeMillis()+35000;boolean[] success={false};
                do{Thread.sleep(300);main(() -> {try{success[0]=flag("authenticated");}catch(Exception e){failure[0]=e.toString();}});}while(!success[0]&&System.currentTimeMillis()<deadline);
                require(success[0],"Saved session failed to restore");photo("selection");
                if(mode.equals("selection")){
                    int slot=Integer.parseInt(options.getString("slot","1"));require(slot>=0&&slot<=2,"Invalid slot");
                    tap(game,.57f,.789f);Thread.sleep(1600);photo("characters-slot"+slot);
                    tap(game,.343f+.157f*slot,.43f);Thread.sleep(800);photo("chosen-slot"+slot);
                    tap(game,.50f,.81f);Thread.sleep(2200);photo("confirmed-slot"+slot);
                    tap(game,.50f,.65f);Thread.sleep(30000);photo("world-slot"+slot);
                    require(!game.isFinishing(),"Game closed entering chosen character");
                }else{
                if("true".equals(options.getString("servers"))){
                    tap(game,.425f,.788f);Thread.sleep(1200);photo("servers");
                }else {Thread.sleep(18000);photo("world");}
                File commands=new File(getTargetContext().getExternalFilesDir(null),"app-tester-command.json");
                long end=System.currentTimeMillis()+240000;
                while(System.currentTimeMillis()<end){
                    if(commands.exists()){
                        byte[] bytes=java.nio.file.Files.readAllBytes(commands.toPath());commands.delete();JSONObject action=new JSONObject(new String(bytes,"UTF-8"));
                        if(action.optString("action").equals("stop"))break;
                        if(action.optString("action").equals("tap"))tap(game,(float)action.getDouble("x"),(float)action.getDouble("y"));
                        Thread.sleep(1000);photo(action.optString("name","inspect"));
                    }Thread.sleep(250);
                }
                }
            }else if(mode.equals("invalid")){
                main(() -> {try {
                    ((EditText)get("user")).setText("lh_no_account_"+System.currentTimeMillis());((EditText)get("password")).setText("InvalidLoginProbeOnly1");((Button)get("submit")).performClick();
                }catch(Exception e){failure[0]=e.toString();}});
                long deadline=System.currentTimeMillis()+22000;boolean[] active={true};
                do{Thread.sleep(250);main(() -> {try{active[0]=flag("busy");}catch(Exception e){failure[0]=e.toString();}});}while(active[0]&&System.currentTimeMillis()<deadline);
                main(() -> {try {
                    String message=((TextView)get("status")).getText().toString();
                    require(message.contains("incorretos"),"Expected server rejection: "+message);
                    require(!flag("authenticated"),"Invalid credentials must not grant identity");
                }catch(Throwable e){failure[0]=e.toString();}});
            }
            if(failure[0]!=null)throw new AssertionError(failure[0]);
            require(!game.isFinishing(),"Game unexpectedly closed");
            result.putString("result","PASS: "+mode+"; Unity running; authentication modal active");
            finish(Activity.RESULT_OK,result);
        }catch(Throwable e){result.putString("result","FAIL: "+e);finish(Activity.RESULT_CANCELED,result);}
    }
}
