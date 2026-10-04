/* Original Unity 2017 ARM64. Own-source authentication, installed before Unity
 * starts. Managed APIs are called only from the game's existing managed Send
 * thread; there is no polling worker, fixed startup sleep or init interception. */
#define _GNU_SOURCE
#include "runtime_compat.h"
#include "runtime_math.h"
#include <jni.h>
#include <android/log.h>
#include <dlfcn.h>
#include <sys/mman.h>
#include <unistd.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <errno.h>
#define LOG(...) __android_log_print(ANDROID_LOG_INFO,"LHRuntime",__VA_ARGS__)
#define GATEWAY "http://179.197.72.129:8190"
typedef struct {void *klass,*monitor;int32_t length;uint16_t chars[];} ManagedString;
static char token[44], directory[1024];
static int prepared, safe_profile, disable_title_video;
static uintptr_t base;
static void *library;
static pthread_mutex_t prepare_lock=PTHREAD_MUTEX_INITIALIZER;
static pthread_mutex_t bind_lock=PTHREAD_MUTEX_INITIALIZER;
static pthread_mutex_t stage_lock=PTHREAD_MUTEX_INITIALIZER;
static void *(*original_send)(void *,const void *);
static void (*original_callback)(void *,const void *);
static void (*original_title)(void *,const void *);
static void (*original_media_init)(void *,const void *);
static void (*original_media_play)(void *,const void *);
static void (*original_media_load)(void *,void *,const void *);
static __thread int title_context;
static void *(*invoke)(const void *,void *,void **,void **);
static void *(*string_new)(const char *);
static const void *uri_get,*uri_absolute,*add_field;
static const void *connect_timeout,*request_timeout,*disable_retry,*request_state,*request_response;
static int binding_state;
static uint32_t title_media_handle;
static JavaVM *vm;
static jclass java_runtime;
static jmethodID java_failure;
static void notify_failure(void) {
    if(!vm||!java_runtime||!java_failure)return;
    JNIEnv *env=NULL;int detached=0;
    if((*vm)->GetEnv(vm,(void **)&env,JNI_VERSION_1_6)==JNI_EDETACHED){
        if((*vm)->AttachCurrentThread(vm,&env,NULL)!=JNI_OK)return;detached=1;
    }
    if(env){(*env)->CallStaticVoidMethod(env,java_runtime,java_failure);if((*env)->ExceptionCheck(env))(*env)->ExceptionClear(env);}
    if(detached)(*vm)->DetachCurrentThread(vm);
}
static int callable(const void *method) { return method&&((void *const *)method)[0]&&((void *const *)method)[1]; }

void br_stage(const char *stage) {
    if(!directory[0])return;
    pthread_mutex_lock(&stage_lock);
    char path[1200];snprintf(path,sizeof(path),"%s/runtime-stage.txt",directory);
    FILE *f=fopen(path,"w");
    if(f){fprintf(f,"%ld %ld %s\n",(long)time(NULL),(long)getpid(),stage);fclose(f);}
    snprintf(path,sizeof(path),"%s/runtime-events.txt",directory);
    f=fopen(path,"a");
    if(f){if(ftell(f)>32768){fclose(f);f=fopen(path,"w");}
        if(f){fprintf(f,"%ld %s\n",(long)time(NULL),stage);fclose(f);}}
    pthread_mutex_unlock(&stage_lock);
    LOG("Etapa: %s",stage);
}
int br_safe_profile(void){return safe_profile;}

/* ADRP uses architectural 4096-byte pages, regardless of the kernel page size.
 * Relocate ADR/ADRP into an absolute literal. Reject all other PC-relative
 * instructions rather than copy a branch into a wrong trampoline address. */
