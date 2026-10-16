/* Authentication adapter for the existing Unity 6 / OffServer request bridge. */
#include <jni.h>
#include <android/log.h>
#include <dlfcn.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

#define GATEWAY "http://179.197.72.129:8190"
#define DBR_BLOB_OFFSET 0x180300u
#define DBR_SEND_POINTER 0x550ce8u
#define DBR_HOOK_CODE 0x49b944u
#define DBR_TICK_FRAMES 0x4bfaa0u

typedef struct { void *klass; void *monitor; int32_t length; uint16_t chars[1]; } ManagedString;
typedef const void *Method;
typedef void *(*SendRequest)(void *, const void *);
static SendRequest original_send;
static pthread_mutex_t send_lock = PTHREAD_MUTEX_INITIALIZER;
static char auth_token[44];
static pthread_mutex_t token_lock = PTHREAD_MUTEX_INITIALIZER;
static int installed;
static Method get_uri, absolute_uri, add_field;
static void *(*runtime_invoke)(Method, void *, void **, void **);
static ManagedString *(*new_string)(const char *);

static int valid_token(const char *text) {
    if (!text || strlen(text) != 43) return 0;
    for (int i = 0; i < 43; i++) {
        char c = text[i];
        if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
            (c >= '0' && c <= '9') || c == '_' || c == '-')) return 0;
    }
    return 1;
}

static int authenticated_route(const char *url) {
    size_t prefix = sizeof(GATEWAY) - 1;
    if (!url || strncmp(url, GATEWAY, prefix) != 0) return 0;
    const char *route = url + prefix;
    const char *paths[] = { "/accounts", "/sessions" };
    for (int i = 0; i < 2; i++) {
        size_t length = strlen(paths[i]);
        if (strncmp(route, paths[i], length) == 0 &&
            (route[length] == 0 || route[length] == '?')) return 1;
    }
    return 0;
}

static int to_ascii(ManagedString *text, char *out, size_t capacity) {
    if (!text || text->length <= 0 || (size_t)text->length >= capacity) return 0;
    for (int i = 0; i < text->length; i++) {
        if (text->chars[i] > 127) return 0;
        out[i] = (char)text->chars[i];
    }
    out[text->length] = 0;
    return 1;
}

static void authorize(void *request) {
    if (!request || !get_uri || !absolute_uri || !add_field) return;
    void *exception = NULL;
    void *uri = runtime_invoke(get_uri, request, NULL, &exception);
    if (exception || !uri) return;
    ManagedString *url = runtime_invoke(absolute_uri, uri, NULL, &exception);
    char address[2048];
    if (exception || !to_ascii(url, address, sizeof(address)) || !authenticated_route(address)) return;
    char token[44];
    pthread_mutex_lock(&token_lock);
    memcpy(token, auth_token, sizeof(token));
    pthread_mutex_unlock(&token_lock);
    if (!valid_token(token)) return;
    void *args[] = { new_string("token"), new_string(token) };
    runtime_invoke(add_field, request, args, &exception);
    memset(token, 0, sizeof(token));
    /* Do not expose token values, URI query strings, account IDs or credentials in logs. */
    if (exception) __android_log_print(ANDROID_LOG_ERROR, "DurangoBR", "Falha ao anexar autenticacao.");
}

static void *authenticated_send(void *request, const void *method) {
    authorize(request);
    pthread_mutex_lock(&send_lock);
    SendRequest forward = original_send;
    pthread_mutex_unlock(&send_lock);
    return forward ? forward(request, method) : NULL;
}

/* Only suppress media operations made by the title screen. Other cutscenes
 * retain their original player and initialization. */
static __thread int title_video_context;
static SendRequest original_title_apply, original_media_init, original_media_play;
static Method title_video_fallback, streaming_assets_path;
static void *(*original_media_load)(void *,void *,const void *);
static void *apply_title_without_video(void *object,const void *method) {
    title_video_context++;
    void *result=original_title_apply(object,method);
    title_video_context--;
    if (title_video_fallback && streaming_assets_path) {
        void *exception=NULL;
        ManagedString *root=runtime_invoke(streaming_assets_path,NULL,NULL,&exception);
        char path[2048],url[2100];
        if (!exception && to_ascii(root,path,sizeof(path))) {
            snprintf(url,sizeof(url),"%s/Movie/Mobile/title.mp4",path);
            void *args[]={new_string(url)};
            runtime_invoke(title_video_fallback,object,args,&exception);
            if (exception) __android_log_print(ANDROID_LOG_ERROR,"DurangoBR","Falha no video da selecao.");
        }
    }
    return result;
}
static void *media_init(void *object,const void *method) {
    return title_video_context?NULL:original_media_init(object,method);
}
static void *media_play(void *object,const void *method) {
    return title_video_context?NULL:original_media_play(object,method);
}
static void *media_load(void *object,void *url,const void *method) {
    return title_video_context?NULL:original_media_load(object,url,method);
}

