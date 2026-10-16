/* In-engine modal for the verified Unity 2017 ARM64 client. No Activity login.
 * All managed calls and account replay occur on the Unity thread. */
#define _GNU_SOURCE
#include <jni.h>
#include <dlfcn.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include "runtime_compat.h"
#include "engine_login_offsets.h"

typedef struct { float x, y, width, height; } Rect;
static void *library;
static uintptr_t base;
static char *auth_token;
static void *(*invoke)(const void *, void *, void **, void **);
static void *(*string_new)(const char *);
static void *(*object_new)(void *);
static void *(*object_class)(void *);
static const void *(*method_find)(void *, const char *, int);
static void *(*unbox)(void *);
static uint32_t (*root_new)(void *, uint8_t);
static void *(*root_target)(uint32_t);
static void (*root_free)(uint32_t);
static const uint16_t *(*string_chars)(void *);
static int32_t (*string_length)(void *);
static uint32_t user, pass, confirmation, keyboard, pending_url, pending_callback, user_control, title;
static int modal, loading, registration, editing, installed, api_failed, selection_pending;
static JavaVM *vm;
static jclass java_login;
static void (*original_start)(void *, const void *);
static void (*original_accounts)(void *, void *, void *, const void *);
static void (*original_updated)(void *, void *, const void *);
static void (*original_camera)(void *, const void *);
static void (*original_confirm)(void *, const void *);
static void *unused_gui;
static const void *gui_label, *gui_box, *gui_button, *gui_enabled, *gui_depth;
static const void *keyboard_open, *keyboard_text, *keyboard_active, *keyboard_cancelled;
static const void *keyboard_close, *select_players, *debug_create, *title_state;
static const void *skin_get, *skin_label, *skin_button, *style_size, *style_alignment;
static const void *matrix_get, *matrix_set;
static const void *color_get, *color_set, *draw_texture, *white_texture;
static jmethodID j_submit, j_continue, j_busy, j_message, j_token, j_remember, j_saved, j_forget, j_diagnostics, j_quit;
static jmethodID j_account_failed, j_start_failed;

