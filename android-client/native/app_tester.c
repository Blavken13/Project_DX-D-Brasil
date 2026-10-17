/* Unity 6 adapter: invoked only by the community bridge on the Unity thread.
 * No init polling, no engine replacement, no video hooks. */
#define _GNU_SOURCE
#include <jni.h>
#include <android/log.h>
#include <dlfcn.h>
#include <pthread.h>
#include <stdint.h>
#include <string.h>
#include "app_tester_offsets.h"
#ifdef TESTER_ESTATE_REPLACE
#include <sys/mman.h>
#include <unistd.h>
#include <errno.h>
#endif
#define LOG(s) __android_log_print(ANDROID_LOG_INFO,"AppTester","%s",s)
typedef const void *Method;
typedef struct { void *klass,*monitor; int32_t length; uint16_t chars[1]; } String;
static JavaVM *vm;
static jclass login;
static jmethodID finished;
static void *engine;
static int installed,loading;
static pthread_mutex_t lock=PTHREAD_MUTEX_INITIALIZER;
static char token[44],queued[44];
typedef uintptr_t Handle;
static Handle pending_url,pending_callback;
typedef struct { void *player; int32_t slot; } PlayerChoice;
static Handle selected_player;
static int selected_slot,has_selection,recommending;
static PlayerChoice (*original_recommended)(void *,Method);
static PlayerChoice (*original_apply)(void *,PlayerChoice,Method);
static void *(*invoke)(Method,void *,void **,void **);
static void *(*new_string)(const char *);
static void *(*object_new)(void *);
static void *(*object_class)(void *);
static Method (*method_find)(void *,const char *,int);
static Handle (*root_new)(void *,uint8_t);
static void *(*root_target)(Handle);
static void (*root_free)(Handle);
static void (*original_accounts)(void *,void *,Method);
static void (*original_updated)(void *,void *,Method);
static void *(*original_send)(void *,Method);
static Method uri_get,uri_absolute,add_field,player_info,apply_choice;