static void *find_class(void *handle, const char *space, const char *name) {
    void *(*domain_get)(void) = dlsym(handle, "il2cpp_domain_get");
    void **(*assemblies)(void *, size_t *) = dlsym(handle, "il2cpp_domain_get_assemblies");
    void *(*image)(void *) = dlsym(handle, "il2cpp_assembly_get_image");
    void *(*from_name)(void *, const char *, const char *) = dlsym(handle, "il2cpp_class_from_name");
    if (!domain_get || !assemblies || !image || !from_name || !domain_get()) return NULL;
    size_t count = 0;
    void **all = assemblies(domain_get(), &count);
    for (size_t i = 0; i < count; i++) {
        void *klass = from_name(image(all[i]), space, name);
        if (klass) return klass;
    }
    return NULL;
}

static void *engine_handle;
static unsigned char *community_base;
static uintptr_t (*original_init)(const char *);
static pthread_mutex_t init_lock = PTHREAD_MUTEX_INITIALIZER;

static char compatibility_shader_path[2048];
static void *(*original_find_shader)(ManagedString *,void *,const void *);
static Method bundle_load_file, bundle_load_asset, shader_supported;
static void *shader_type;
static uint32_t compatibility_bundle_handle, compatibility_shader_handle;
static uint32_t (*gc_handle_new)(void *,int);
static void *(*gc_handle_target)(uint32_t);
static int shader_load_attempted;

static void *find_compatible_shader(ManagedString *name,void *keywords,const void *method) {
    char text[128];
    if (!to_ascii(name,text,sizeof(text)) || strcmp(text,"Durango/Building/Floor2AlphaUV")!=0)
        return original_find_shader(name,keywords,method);
    if (compatibility_shader_handle) return gc_handle_target(compatibility_shader_handle);
    if (!shader_load_attempted && compatibility_shader_path[0]) {
        shader_load_attempted=1;
        void *exception=NULL;
        void *args[]={new_string(compatibility_shader_path)};
        void *bundle=runtime_invoke(bundle_load_file,NULL,args,&exception);
        if (!exception && bundle) {
            compatibility_bundle_handle=gc_handle_new(bundle,0);
            void *load_args[]={new_string("assets/resources/shaders/floor2alphauv.shader"),shader_type};
            void *shader=runtime_invoke(bundle_load_asset,bundle,load_args,&exception);
            if (!exception && shader) {
                void *supported=runtime_invoke(shader_supported,shader,NULL,&exception);
                if (!exception && supported && *((uint8_t *)supported+2*sizeof(void *))) {
                    compatibility_shader_handle=gc_handle_new(shader,0);
                    __android_log_print(ANDROID_LOG_INFO,"DurangoBR","Shader do piso carregado e compativel com a GPU.");
                    return shader;
                }
            }
        }
        __android_log_print(ANDROID_LOG_ERROR,"DurangoBR","Shader compativel do piso indisponivel.");
    }
    return original_find_shader(name,keywords,method);
}

static void describe_runtime_class(void *klass,const char *label) {
    Method (*next)(void *,void **)=dlsym(engine_handle,"il2cpp_class_get_methods");
    const char *(*name)(Method)=dlsym(engine_handle,"il2cpp_method_get_name");
    uint32_t (*count)(Method)=dlsym(engine_handle,"il2cpp_method_get_param_count");
    if (!klass || !next || !name || !count) { __android_log_print(ANDROID_LOG_INFO,"DurangoBR","Classe ausente: %s",label); return; }
    void *iter=NULL; Method item;
    while ((item=next(klass,&iter))) {
        const char *n=name(item);
        if (strstr(n,"Init") || strstr(n,"Play") || strstr(n,"Load") || strstr(n,"Find") || strstr(n,"Fix") || strstr(n,"Hide") || strstr(n,"Close") || strstr(n,"Show") || strstr(n,"Curta") || strstr(n,"Ready") || strstr(n,"Appear") || strstr(n,"Disappear"))
            __android_log_print(ANDROID_LOG_INFO,"DurangoBR","API %s.%s (%u)",label,n,count(item));
    }
}