static void keep(uint32_t *slot, void *object) {
    uint32_t next = object ? root_new(object, 0) : 0;
    if (*slot) root_free(*slot);
    *slot = next;
}
static void *target(uint32_t slot) { return slot ? root_target(slot) : NULL; }
static void *call(const void *method, void *self, void **args) {
    if (!method || api_failed) return NULL;
    void *exception = NULL;
    void *result = invoke(method, self, args, &exception);
    if (exception) {
        api_failed = 1; br_stage("ENGINE_MODAL_MANAGED_FAILED");
        int attached = 0; JNIEnv *env = NULL;
        if ((*vm)->GetEnv(vm, (void **)&env, JNI_VERSION_1_6) == JNI_EDETACHED) {
            if ((*vm)->AttachCurrentThread(vm, &env, NULL) == JNI_OK) attached = 1;
        }
        if (env) (*env)->CallStaticVoidMethod(env, java_login, j_start_failed, NULL);
        if (attached) (*vm)->DetachCurrentThread(vm);
    }
    return result;
}
static int boolean(void *value) { return value && *(uint8_t *)unbox(value); }
static void *klass(const char *space, const char *name) {
    void *(*domain)(void) = dlsym(library, "il2cpp_domain_get");
    const void **(*assemblies)(void *, size_t *) = dlsym(library, "il2cpp_domain_get_assemblies");
    void *(*image)(const void *) = dlsym(library, "il2cpp_assembly_get_image");
    void *(*find)(void *, const char *, const char *) = dlsym(library, "il2cpp_class_from_name");
    size_t count = 0; void *current = domain();
    if (!current) return NULL;
    const void **all = assemblies(current, &count);
    if (!all || count > 4096) return NULL;
    for (size_t i = 0; i < count; i++) { void *c = find(image(all[i]), space, name); if (c) return c; }
    return NULL;
}
static const void *method(const char *space, const char *name, const char *member, int count) {
    void *c = klass(space, name); return c ? method_find(c, member, count) : NULL;
}
static JNIEnv *environment(int *attached) {
    JNIEnv *env = NULL; *attached = 0;
    if ((*vm)->GetEnv(vm, (void **)&env, JNI_VERSION_1_6) == JNI_EDETACHED) {
        if ((*vm)->AttachCurrentThread(vm, &env, NULL) != JNI_OK) return NULL;
        *attached = 1;
    }
    return env;
}
static jstring java_string(JNIEnv *env, void *value) {
    return value ? (*env)->NewString(env, (const jchar *)string_chars(value), string_length(value)) : (*env)->NewStringUTF(env, "");
}
static void *managed_string(JNIEnv *env, jstring value) {
    if (!value) return string_new("");
    const char *text = (*env)->GetStringUTFChars(env, value, NULL);
    /* Login usernames/messages use UTF-8; keyboard credentials remain UTF-16. */
    void *(*new_utf16)(const uint16_t *, int32_t) = dlsym(library, "il2cpp_string_new_utf16");
    void *result = NULL;
    if (new_utf16) {
        const jchar *chars = (*env)->GetStringChars(env, value, NULL);
        if (chars) { result = new_utf16(chars, (*env)->GetStringLength(env, value)); (*env)->ReleaseStringChars(env, value, chars); }
    } else if (text) result = string_new(text);
    if (text) (*env)->ReleaseStringUTFChars(env, value, text);
    return result;
}
static int bind_gui(void) {
    gui_label = method("UnityEngine", "GUI", "Label", 2);
    gui_box = method("UnityEngine", "GUI", "Box", 2);
    gui_button = method("UnityEngine", "GUI", "Button", 2);
    gui_enabled = method("UnityEngine", "GUI", "set_enabled", 1);
    gui_depth = method("UnityEngine", "GUI", "set_depth", 1);
    skin_get = method("UnityEngine", "GUI", "get_skin", 0);
    skin_label = method("UnityEngine", "GUISkin", "get_label", 0);
    skin_button = method("UnityEngine", "GUISkin", "get_button", 0);
    style_size = method("UnityEngine", "GUIStyle", "set_fontSize", 1);
    style_alignment = method("UnityEngine", "GUIStyle", "set_alignment", 1);
    matrix_get = method("UnityEngine", "GUI", "get_matrix", 0);
    matrix_set = method("UnityEngine", "GUI", "set_matrix", 1);
    color_get = method("UnityEngine", "GUI", "get_color", 0);
    color_set = method("UnityEngine", "GUI", "set_color", 1);
    draw_texture = method("UnityEngine", "GUI", "DrawTexture", 2);
    white_texture = method("UnityEngine", "Texture2D", "get_whiteTexture", 0);
    keyboard_open = method("UnityEngine", "TouchScreenKeyboard", "Open", 7);
    keyboard_text = method("UnityEngine", "TouchScreenKeyboard", "get_text", 0);
    keyboard_active = method("UnityEngine", "TouchScreenKeyboard", "get_active", 0);
    keyboard_cancelled = method("UnityEngine", "TouchScreenKeyboard", "get_wasCanceled", 0);
    keyboard_close = method("UnityEngine", "TouchScreenKeyboard", "set_active", 1);
    select_players = method("Durango.UI", "TitleMenuUserControlBase", "PlayerSelectionButton_Clicked", 0);
    debug_create = method("", "NGUIDebug", "CreateInstance", 0);
    title_state = method("Durango.UI", "TitleMenuGroup", "get_CurState", 0);
    return gui_label && gui_box && gui_button && gui_enabled && matrix_get && matrix_set
        && keyboard_open && keyboard_text && keyboard_active && keyboard_close && select_players && debug_create;
}
static void label(Rect rect, const char *text) { void *args[] = {&rect, string_new(text)}; call(gui_label, NULL, args); }
static int button(Rect rect, void *text) { void *args[] = {&rect, text}; return boolean(call(gui_button, NULL, args)); }
static int text_button(Rect rect, const char *text) { return button(rect, string_new(text)); }
static void set_enabled(uint8_t enabled) { void *args[] = {&enabled}; call(gui_enabled, NULL, args); }
static void close_keyboard(void) {
    if (keyboard) { uint8_t active = 0; void *args[] = {&active}; call(keyboard_close, target(keyboard), args); keep(&keyboard, NULL); }
    editing = 0;
}
static void edit(int field) {
    close_keyboard(); editing = field;
    uint8_t autocorrect = 0, multiline = 0, secure = field != 1, alert = 0;
    int type = 0;
    void *value = target(field == 1 ? user : field == 2 ? pass : confirmation);
    void *args[] = {value, &type, &autocorrect, &multiline, &secure, &alert, string_new(field == 1 ? "Usuário" : "Senha")};
    keep(&keyboard, call(keyboard_open, NULL, args));
}
static void update_keyboard(void) {
    if (!keyboard) return;
    void *key = target(keyboard);
    if (!boolean(call(keyboard_cancelled, key, NULL))) {
        void *value = call(keyboard_text, key, NULL);
        if (value) keep(editing == 1 ? &user : editing == 2 ? &pass : &confirmation, value);
    }
    if (!boolean(call(keyboard_active, key, NULL))) { keep(&keyboard, NULL); editing = 0; }
}
static void invoke_callback(void *callback, void *account) {
    const void *m = method_find(object_class(callback), "Invoke", 1);
    void *args[] = {account}; call(m, callback, args);
}
static void account_request(void *unused, void *gateway, void *callback, const void *info) {
    if (auth_token[0]) { original_accounts(unused, gateway, callback, info); return; }
    keep(&pending_url, gateway); keep(&pending_callback, callback);
    modal = 1; loading = 0;
    br_stage("ENGINE_ACCOUNT_WAITING_FOR_AUTH");
    // Empty, zero-slot DTO only builds the selection background. No identity,
    // characters or session are granted before the real gateway authenticates.
    void *account_class = klass("Durango.Logic.Clusters", "Account");
    void *account = account_class ? object_new(account_class) : NULL;
    if (account && callback) {
        uint32_t account_root = root_new(account, 0);
        call(method_find(account_class, ".ctor", 0), account, NULL);
        invoke_callback(callback, account);
        root_free(account_root);
    }
}
static void account_updated(void *self, void *account, const void *info) {
    keep(&user_control, self);
    original_updated(self, account, info);
    if (modal && account) {
        selection_pending = 1;
        if (auth_token[0] && loading) {
            modal = 0; loading = 0;
            keep(&pass, string_new("")); keep(&confirmation, string_new(""));
            br_stage("ENGINE_CHARACTERS_READY");
        }
    } else if (modal && !account && loading) {
        auth_token[0] = 0; loading = 0; br_stage("ENGINE_ACCOUNT_LOAD_FAILED");
        int attached; JNIEnv *env = environment(&attached);
        if (env) (*env)->CallStaticVoidMethod(env, java_login, j_account_failed);
        if (attached) (*vm)->DetachCurrentThread(vm);
    }
}
static void title_start(void *self, const void *info) {
    keep(&title, self);
    int ready = gui_label || bind_gui();
    original_start(self, info);
    if (!ready) { br_stage("ENGINE_MODAL_APIS_MISSING"); api_failed = 1; return; }
    call(debug_create, NULL, NULL);
    br_stage("ENGINE_SELECTION_STARTED");
}
static void camera_update(void *self, const void *info) { if (!modal) original_camera(self, info); }
static void confirm(void *self, const void *info) { if (auth_token[0] && !modal) original_confirm(self, info); }
__attribute__((noinline)) static int apply_token(const char *value) {
    if (!value || loading || !pending_url || !pending_callback || strlen(value) != 43) return 0;
    for (int i = 0; i < 43; i++) {
        char c = value[i];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return 0;
    }
    memcpy(auth_token, value, 44); loading = 1; close_keyboard();
    br_stage("ENGINE_AUTH_TOKEN_APPLIED");
    original_accounts(NULL, target(pending_url), target(pending_callback), NULL);
    return 1;
}
static void on_gui(void *self, const void *info) {
    (void)self; (void)info;
    if (!gui_label || api_failed) return;
    int attached = 0; JNIEnv *env = environment(&attached);
    if (!env) return;
    float saved_matrix[16]; int transformed = 0;
    float saved_color[4]; int recolored = 0;
    int (*width)(void) = NULL, (*height)(void) = NULL;
    void *(*resolve)(const char *) = dlsym(library, "il2cpp_resolve_icall");
    if (resolve) { width = resolve("UnityEngine.Screen::get_width()"); height = resolve("UnityEngine.Screen::get_height()"); }
    float w = width ? width() : 0, h = height ? height() : 0;
    if (w <= 0 || h <= 0) goto done;
    if (selection_pending && user_control && select_players) {
        selection_pending = 0;
        call(select_players, target(user_control), NULL);
    }
    float scale = h / 650.0f;
    if (560 * scale > w * .92f) scale = w * .92f / 560;
    // Matrix scaling works even when GUIStyle.fontSize was stripped from IL2CPP.
    void *previous = call(matrix_get, NULL, NULL);
    if (previous) {
        memcpy(saved_matrix, unbox(previous), sizeof(saved_matrix));
        float matrix[16] = {0}; matrix[0] = scale; matrix[5] = scale; matrix[10] = matrix[15] = 1;
        void *args[] = {matrix}; call(matrix_set, NULL, args); transformed = 1;
        w /= scale; h /= scale; scale = 1;
    }
    void *skin = call(skin_get, NULL, NULL);
    int font = (int)(22 * scale); if (font < 12) font = 12;
    int alignment = 4; void *font_args[] = {&font}, *align_args[] = {&alignment};
    void *style = call(skin_button, skin, NULL); call(style_size, style, font_args); call(style_alignment, style, align_args);
    style = call(skin_label, skin, NULL); call(style_size, style, font_args); call(style_alignment, style, align_args);
    int depth = -100; void *depth_args[] = {&depth}; call(gui_depth, NULL, depth_args);
    if (!modal) {
        void *state = title ? call(title_state, target(title), NULL) : NULL;
        int value = state ? *(int *)unbox(state) : -1;
        if (value == 2 || value == 3) {
            if (text_button((Rect){w - 235 * scale, 14 * scale, 220 * scale, 48 * scale}, "Trocar de conta")) {
                (*env)->CallStaticVoidMethod(env, java_login, j_forget);
                auth_token[0] = 0; registration = 0; modal = 1;
                close_keyboard(); keep(&pass, string_new("")); keep(&confirmation, string_new(""));
                if (pending_callback && pending_url) account_request(NULL, target(pending_url), target(pending_callback), NULL);
            }
        }
        goto done;
    }
    update_keyboard();
    if (!user) {
        jstring value = (*env)->CallStaticObjectMethod(env, java_login, j_remember);
        keep(&user, managed_string(env, value)); if (value) (*env)->DeleteLocalRef(env, value);
        keep(&pass, string_new("")); keep(&confirmation, string_new(""));
        br_stage("ENGINE_LOGIN_MODAL_VISIBLE");
    }
    jstring ticket = (*env)->CallStaticObjectMethod(env, java_login, j_token);
    if (ticket && !loading) {
        const char *value = (*env)->GetStringUTFChars(env, ticket, NULL);
        apply_token(value);
        if (value) (*env)->ReleaseStringUTFChars(env, ticket, value);
    }
    if (ticket) (*env)->DeleteLocalRef(env, ticket);
    uint8_t busy = loading || (*env)->CallStaticBooleanMethod(env, java_login, j_busy);
    float cw = 560 * scale, ch = (registration ? 600 : 540) * scale;
    float x = (w - cw) / 2, y = (h - ch) / 2;
    Rect shade = {0, 0, w, h}, card = {x, y, cw, ch};
    void *texture = white_texture ? call(white_texture, NULL, NULL) : NULL;
    void *prior_color = call(color_get, NULL, NULL);
    if (texture && prior_color && draw_texture && color_set) {
        memcpy(saved_color, unbox(prior_color), sizeof(saved_color)); recolored = 1;
        float shade_color[] = {0, 0, 0, .55f}; void *color_args[] = {shade_color}; call(color_set, NULL, color_args);
        void *shade_args[] = {&shade, texture}; call(draw_texture, NULL, shade_args);
        float card_color[] = {.055f, .075f, .09f, .98f}; color_args[0] = card_color; call(color_set, NULL, color_args);
        void *card_args[] = {&card, texture}; call(draw_texture, NULL, card_args);
        float white[] = {1, 1, 1, 1}; color_args[0] = white; call(color_set, NULL, color_args);
    } else {
        void *shade_args[] = {&shade, string_new("")}, *card_args[] = {&card, string_new("")};
        call(gui_box, NULL, shade_args); call(gui_box, NULL, card_args);
    }
    label((Rect){x + 24 * scale, y + 20 * scale, cw - 48 * scale, 45 * scale}, registration ? "Criar conta" : "Entrar na conta");
    label((Rect){x + 24 * scale, y + 62 * scale, cw - 48 * scale, 32 * scale}, "Lost Horizon");
    set_enabled(!busy);
    float row = y + 105 * scale;
    label((Rect){x + 24 * scale, row, cw - 48 * scale, 28 * scale}, "Usuário"); row += 30 * scale;
    if (button((Rect){x + 30 * scale, row, cw - 60 * scale, 48 * scale}, target(user))) edit(1);
    row += 58 * scale;
    label((Rect){x + 24 * scale, row, cw - 48 * scale, 28 * scale}, "Senha"); row += 30 * scale;
    char masked[25]; int length = target(pass) ? string_length(target(pass)) : 0; if (length > 24) length = 24;
    memset(masked, '*', (size_t)length); masked[length] = 0;
    if (text_button((Rect){x + 30 * scale, row, cw - 60 * scale, 48 * scale}, masked)) edit(2);
    row += 58 * scale;
    if (registration) {
        label((Rect){x + 24 * scale, row, cw - 48 * scale, 28 * scale}, "Confirmar senha"); row += 30 * scale;
        length = target(confirmation) ? string_length(target(confirmation)) : 0; if (length > 24) length = 24;
        memset(masked, '*', (size_t)length); masked[length] = 0;
        if (text_button((Rect){x + 30 * scale, row, cw - 60 * scale, 48 * scale}, masked)) edit(3);
        row += 58 * scale;
    }
    if (text_button((Rect){x + 30 * scale, row, cw - 60 * scale, 48 * scale}, registration ? "Criar conta" : "Entrar")) {
        close_keyboard();
        jstring name = java_string(env, target(user)), password = java_string(env, target(pass)), confirm = java_string(env, target(confirmation));
        (*env)->CallStaticVoidMethod(env, java_login, j_submit, name, password, confirm, (jboolean)registration);
        (*env)->DeleteLocalRef(env, name); (*env)->DeleteLocalRef(env, password); (*env)->DeleteLocalRef(env, confirm);
    }
    row += 56 * scale;
    if (!registration && (*env)->CallStaticBooleanMethod(env, java_login, j_saved)) {
        if (text_button((Rect){x + 30 * scale, row, cw - 60 * scale, 44 * scale}, "Continuar com a conta salva")) (*env)->CallStaticVoidMethod(env, java_login, j_continue);
        row += 48 * scale;
    }
    if (text_button((Rect){x + 30 * scale, row, cw - 60 * scale, 40 * scale}, registration ? "Já tenho uma conta" : "Criar uma conta")) { registration = !registration; close_keyboard(); }
    set_enabled(1);
    jstring message = (*env)->CallStaticObjectMethod(env, java_login, j_message);
    void *message_args[] = {&(Rect){x + 20 * scale, y + ch - 100 * scale, cw - 40 * scale, 50 * scale},
                           loading ? string_new("Carregando os personagens…") : managed_string(env, message)};
    call(gui_label, NULL, message_args); if (message) (*env)->DeleteLocalRef(env, message);
    if (text_button((Rect){x + 30 * scale, y + ch - 44 * scale, (cw - 70 * scale) / 2, 34 * scale}, "Diagnóstico")) (*env)->CallStaticVoidMethod(env, java_login, j_diagnostics);
    if (text_button((Rect){x + cw / 2 + 5 * scale, y + ch - 44 * scale, (cw - 70 * scale) / 2, 34 * scale}, "Sair")) (*env)->CallStaticVoidMethod(env, java_login, j_quit);
done:
    if (recolored) { void *args[] = {saved_color}; call(color_set, NULL, args); }
    if (transformed) { void *args[] = {saved_matrix}; call(matrix_set, NULL, args); }
    if ((*env)->ExceptionCheck(env)) { (*env)->ExceptionClear(env); br_stage("ENGINE_LOGIN_JAVA_FAILED"); }
    if (attached) (*vm)->DetachCurrentThread(vm);
}