int br_hook_install(void *entry,const unsigned char expected[16],void *replacement,void **original) {
    if(!entry||!replacement||!original||memcmp(entry,expected,16))return 0;
    long size=sysconf(_SC_PAGESIZE);
    if(size<4096 || (size&(size-1)))return 0;
    unsigned char *stub=mmap(NULL,(size_t)size,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS,-1,0);
    if(stub==MAP_FAILED)return 0;
    for(int i=0;i<4;i++){
        uint32_t input,output;uintptr_t literal=0;memcpy(&input,(char *)entry+i*4,4);
        if(!lh_relocate(input,(uintptr_t)entry+i*4,&output,&literal,i)){munmap(stub,(size_t)size);return 0;}
        memcpy(stub+i*4,&output,4);memcpy(stub+64+i*8,&literal,8);
    }
    uint32_t jump[]={0x58000051u,0xd61f0220u};void *continuation=(char *)entry+16;
    memcpy(stub+16,jump,8);memcpy(stub+24,&continuation,8);
    __builtin___clear_cache((char *)stub,(char *)stub+96);
    if(mprotect(stub,(size_t)size,PROT_READ|PROT_EXEC)){munmap(stub,(size_t)size);return 0;}
    uintptr_t start;size_t span;
    if(!lh_span((uintptr_t)entry,16,(size_t)size,&start,&span)){munmap(stub,(size_t)size);return 0;}
    if(mprotect((void *)start,span,PROT_READ|PROT_WRITE|PROT_EXEC)){munmap(stub,(size_t)size);return 0;}
    unsigned char patch[16];memcpy(patch,jump,8);memcpy(patch+8,&replacement,8);
    *original=stub;memcpy(entry,patch,16);__builtin___clear_cache(entry,(char *)entry+16);
    if(mprotect((void *)start,span,PROT_READ|PROT_EXEC)){
        memcpy(entry,expected,16);__builtin___clear_cache(entry,(char *)entry+16);
        /* If RX restoration failed, make a second attempt after rollback. */
        if(mprotect((void *)start,span,PROT_READ|PROT_EXEC))br_stage("MEMORY_PROTECTION_FAILED");
        *original=NULL;munmap(stub,(size_t)size);return 0;
    }
    return 1;
}