/* Called on Unity's own initialized VM thread, before scenes and HTTP requests. */
static int install_bindings(void) {
    if (__atomic_load_n(&installed,__ATOMIC_ACQUIRE)) return 1;
    void *http=find_class(engine_handle,"BestHTTP","HTTPRequest");
    void *uri=find_class(engine_handle,"System","Uri");
    Method (*method)(void *,const char *,int)=dlsym(engine_handle,"il2cpp_class_get_method_from_name");
    runtime_invoke=dlsym(engine_handle,"il2cpp_runtime_invoke");
    new_string=dlsym(engine_handle,"il2cpp_string_new");
    if (!http || !uri || !method || !runtime_invoke || !new_string) return 0;
    get_uri=method(http,"get_Uri",0);
    if (!get_uri) get_uri=method(http,"get_CurrentUri",0);
    absolute_uri=method(uri,"get_AbsoluteUri",0); add_field=method(http,"AddField",2);
    if (!get_uri || !absolute_uri || !add_field) return 0;
    void *(*hook_code)(void *,void *)=(void *(*)(void *,void *))(community_base+DBR_HOOK_CODE);
    SendRequest *slot=(SendRequest *)(community_base+DBR_SEND_POINTER);
    SendRequest prior=__atomic_load_n(slot,__ATOMIC_ACQUIRE);
    int success=0;
    pthread_mutex_lock(&send_lock);
    if (prior) {
        original_send=prior;
        success=__atomic_compare_exchange_n(slot,&prior,authenticated_send,0,__ATOMIC_RELEASE,__ATOMIC_ACQUIRE);
    } else {
        void *manager=find_class(engine_handle,"BestHTTP","HTTPManager");
        Method send=manager?method(manager,"SendRequest",1):NULL;
        if (send && *(void *const *)send) {
            original_send=hook_code(*(void *const *)send,authenticated_send);
            success=original_send!=NULL;
        }
    }
    pthread_mutex_unlock(&send_lock);
    if (!success) return 0;
    void *ui=find_class(engine_handle,"Durango.UI","TitleMenuGroup");
    title_video_fallback=ui?method(ui,"PlayTitleVideoFallback",1):NULL;
    void *application=find_class(engine_handle,"UnityEngine","Application");
    streaming_assets_path=application?method(application,"get_streamingAssetsPath",0):NULL;
    void *media=find_class(engine_handle,"","MediaPlayerCtrl");
    Method apply=ui?method(ui,"ApplyEmigrationMode",0):NULL;
    Method init=media?method(media,"InitPlayer",0):NULL;
    Method play=media?method(media,"Play",0):NULL;
    Method load=media?method(media,"Load",1):NULL;
    if (apply && init && play && load) {
        original_media_init=hook_code(*(void *const *)init,media_init);
        original_media_play=hook_code(*(void *const *)play,media_play);
        original_media_load=hook_code(*(void *const *)load,media_load);
        if (original_media_init && original_media_play && original_media_load) {
            original_title_apply=hook_code(*(void *const *)apply,apply_title_without_video);
            if (original_title_apply)
                __android_log_print(ANDROID_LOG_INFO,"DurangoBR","Video da selecao preparado com Unity VideoPlayer.");
        }
    }
    void *remap=find_class(engine_handle,"","OffServerShaderRemap");
    void *bundle_class=find_class(engine_handle,"UnityEngine","AssetBundle");
    void *shader_class=find_class(engine_handle,"UnityEngine","Shader");
    Method find=remap?method(remap,"FindProjectShader",2):NULL;
    bundle_load_file=bundle_class?method(bundle_class,"LoadFromFile",1):NULL;
    bundle_load_asset=bundle_class?method(bundle_class,"LoadAsset",2):NULL;
    shader_supported=shader_class?method(shader_class,"get_isSupported",0):NULL;
    const void *(*class_type)(void *)=dlsym(engine_handle,"il2cpp_class_get_type");
    void *(*type_object)(const void *)=dlsym(engine_handle,"il2cpp_type_get_object");
    gc_handle_new=dlsym(engine_handle,"il2cpp_gchandle_new");
    gc_handle_target=dlsym(engine_handle,"il2cpp_gchandle_get_target");
    if (find && bundle_load_file && bundle_load_asset && shader_supported && class_type && type_object && gc_handle_new && gc_handle_target) {
        shader_type=type_object(class_type(shader_class));
        original_find_shader=hook_code(*(void *const *)find,find_compatible_shader);
        if (original_find_shader) __android_log_print(ANDROID_LOG_INFO,"DurangoBR","Correcao do shader de piso preparada.");
    }
    describe_runtime_class(media,"MediaPlayerCtrl");
    describe_runtime_class(remap,"ShaderRemap");
    describe_runtime_class(bundle_class,"AssetBundle");
    describe_runtime_class(shader_class,"Shader");
    describe_runtime_class(find_class(engine_handle,"Durango.UI","LoadingCurtainGroup"),"LoadingCurtainGroup");
    describe_runtime_class(find_class(engine_handle,"Durango.UI","LoadingCurtainBase"),"LoadingCurtainBase");
    describe_runtime_class(find_class(engine_handle,"Durango.Model","CostumableModel"),"CostumableModel");
    __android_log_print(ANDROID_LOG_INFO,"DurangoBR","API shader: find=%d file=%d asset=%d supported=%d type=%d object=%d gc=%d target=%d",find!=NULL,bundle_load_file!=NULL,bundle_load_asset!=NULL,shader_supported!=NULL,class_type!=NULL,type_object!=NULL,gc_handle_new!=NULL,gc_handle_target!=NULL);
    __atomic_store_n(&installed,1,__ATOMIC_RELEASE);
    __android_log_print(ANDROID_LOG_INFO,"DurangoBR","Ponte de autenticacao Unity 6 pronta.");
    return 1;
}