JNIEXPORT jboolean JNICALL Java_com_newdawn_launcher_EngineLogin_nativeInstall(JNIEnv *env, jclass cls) {
    if (installed) { memset(auth_token, 0, 44); modal = 1; loading = 0; return JNI_TRUE; }
    (*env)->GetJavaVM(env, &vm); java_login = (*env)->NewGlobalRef(env, cls);
#define JAVA_ID(name, signature) (*env)->GetStaticMethodID(env, cls, name, signature)
    j_submit = JAVA_ID("submit", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Z)V");
    j_continue = JAVA_ID("continueSaved", "()V"); j_busy = JAVA_ID("isBusy", "()Z");
    j_message = JAVA_ID("message", "()Ljava/lang/String;"); j_token = JAVA_ID("takeToken", "()Ljava/lang/String;");
    j_remember = JAVA_ID("rememberedUsername", "()Ljava/lang/String;"); j_saved = JAVA_ID("hasSavedSession", "()Z");
    j_forget = JAVA_ID("forget", "()V"); j_diagnostics = JAVA_ID("diagnostics", "()V"); j_quit = JAVA_ID("quit", "()V");
    j_account_failed = JAVA_ID("accountFailed", "()V"); j_start_failed = JAVA_ID("startFailure", "(Landroid/app/Activity;)V");
    if ((*env)->ExceptionCheck(env)) { (*env)->ExceptionClear(env); return JNI_FALSE; }
    void *nd = dlopen("libnd.so", RTLD_NOW | RTLD_NOLOAD); Dl_info nd_info, game_info;
    void *nd_prepare = nd ? dlsym(nd, "Java_com_newdawn_launcher_NativeRuntime_prepare") : NULL;
    library = dlopen("libil2cpp.so", RTLD_NOW | RTLD_NOLOAD);
    if (!nd_prepare || !library || !dladdr(nd_prepare, &nd_info) || !dladdr(dlsym(library, "il2cpp_init"), &game_info)) return JNI_FALSE;
    auth_token = (char *)nd_info.dli_fbase + ND_TOKEN_OFFSET;
    memset(auth_token, 0, 44);
    base = (uintptr_t)game_info.dli_fbase;
#define API(name, symbol) name = dlsym(library, symbol); if (!name) return JNI_FALSE
    API(invoke, "il2cpp_runtime_invoke"); API(string_new, "il2cpp_string_new");
    API(object_new, "il2cpp_object_new"); API(object_class, "il2cpp_object_get_class");
    API(method_find, "il2cpp_class_get_method_from_name"); API(unbox, "il2cpp_object_unbox");
    API(root_new, "il2cpp_gchandle_new"); API(root_target, "il2cpp_gchandle_get_target"); API(root_free, "il2cpp_gchandle_free");
    API(string_chars, "il2cpp_string_chars"); API(string_length, "il2cpp_string_length");
    if (!br_hook_install((void *)(base + START_OFFSET), START_PROLOGUE, title_start, (void **)&original_start)
        || !br_hook_install((void *)(base + ACCOUNTS_OFFSET), ACCOUNTS_PROLOGUE, account_request, (void **)&original_accounts)
        || !br_hook_install((void *)(base + UPDATED_OFFSET), UPDATED_PROLOGUE, account_updated, (void **)&original_updated)
        || !br_hook_install((void *)(base + CAMERA_OFFSET), CAMERA_PROLOGUE, camera_update, (void **)&original_camera)
        || !br_hook_install((void *)(base + CONFIRM_OFFSET), CONFIRM_PROLOGUE, confirm, (void **)&original_confirm)
        || !br_hook_install((void *)(base + GUI_OFFSET), GUI_PROLOGUE, on_gui, &unused_gui)) return JNI_FALSE;
    installed = 1; br_stage("ENGINE_LOGIN_HOOKS_READY"); return JNI_TRUE;
}
