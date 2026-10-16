package com.newdawn.launcher;

import android.app.Activity;
import android.app.Dialog;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.io.File;
import java.io.FileOutputStream;

/** Replaces the community version notice on the existing character screen. */
public final class TesterLogin {
    private static Activity activity;
    private static Dialog dialog;
    private static TextView status;
    private static EditText user, password, confirmation;
    private static Button submit, register, resume, close, accountButton;
    private static SharedPreferences preferences;
    private static boolean busy, registration, authenticated, selection;
    private static NewDawnApi.Session pending;
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final ExecutorService NETWORK = Executors.newSingleThreadExecutor();
    private static native void initialize();
    private static native boolean ready();
    private static native boolean accept(String token);

    public static void prepare(Activity current) {
        activity = current;
        preferences = current.getSharedPreferences("losthorizon-tester-auth-v1", Activity.MODE_PRIVATE);
        try {
            String config="gateway=http://179.197.72.129:8190\nname=Lost Horizon Brasil\nserver=Lost Horizon Brasil|http://179.197.72.129:8190\n";
            File folder=current.getExternalFilesDir(null);
            if(folder!=null){
                try(FileOutputStream out=new FileOutputStream(new File(folder,"offserver.txt"))){out.write(config.getBytes("UTF-8"));}
                String servers="{\"servers\":[{\"name\":\"Lost Horizon Brasil\",\"gateway\":\"http://179.197.72.129:8190\"}]}";
                try(FileOutputStream out=new FileOutputStream(new File(folder,"offserver.servers.cache.json"))){out.write(servers.getBytes("UTF-8"));}
            }
        } catch(Exception e) { android.util.Log.e("AppTester","server_config_failed"); }
        System.loadLibrary("tester");
        initialize();
        android.util.Log.i("AppTester", "activity_prepared");
    }
    public static void show(Activity current) {
        MAIN.post(() -> { if (current == activity && !current.isFinishing()) open(); });
    }
    public static void setVisible(Activity current, boolean visible) {
        MAIN.post(() -> {
            if (current != activity || current.isFinishing()) return;
            selection = visible;
            if (visible && accountButton == null) {
                ViewGroup root = current.findViewById(android.R.id.content);
                accountButton = new Button(current);
                accountButton.setText("Conta"); accountButton.setTextSize(14);
                accountButton.setTextColor(Color.WHITE); accountButton.setBackground(panel(0x99202722));
                FrameLayout.LayoutParams lp = new FrameLayout.LayoutParams(dp(92), dp(42), Gravity.TOP | Gravity.RIGHT);
                lp.topMargin = dp(12); lp.rightMargin = dp(16);
                root.addView(accountButton, lp);
                accountButton.setOnClickListener(v -> open());
            }
            if (accountButton != null) accountButton.setVisibility(visible ? View.VISIBLE : View.GONE);
        });
    }
    private static int dp(float n) { return (int)(n * activity.getResources().getDisplayMetrics().density + .5f); }
    private static GradientDrawable panel(int color) {
        GradientDrawable bg = new GradientDrawable(); bg.setColor(color); bg.setCornerRadius(dp(10));
        bg.setStroke(dp(1), 0xff776549); return bg;
    }
    private static TextView text(String value, int size, int color) {
        TextView t = new TextView(activity); t.setText(value); t.setTextSize(size); t.setTextColor(color);
        t.setGravity(Gravity.CENTER); t.setPadding(0, dp(4), 0, dp(4)); return t;
    }
    private static EditText field(String hint, boolean secret) {
        EditText e = new EditText(activity); e.setSingleLine(true); e.setHint(hint);
        e.setTextColor(Color.WHITE); e.setHintTextColor(0xffb5b7ad); e.setTextSize(16);
        e.setPadding(dp(14), dp(8), dp(14), dp(8)); e.setBackground(panel(0xff242c25));
        e.setInputType(secret ? InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD : InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, dp(46)); p.topMargin=dp(6); e.setLayoutParams(p);return e;
    }
    private static Button button(String label, boolean primary) {
        Button b = new Button(activity); b.setText(label); b.setAllCaps(false); b.setTextSize(15);
        b.setTextColor(primary ? 0xff1a201b : 0xffeed9a4); b.setBackground(panel(primary ? 0xffe5b849 : 0xff263028));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, dp(44));p.topMargin=dp(7);b.setLayoutParams(p);return b;
    }
    private static void open() {
        if (dialog != null && dialog.isShowing()) return;
        registration=false;busy=false;pending=null;
        dialog = new Dialog(activity, android.R.style.Theme_Material_NoActionBar);
        dialog.requestWindowFeature(Window.FEATURE_NO_TITLE); dialog.setCancelable(authenticated);
        LinearLayout content = new LinearLayout(activity);content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(24), dp(12), dp(24), dp(12));content.setBackground(panel(0xf51b241e));
        TextView title=text("Lost Horizon Brasil", 24, 0xffeed9a4);title.setTypeface(Typeface.DEFAULT,Typeface.BOLD);content.addView(title);
        content.addView(text("Build tester · Entre na sua conta",14,0xffe0e2d8));
        user=field("Usuário",false);user.setText(preferences.getString("username",""));content.addView(user);
        password=field("Senha",true);content.addView(password);
        confirmation=field("Confirmar senha",true);confirmation.setVisibility(View.GONE);content.addView(confirmation);
        status=text("Use a mesma conta do servidor brasileiro.",13,0xffd3d6cc);status.setMinHeight(dp(28));content.addView(status);
        submit=button("Entrar",true);content.addView(submit);submit.setOnClickListener(v -> authenticate(false));
        register=button("Criar conta",false);content.addView(register);register.setOnClickListener(v -> {
            if (busy) return;registration=!registration;
            confirmation.setVisibility(registration ? View.VISIBLE : View.GONE);
            submit.setText(registration ? "Criar conta e entrar" : "Entrar");register.setText(registration ? "Voltar ao login" : "Criar conta");
            status.setText(registration ? "A senha deve ter pelo menos 8 caracteres." : "Use a mesma conta do servidor brasileiro.");
        });
        resume=button("Continuar com a conta salva",false);content.addView(resume);
        resume.setVisibility(preferences.getString("session","").isEmpty() ? View.GONE : View.VISIBLE);resume.setOnClickListener(v -> authenticate(true));
        close=button("Voltar aos personagens",false);close.setVisibility(authenticated?View.VISIBLE:View.GONE);content.addView(close);close.setOnClickListener(v -> dialog.dismiss());
        ScrollView scroll=new ScrollView(activity);scroll.setFillViewport(false);scroll.addView(content);
        dialog.setContentView(scroll);Window w=dialog.getWindow();if(w==null)return;
        w.setBackgroundDrawableResource(android.R.color.transparent);w.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);
        w.setDimAmount(.55f);w.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE | WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN);
        dialog.show();int maxWidth=activity.getResources().getDisplayMetrics().widthPixels;
        int maxHeight=activity.getResources().getDisplayMetrics().heightPixels;
        w.setLayout(Math.min(dp(460),maxWidth-dp(32)),Math.min(dp(440),maxHeight-dp(24)));
        w.getDecorView().setSystemUiVisibility(5894);
        content.setFocusableInTouchMode(true);content.requestFocus();
        android.util.Log.i("AppTester","login_modal_shown");
    }
    private static void setBusy(boolean value,String message) {
        busy=value;status.setText(message);submit.setEnabled(!value);register.setEnabled(!value);resume.setEnabled(!value);
        user.setEnabled(!value);password.setEnabled(!value);confirmation.setEnabled(!value);close.setEnabled(!value);
    }
    private static void authenticate(boolean restore) {
        if(busy)return;
        if(!ready()){status.setText("Aguarde o jogo preparar a seleção de personagens.");return;}
        String name=user.getText().toString().trim(),secret=password.getText().toString();
        if(!restore && registration && !secret.equals(confirmation.getText().toString())){status.setText("As senhas não conferem.");return;}
        boolean create=registration;
        setBusy(true,"Conectando ao servidor…");
        String saved=preferences.getString("session","");
        NETWORK.execute(() -> {
            try {
                NewDawnApi.Session result=restore?NewDawnApi.restore(saved):NewDawnApi.authenticate(name,secret,create);
                MAIN.post(() -> {
                    pending=result;
                    if(!accept(result.accessToken)){pending=null;setBusy(false,"Não foi possível atualizar os personagens. Tente novamente.");return;}
                    setBusy(true,"Carregando seus personagens…");
                    MAIN.postDelayed(() -> {if(busy && pending==result){pending=null;setBusy(false,"O servidor demorou para responder. Tente novamente.");}},25000);
                });
            } catch(Exception e) {
                MAIN.post(() -> {
                    if(e instanceof NewDawnApi.ApiException && ((NewDawnApi.ApiException)e).sessionLost){preferences.edit().remove("session").apply();resume.setVisibility(View.GONE);}
                    setBusy(false,error(e));
                });
            }
        });
    }
    private static String error(Exception e){
        int code=e instanceof NewDawnApi.ApiException?((NewDawnApi.ApiException)e).messageId:0;
        switch(code){case 23:return "Use um usuário de 3 a 32 caracteres: letras, números, ponto, hífen ou sublinhado.";
        case 24:return "A senha deve ter pelo menos 8 caracteres.";case 25:return "Muitas tentativas. Aguarde um pouco.";
        case 27:return "A sessão salva expirou. Entre novamente.";case 48:return "Este usuário já está cadastrado.";
        case 49:return "Usuário ou senha incorretos.";default:return "Não foi possível conectar ao servidor. Tente novamente.";}
    }
    public static void charactersLoaded(boolean success) {
        MAIN.post(() -> {
            if(!busy||pending==null)return;
            if(!success){pending=null;setBusy(false,"Não foi possível carregar os personagens. Tente novamente.");return;}
            preferences.edit().putString("username",pending.email).putString("session",pending.refreshToken).apply();
            pending=null;authenticated=true;busy=false;password.setText("");confirmation.setText("");dialog.dismiss();
            android.util.Log.i("AppTester","login_completed");
        });
    }
}