static void *find_class(const char *space,const char *name) {
    void *(*domain)(void)=dlsym(library,"il2cpp_domain_get");
    const void **(*assemblies)(void *,size_t *)=dlsym(library,"il2cpp_domain_get_assemblies");
    void *(*image)(const void *)=dlsym(library,"il2cpp_assembly_get_image");
    void *(*find)(void *,const char *,const char *)=dlsym(library,"il2cpp_class_from_name");
    if(!domain||!assemblies||!image||!find)return NULL;
    void *d=domain();if(!d)return NULL;
    size_t count=0;const void **all=assemblies(d,&count);if(!all)return NULL;
    for(size_t i=0;i<count;i++){void *c=find(image(all[i]),space,name);if(c)return c;}
    return NULL;
}
static int bind(void) {
    pthread_mutex_lock(&bind_lock);
    if(binding_state){int ok=binding_state>0;pthread_mutex_unlock(&bind_lock);return ok;}
    const void *(*method)(void *,const char *,int)=dlsym(library,"il2cpp_class_get_method_from_name");
    void *http=find_class("BestHTTP","HTTPRequest"),*uri=find_class("System","Uri");
    invoke=dlsym(library,"il2cpp_runtime_invoke");string_new=dlsym(library,"il2cpp_string_new");
    if(method&&http&&uri&&invoke&&string_new){
        uri_get=method(http,"get_Uri",0);if(!uri_get)uri_get=method(http,"get_CurrentUri",0);
        uri_absolute=method(uri,"get_AbsoluteUri",0);add_field=method(http,"AddField",2);
        connect_timeout=method(http,"set_ConnectTimeout",1);request_timeout=method(http,"set_Timeout",1);
        disable_retry=method(http,"set_DisableRetry",1);request_state=method(http,"get_State",0);
        request_response=method(http,"get_Response",0);
    }
    binding_state=callable(uri_get)&&callable(uri_absolute)&&callable(add_field)?1:-1;
    br_stage(binding_state>0?"AUTH_READY":"AUTH_BINDING_FAILED");
    int ok=binding_state>0;pthread_mutex_unlock(&bind_lock);return ok;
}
static int ascii(void *value,char *out,size_t capacity) {
    ManagedString *s=value;if(!s||s->length<0||(size_t)s->length>=capacity)return 0;
    for(int i=0;i<s->length;i++){if(s->chars[i]>127)return 0;out[i]=(char)s->chars[i];}
    out[s->length]=0;return 1;
}
static int route(const char *address) {
    return lh_auth_route(address);
}
static int request_route(void *request) {
    if(!request||!bind())return 0;
    void *exception=NULL,*uri=invoke(uri_get,request,NULL,&exception);
    if(exception||!uri)return 0;
    void *value=invoke(uri_absolute,uri,NULL,&exception);char address[2048];
    if(exception||!ascii(value,address,sizeof(address))||!route(address))return 0;
    return !strncmp(address+sizeof(GATEWAY)-1,"/accounts",9)?1:2;
}
static void request_callback(void *request,const void *method_info) {
    int kind=request_route(request);
    if(kind){
        br_stage(kind==1?"ACCOUNT_CALLBACK_ENTER":"SESSION_CALLBACK_ENTER");
        if(callable(request_state)){
            void *exception=NULL,*state=invoke(request_state,request,NULL,&exception);
            /* Use the exported unbox API for enum payloads. */
            void *(*unbox)(void *)=dlsym(library,"il2cpp_object_unbox");
            if(!exception&&state&&unbox){int32_t *value=unbox(state);char stage[64];
                if(value&&*value>=0&&*value<=7){snprintf(stage,sizeof(stage),"%s_STATE_%d",kind==1?"ACCOUNT":"SESSION",*value);br_stage(stage);}}
        }
        if(callable(request_response)){
            void *exception=NULL,*response=invoke(request_response,request,NULL,&exception);
            if(!exception&&response)br_stage(kind==1?"ACCOUNT_RESPONSE_RECEIVED":"SESSION_RESPONSE_RECEIVED");
        }
    }
    original_callback(request,method_info);
    if(kind)br_stage(kind==1?"ACCOUNT_CALLBACK_RETURN":"SESSION_CALLBACK_RETURN");
}
static void *send_request(void *request,const void *method_info) {
    if(!request)return NULL;
    if(!bind()){notify_failure();return NULL;}
    void *exception=NULL,*uri=invoke(uri_get,request,NULL,&exception);
    if(exception||!uri){br_stage("AUTH_URI_FAILED");notify_failure();return NULL;}
    void *value=invoke(uri_absolute,uri,NULL,&exception);char address[2048];
    if(exception||!ascii(value,address,sizeof(address))){br_stage("AUTH_URI_FAILED");notify_failure();return NULL;}
    if(route(address)){
        void *key=string_new("token"),*credential=string_new(token);
        if(!key||!credential){br_stage("AUTH_ALLOCATION_FAILED");notify_failure();return NULL;}
        void *args[]={key,credential};invoke(add_field,request,args,&exception);
        if(exception){br_stage("AUTH_FIELD_FAILED");notify_failure();return NULL;}
        /* Bound only authentication requests. TimeSpan is a single Int64 ticks
         * value in this client; runtime_invoke takes its payload address.
         * Keep the existing async HTTP worker and game error callback. */
        if(callable(connect_timeout)&&callable(request_timeout)&&callable(disable_retry)){
            int64_t connect_ticks=10LL*10000000,timeout_ticks=20LL*10000000;uint8_t retry=1;
            void *connect_args[]={&connect_ticks},*timeout_args[]={&timeout_ticks},*retry_args[]={&retry};
            invoke(connect_timeout,request,connect_args,&exception);
            if(!exception)invoke(request_timeout,request,timeout_args,&exception);
            if(!exception)invoke(disable_retry,request,retry_args,&exception);
            if(exception){br_stage("AUTH_TIMEOUT_CONFIGURATION_FAILED");notify_failure();return NULL;}
        }else br_stage("AUTH_TIMEOUT_ORIGINAL_FALLBACK");
        br_stage(strstr(address+sizeof(GATEWAY)-1,"/accounts")==address+sizeof(GATEWAY)-1?"ACCOUNT_REQUEST":"SESSION_REQUEST");
    }
    int kind=route(address)?(!strncmp(address+sizeof(GATEWAY)-1,"/accounts",9)?1:2):0;
    void *result=original_send(request,method_info);
    if(kind)br_stage(kind==1?"ACCOUNT_SEND_RETURN":"SESSION_SEND_RETURN");
    return result;
}
static int skip_title_media(void *self) {
    if(!disable_title_video)return 0;
    uint32_t (*handle_new)(void *,uint8_t)=dlsym(library,"il2cpp_gchandle_new");
    void *(*handle_target)(uint32_t)=dlsym(library,"il2cpp_gchandle_get_target");
    if(title_context){
        if(!title_media_handle&&handle_new)title_media_handle=handle_new(self,0);
        return 1;
    }
    /* Keep this one title component alive so a later scene cannot reuse its
     * managed address. Prologue/map/cutscene players remain untouched. */
    return title_media_handle&&handle_target&&handle_target(title_media_handle)==self;
}
static void media_init(void *self,const void *m){if(!skip_title_media(self))original_media_init(self,m);}
static void media_play(void *self,const void *m){if(!skip_title_media(self))original_media_play(self,m);}
static void media_load(void *self,void *url,const void *m){if(!skip_title_media(self))original_media_load(self,url,m);}
static void record_graphics(void) {
    const void *(*method)(void *,const char *,int)=dlsym(library,"il2cpp_class_get_method_from_name");
    void *(*call)(const void *,void *,void **,void **)=dlsym(library,"il2cpp_runtime_invoke");
    void *klass=find_class("UnityEngine","SystemInfo");
    if(!method||!call||!klass||!directory[0])return;
    char path[1200];snprintf(path,sizeof(path),"%s/graphics-info.txt",directory);
    FILE *f=fopen(path,"w");if(!f)return;
    const char *names[]={"get_graphicsDeviceName","get_graphicsDeviceVendor","get_graphicsDeviceVersion"};
    for(int i=0;i<3;i++){
        const void *m=method(klass,names[i],0);void *exception=NULL;
        void *value=callable(m)?call(m,NULL,NULL,&exception):NULL;char text[256];
        if(!exception&&ascii(value,text,sizeof(text)))fprintf(f,"%s: %s\n",names[i],text);
    }
    fclose(f);
}
static void apply_profile(void) {
    if(!safe_profile)return;
    /* Unity icalls on the title's Unity thread, after the graphics subsystem is
     * alive. Keep original shaders and choose a conservative resolution/FPS. */
    void *(*resolve)(const char *)=dlsym(library,"il2cpp_resolve_icall");
    if(!resolve)return;
    int (*width)(void)=resolve("UnityEngine.Screen::get_width()");
    int (*height)(void)=resolve("UnityEngine.Screen::get_height()");
    void (*resolution)(int,int,uint8_t,int)=resolve("UnityEngine.Screen::SetResolution(System.Int32,System.Int32,System.Boolean,System.Int32)");
    void (*fps)(int)=resolve("UnityEngine.Application::set_targetFrameRate(System.Int32)");
    if(fps){fps(30);br_stage("SAFE_FPS_REQUESTED");}
    if(width&&height&&resolution){int w=width(),h=height();
        if(w>0&&h>0){int longest=w>h?w:h;if(longest>1280){resolution(w*1280/longest,h*1280/longest,1,30);br_stage("SAFE_RESOLUTION_APPLIED");}}}
}
static void title(void *self,const void *m) {
    br_stage(disable_title_video?"TITLE_VIDEO_DISABLED":"TITLE_VIDEO");
    record_graphics();
    apply_profile();int previous=title_context;title_context=disable_title_video;
    original_title(self,m);title_context=previous;
}