static uintptr_t initialized_runtime(const char *domain_name) {
    pthread_mutex_lock(&init_lock);
    uintptr_t (*forward)(const char *)=original_init;
    pthread_mutex_unlock(&init_lock);
    uintptr_t result=forward(domain_name);
    __android_log_print(ANDROID_LOG_INFO,"DurangoBR","Runtime Unity iniciado; preparando conexao.");
    install_bindings();
    return result;
}

static void *install_adapter(void *unused) {
    (void)unused;
    for (int attempt=0; attempt<30000; attempt++) {
        void *dbr=dlopen("libdbr.so",RTLD_NOW|RTLD_NOLOAD);
        void *il2cpp=dlopen("libil2cpp.so",RTLD_NOW|RTLD_NOLOAD);
        if (!dbr || !il2cpp) {
            if (dbr) dlclose(dbr); if (il2cpp) dlclose(il2cpp);
            usleep(2000); continue;
        }
        unsigned char *blob=dlsym(dbr,"thpt_blob");
        if (!blob) { dlclose(dbr); dlclose(il2cpp); return NULL; }
        community_base=blob-DBR_BLOB_OFFSET; engine_handle=il2cpp;
        uint32_t frames=__atomic_load_n((uint32_t *)(community_base+DBR_TICK_FRAMES),__ATOMIC_ACQUIRE);
        if (frames==0) {
            void *init=dlsym(il2cpp,"il2cpp_init");
            void *(*hook_code)(void *,void *)=(void *(*)(void *,void *))(community_base+DBR_HOOK_CODE);
            pthread_mutex_lock(&init_lock);
            original_init=init?hook_code(init,initialized_runtime):NULL;
            pthread_mutex_unlock(&init_lock);
            if (original_init) {
                __android_log_print(ANDROID_LOG_INFO,"DurangoBR","Inicializacao Unity monitorada.");
                return NULL;
            }
        } else {
            void *(*domain_get)(void)=dlsym(il2cpp,"il2cpp_domain_get");
            void *(*attach)(void *)=dlsym(il2cpp,"il2cpp_thread_attach");
            void (*detach)(void *)=dlsym(il2cpp,"il2cpp_thread_detach");
            void *domain=domain_get?domain_get():NULL;
            void *thread=domain && attach && detach?attach(domain):NULL;
            if (thread) { int ready=install_bindings(); detach(thread); if (ready) return NULL; }
        }
        dlclose(dbr); dlclose(il2cpp); usleep(2000);
    }
    __android_log_print(ANDROID_LOG_ERROR,"DurangoBR","Ponte de autenticacao nao inicializada.");
    return NULL;
}

JNIEXPORT void JNICALL Java_com_newdawn_launcher_BrazilUnityBridge_configure(JNIEnv *env, jclass klass, jstring value) {
    (void)klass;
    const char *token = value ? (*env)->GetStringUTFChars(env, value, NULL) : NULL;
    pthread_mutex_lock(&token_lock);
    memset(auth_token, 0, sizeof(auth_token));
    if (valid_token(token)) memcpy(auth_token, token, 43);
    pthread_mutex_unlock(&token_lock);
    if (token) (*env)->ReleaseStringUTFChars(env, value, token);
}

JNIEXPORT void JNICALL Java_com_newdawn_launcher_BrazilUnityBridge_configureShaderPath(JNIEnv *env,jclass klass,jstring value) {
    (void)klass;
    const char *path=value?(*env)->GetStringUTFChars(env,value,NULL):NULL;
    compatibility_shader_path[0]=0;
    if (path && strlen(path)<sizeof(compatibility_shader_path))
        strcpy(compatibility_shader_path,path);
    if (path) (*env)->ReleaseStringUTFChars(env,value,path);
}

JNIEXPORT jboolean JNICALL Java_com_newdawn_launcher_BrazilUnityBridge_ready(JNIEnv *env, jclass klass) {
    (void)env; (void)klass;
    return __atomic_load_n(&installed, __ATOMIC_ACQUIRE) ? JNI_TRUE : JNI_FALSE;
}

__attribute__((constructor)) static void start_adapter(void) {
    __android_log_print(ANDROID_LOG_INFO, "DurangoBR", "Adaptador brasileiro carregado.");
    pthread_t thread;
    if (pthread_create(&thread, NULL, install_adapter, NULL) == 0) pthread_detach(thread);
}