static int valid(const char *v) {
    if(!v || strlen(v)!=43)return 0;
    for(int i=0;i<43;i++){char c=v[i];if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'||c=='-'))return 0;}
    return 1;
}
static void keep(Handle *slot,void *value){Handle next=value?root_new(value,0):0;if(*slot)root_free(*slot);*slot=next;}
static void *call(Method m,void *self,void **args) {
    if(!m)return NULL;
    void *exception=NULL,*result=invoke(m,self,args,&exception);
    if(exception){LOG("managed_call_failed");return NULL;}return result;
}
static void *klass(const char *space,const char *name){
    void *(*domain_get)(void)=dlsym(engine,"il2cpp_domain_get");
    void **(*assemblies)(void *,size_t *)=dlsym(engine,"il2cpp_domain_get_assemblies");
    void *(*image)(void *)=dlsym(engine,"il2cpp_assembly_get_image");
    void *(*find)(void *,const char *,const char *)=dlsym(engine,"il2cpp_class_from_name");
    if(!domain_get||!assemblies||!image||!find||!domain_get())return NULL;
    size_t count=0;void **all=assemblies(domain_get(),&count);
    if(!all||count>4096)return NULL;
    for(size_t i=0;i<count;i++){void *c=find(image(all[i]),space,name);if(c)return c;}return NULL;
}
static Method method(const char *s,const char *c,const char *n,int a){void *k=klass(s,c);return k?method_find(k,n,a):NULL;}
static void notify(int success){
    if(!vm||!login||!finished)return;
    JNIEnv *env=NULL;int attached=0;
    if((*vm)->GetEnv(vm,(void **)&env,JNI_VERSION_1_6)==JNI_EDETACHED){if((*vm)->AttachCurrentThread(vm,&env,NULL)!=JNI_OK)return;attached=1;}
    if(env){(*env)->CallStaticVoidMethod(env,login,finished,success?JNI_TRUE:JNI_FALSE);if((*env)->ExceptionCheck(env)){(*env)->ExceptionClear(env);LOG("ui_callback_failed");}}
    if(attached)(*vm)->DetachCurrentThread(vm);
}
static void accounts(void *url,void *callback,Method info){
    keep(&pending_url,url);keep(&pending_callback,callback);
    if(token[0]){original_accounts(url,callback,info);return;}
    LOG("accounts_waiting_for_login");
    void *c=klass("Durango.Logic.Clusters","Account");void *empty=c?object_new(c):NULL;
    LOG(empty?"selection_account_allocated":"selection_account_missing");
    if(empty&&callback){Handle held=root_new(empty,0);call(method_find(c,".ctor",0),empty,NULL);LOG("selection_account_initialized");void *args[]={empty};call(method_find(object_class(callback),"Invoke",1),callback,args);LOG("selection_callback_completed");root_free(held);}
}
static PlayerChoice recommended(void *account,Method info){
    recommending++;
    PlayerChoice result=original_recommended(account,info);
    recommending--;
    return result;
}
static PlayerChoice choose(void *account,PlayerChoice choice,Method info){
    /* GetRecommendedPlayer also calls ApplyRecommendedPlayer for its default.
     * Only an explicit UI confirmation may replace the remembered choice. */
    if(!recommending&&token[0]&&choice.player){
        keep(&selected_player,choice.player);selected_slot=choice.slot;has_selection=1;
        __android_log_print(ANDROID_LOG_INFO,"AppTester","character_choice_saved slot=%d",selected_slot);
    }
    return original_apply(account,choice,info);
}
static void updated(void *self,void *account,Method info){
    LOG("selection_update_entered");
    if(account&&has_selection&&!loading){
        String *id=root_target(selected_player);
        void *args[]={id};
        /* A removed character must not be restored into a fresh account DTO.
         * Empty IDs are the explicit choice to create a new character. */
        if(id&&(id->length==0||call(player_info,account,args))){
            PlayerChoice choice={id,selected_slot};void *values[]={&choice};
            call(apply_choice,account,values);
            __android_log_print(ANDROID_LOG_INFO,"AppTester","character_choice_restored slot=%d",selected_slot);
        }else{has_selection=0;keep(&selected_player,NULL);LOG("character_choice_unavailable");}
    }
    original_updated(self,account,info);
    LOG("selection_update_completed");
    if(loading){loading=0;if(!account)memset(token,0,sizeof(token));LOG(account?"characters_ready":"characters_failed");notify(account!=NULL);}
}
static void authorize(void *request){
    if(!request||!token[0])return;
    void *uri=call(uri_get,request,NULL);if(!uri)return;String *s=call(uri_absolute,uri,NULL);
    if(!s||s->length<1||s->length>2047)return;
    char text[2048];for(int i=0;i<s->length;i++){if(s->chars[i]>127)return;text[i]=(char)s->chars[i];}text[s->length]=0;
    const char *gateway="http://179.197.72.129:8190";size_t n=strlen(gateway);
    if(strncmp(text,gateway,n))return;
    const char *p=text+n;
    if(!((!strncmp(p,"/accounts",9)&&(p[9]==0||p[9]=='?'))||(!strncmp(p,"/sessions",9)&&(p[9]==0||p[9]=='?'))))return;
    void *args[]={new_string("token"),new_string(token)};call(add_field,request,args);LOG("request_authorized");
}
static void *send(void *request,Method info){authorize(request);return original_send(request,info);}

#ifdef TESTER_ESTATE_EXPANSION
/* Same route used by the working Unity 2017 Android estate patch. Resolve the
 * methods in this build instead of reusing addresses from the old IL2CPP ELF. */
typedef struct { int32_t x,y; } EstatePoint;
static Method estate_free;
static int estate_installed;
#ifdef TESTER_ESTATE_REPLACE
static void estate_click_state(void *self,EstatePoint position){
    void *(*field_find)(void *,const char *)=dlsym(engine,"il2cpp_class_get_field_from_name");
    void (*field_get)(void *,void *,void *)=dlsym(engine,"il2cpp_field_get_value");
    int32_t largest=-1,size=-1,type=-1;
    if(self&&field_find&&field_get){
        void *c=object_class(self),*f=field_find(c,"_largestSize");
        if(f)field_get(self,f,&largest);
        void *estate=NULL;f=field_find(c,"_expandEstate");
        if(f)field_get(self,f,&estate);
        if(estate){
            void *license=call(method_find(object_class(estate),"get_License",0),estate,NULL);
            if(license){
                void *lc=object_class(license);f=field_find(lc,"Size");
                if(f)field_get(license,f,&size);
                f=field_find(lc,"Type");if(f)field_get(license,f,&type);
            }
        }
    }
    __android_log_print(ANDROID_LOG_INFO,"AppTester",
        "estate_expansion_click tile=%d,%d size=%d allowance=%d owner_type=%d",
        position.x,position.y,size,largest,type);
}
/* This handler is replaced completely; its old instructions are never run.
 * A trampoline would unnecessarily relocate its initial CBZ and is rejected
 * by the community hook_code implementation on the actual Unity 6 build. */
static int replace_estate_entry(void *entry,void *replacement){
    long page_size=sysconf(_SC_PAGESIZE);
    if(!entry||!replacement||((uintptr_t)entry&3)||page_size<=0)return 0;
    uintptr_t mask=(uintptr_t)page_size-1;
    if(((uintptr_t)page_size&mask)!=0)return 0;
    uintptr_t begin=(uintptr_t)entry&~mask;
    size_t length=(((uintptr_t)entry+16+mask)&~mask)-begin;
    unsigned char original[16],redirect[16];
    const uint32_t jump[2]={0x58000050u,0xd61f0200u}; /* ldr x16,+8; br x16 */
    memcpy(original,entry,sizeof(original));
    memcpy(redirect,jump,sizeof(jump));memcpy(redirect+8,&replacement,sizeof(replacement));
    __android_log_print(ANDROID_LOG_INFO,"AppTester",
        "estate_expansion_entry_before %08x %08x %08x %08x",
        ((uint32_t *)entry)[0],((uint32_t *)entry)[1],((uint32_t *)entry)[2],((uint32_t *)entry)[3]);
    if(mprotect((void *)begin,length,PROT_READ|PROT_WRITE|PROT_EXEC)!=0){
        __android_log_print(ANDROID_LOG_ERROR,"AppTester","estate_expansion_write_protection errno=%d",errno);
        return 0;
    }
    memcpy(entry,redirect,sizeof(redirect));
    __builtin___clear_cache((char *)entry,(char *)entry+sizeof(redirect));
    if(mprotect((void *)begin,length,PROT_READ|PROT_EXEC)!=0){
        memcpy(entry,original,sizeof(original));
        __builtin___clear_cache((char *)entry,(char *)entry+sizeof(original));
        mprotect((void *)begin,length,PROT_READ|PROT_EXEC);
        LOG("estate_expansion_restore_protection_failed");return 0;
    }
    LOG("estate_expansion_direct_entry_ready");return 1;
}
#endif
static void estate_click(void *self,EstatePoint position,Method info){
    (void)info;
    void *args[]={&position};
#ifdef TESTER_ESTATE_REPLACE
    if(!self)return;
    estate_click_state(self,position);
#endif
    LOG("estate_expansion_free_path");
    call(estate_free,self,args);
}
static void install_estate_expansion(void){
    if(estate_installed)return;
    void *point=klass("","Point2");
    int32_t (*value_size)(void *,uint32_t *)=dlsym(engine,"il2cpp_class_value_size");
    uint32_t alignment=0;
    if(!point||!value_size)return;
    if(value_size(point,&alignment)!=(int32_t)sizeof(EstatePoint)){
        estate_installed=-1;LOG("estate_expansion_unsupported_point_abi");return;
    }
    Method click=method("Durango.UI","EstateGridGroup","OnExpandEstateClick",1);
    estate_free=method("Durango.UI","EstateGridGroup","ExpandPersonalEstate",1);
    if(!click||!estate_free||!*(void *const *)click)return;
#ifdef TESTER_ESTATE_REPLACE
    estate_installed=replace_estate_entry(*(void *const *)click,estate_click)?1:-1;
    LOG(estate_installed==1?"estate_expansion_ready":"estate_expansion_hook_failed");
#else
    void *dbr=dlopen("libdbr.so",RTLD_NOW|RTLD_NOLOAD);
    void *blob=dbr?dlsym(dbr,"thpt_blob"):NULL;
    if(!blob){if(dbr)dlclose(dbr);return;}
    unsigned char *base=(unsigned char *)blob-DBR_BLOB;
    void *(*hook)(void *,void *)=(void *(*)(void *,void *))(base+DBR_HOOK);
    void *previous=hook(*(void *const *)click,estate_click);
    estate_installed=previous?1:-1;
    LOG(previous?"estate_expansion_ready":"estate_expansion_hook_failed");
    dlclose(dbr);
#endif
}
#endif

__attribute__((visibility("default"))) void tester_before_tick(void){
    if(!installed){
        if(!engine)engine=dlopen("libil2cpp.so",RTLD_NOW|RTLD_NOLOAD);
        if(!engine)return;
#define API(n,s) n=dlsym(engine,s)
        API(invoke,"il2cpp_runtime_invoke");API(new_string,"il2cpp_string_new");API(object_new,"il2cpp_object_new");API(object_class,"il2cpp_object_get_class");API(method_find,"il2cpp_class_get_method_from_name");
        API(root_new,"il2cpp_gchandle_new");API(root_target,"il2cpp_gchandle_get_target");API(root_free,"il2cpp_gchandle_free");
        if(!invoke||!new_string||!object_new||!object_class||!method_find||!root_new||!root_target||!root_free)return;
        Method ra=method("Durango.Logic.Clusters","Clusters","RequestAccounts",2);
        Method up=method("Durango.UI","TitleMenuUserControlBase","OnClusterAccountUpdated",1);
        Method sr=method("BestHTTP","HTTPManager","SendRequest",1);
        Method rec=method("Durango.Logic.Clusters","Account","GetRecommendedPlayer",0);
        apply_choice=method("Durango.Logic.Clusters","Account","ApplyRecommendedPlayer",1);
        player_info=method("Durango.Logic.Clusters","Account","GetPlayerInfoText",1);
        uri_get=method("BestHTTP","HTTPRequest","get_Uri",0);
        if(!uri_get)uri_get=method("BestHTTP","HTTPRequest","get_CurrentUri",0);
        uri_absolute=method("System","Uri","get_AbsoluteUri",0);add_field=method("BestHTTP","HTTPRequest","AddField",2);
        if(!ra||!up||!sr||!rec||!apply_choice||!player_info||!uri_get||!uri_absolute||!add_field)return;
        void *dbr=dlopen("libdbr.so",RTLD_NOW|RTLD_NOLOAD);void *blob=dbr?dlsym(dbr,"thpt_blob"):NULL;if(!blob)return;
        unsigned char *base=(unsigned char *)blob-DBR_BLOB;
        void *(*hook)(void *,void *)=(void *(*)(void *,void *))(base+DBR_HOOK);
        original_send=hook(*(void *const *)sr,send);
        original_updated=hook(*(void *const *)up,updated);
        original_accounts=hook(*(void *const *)ra,accounts);
        original_recommended=hook(*(void *const *)rec,recommended);
        original_apply=hook(*(void *const *)apply_choice,choose);
        __atomic_store_n(&installed,original_send&&original_updated&&original_accounts&&original_recommended&&original_apply?1:-1,__ATOMIC_RELEASE);
        LOG(installed==1?"unity6_login_ready":"unity6_login_hook_failed");
        dlclose(dbr);
    }
    if(installed!=1)return;
#ifdef TESTER_ESTATE_EXPANSION
    install_estate_expansion();
#endif
    pthread_mutex_lock(&lock);int pending=queued[0]!=0;if(pending){memcpy(token,queued,sizeof(token));memset(queued,0,sizeof(queued));}pthread_mutex_unlock(&lock);
    if(pending){
        has_selection=0;keep(&selected_player,NULL);
        if(!pending_callback||!pending_url){memset(token,0,sizeof(token));notify(0);return;}
        loading=1;LOG("refreshing_characters");original_accounts(root_target(pending_url),root_target(pending_callback),NULL);
    }
}
JNIEXPORT void JNICALL Java_com_newdawn_launcher_TesterLogin_initialize(JNIEnv *env,jclass c){
    (*env)->GetJavaVM(env,&vm);if(!login)login=(*env)->NewGlobalRef(env,c);finished=(*env)->GetStaticMethodID(env,c,"charactersLoaded","(Z)V");LOG("adapter_loaded");
}
JNIEXPORT jboolean JNICALL Java_com_newdawn_launcher_TesterLogin_ready(JNIEnv *env,jclass c){(void)env;(void)c;return __atomic_load_n(&installed,__ATOMIC_ACQUIRE)==1?JNI_TRUE:JNI_FALSE;}
JNIEXPORT jboolean JNICALL Java_com_newdawn_launcher_TesterLogin_accept(JNIEnv *env,jclass c,jstring t){
    (void)c;const char *v=t?(*env)->GetStringUTFChars(env,t,NULL):NULL;int ok=valid(v)&&__atomic_load_n(&installed,__ATOMIC_ACQUIRE)==1;
    if(ok){pthread_mutex_lock(&lock);memcpy(queued,v,44);pthread_mutex_unlock(&lock);}if(v)(*env)->ReleaseStringUTFChars(env,t,v);return ok?JNI_TRUE:JNI_FALSE;
}