JNIEXPORT jboolean JNICALL Java_com_newdawn_launcher_NativeRuntime_prepare(JNIEnv *env,jclass cls,jstring ticket,jstring folder,jboolean compatible,jboolean disable_video) {
    pthread_mutex_lock(&prepare_lock);
    if(prepared){pthread_mutex_unlock(&prepare_lock);return JNI_TRUE;}
    const char *t=ticket?(*env)->GetStringUTFChars(env,ticket,NULL):NULL;
    const char *d=folder?(*env)->GetStringUTFChars(env,folder,NULL):NULL;
    int valid=lh_token_valid(t)&&d&&strlen(d)<sizeof(directory);
    if(valid){memcpy(token,t,44);snprintf(directory,sizeof(directory),"%s",d);}
    if(t)(*env)->ReleaseStringUTFChars(env,ticket,t);if(d)(*env)->ReleaseStringUTFChars(env,folder,d);
    safe_profile=compatible;
    disable_title_video=disable_video;
    if(!valid){pthread_mutex_unlock(&prepare_lock);return JNI_FALSE;}
    if(!java_runtime){
        (*env)->GetJavaVM(env,&vm);
        java_runtime=(*env)->NewGlobalRef(env,cls);
        java_failure=(*env)->GetStaticMethodID(env,cls,"onFailure","()V");
        if((*env)->ExceptionCheck(env)){(*env)->ExceptionClear(env);java_failure=NULL;}
    }
    br_stage("NATIVE_LOADING");library=dlopen("libil2cpp.so",RTLD_NOW);
    Dl_info info;void *init=library?dlsym(library,"il2cpp_init"):NULL;
    if(!init||!dladdr(init,&info)){br_stage("NATIVE_LOAD_FAILED");pthread_mutex_unlock(&prepare_lock);return JNI_FALSE;}
    base=(uintptr_t)info.dli_fbase;
    static const unsigned char callback_bytes[16]={0xf6,0x57,0xbd,0xa9,0xf4,0x4f,0x01,0xa9,0xfd,0x7b,0x02,0xa9,0xfd,0x83,0x00,0x91};
    if(!original_callback&&!br_hook_install((void *)(base+0x24e3a94),callback_bytes,request_callback,(void **)&original_callback)){
        br_stage("AUTH_CALLBACK_INSTALL_FAILED");pthread_mutex_unlock(&prepare_lock);return JNI_FALSE;}
    /* Exact original prologue / offsets verified by metadata tests at build. */
    static const unsigned char send_bytes[16]={0xf4,0x4f,0xbe,0xa9,0xfd,0x7b,0x01,0xa9,0xfd,0x43,0x00,0x91,0x74,0x81,0x01,0xb0};
    if(!original_send&&!br_hook_install((void *)(base+0x24e3d3c),send_bytes,send_request,(void **)&original_send)){
        br_stage("AUTH_INSTALL_FAILED");pthread_mutex_unlock(&prepare_lock);return JNI_FALSE;}
    void *br=dlopen("libbr.so",RTLD_NOW|RTLD_NOLOAD);
    int (*tutorial)(void *,uintptr_t)=br?dlsym(br,"br_install_tutorial"):NULL;
    if(tutorial&&!tutorial(library,base))br_stage("TUTORIAL_ORIGINAL_FALLBACK");
    static const unsigned char title_bytes[16]={0xf8,0x5f,0xbc,0xa9,0xf6,0x57,0x01,0xa9,0xf4,0x4f,0x02,0xa9,0xfd,0x7b,0x03,0xa9};
    static const unsigned char media_bytes[16]={0xf4,0x4f,0xbe,0xa9,0xfd,0x7b,0x01,0xa9,0xfd,0x43,0x00,0x91,0xd4,0x86,0x01,0xb0};
    static const unsigned char load_bytes[16]={0xf4,0x4f,0xbe,0xa9,0xfd,0x7b,0x01,0xa9,0xfd,0x43,0x00,0x91,0xf3,0x03,0x00,0xaa};
    if(!original_title){
        int ok=br_hook_install((void *)(base+0x2436120),media_bytes,media_init,(void **)&original_media_init)&&
            br_hook_install((void *)(base+0x24366c4),media_bytes,media_play,(void **)&original_media_play)&&
            br_hook_install((void *)(base+0x243a5e4),load_bytes,media_load,(void **)&original_media_load)&&
            br_hook_install((void *)(base+0x175de3c),title_bytes,title,(void **)&original_title);
        if(!ok){safe_profile=0;disable_title_video=0;br_stage("TITLE_ORIGINAL_FALLBACK");}
    }
    prepared=1;br_stage("NATIVE_PREPARED");pthread_mutex_unlock(&prepare_lock);return JNI_TRUE;
}
