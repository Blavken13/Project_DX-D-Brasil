/* Original ARM64 client: restore the missing K beside the tutorial raft.
 * Reuse NPC 502 when present, otherwise create it as the PC tutorial does.
 * Keep the original ToDo/completion flow; never advance a tutorial by patching it.
 * Unity objects are inspected/changed exclusively from PlayGuideSystem.Update.
 */
#include <android/log.h>
#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>
#include "runtime_compat.h"
#define LOG(...) __android_log_print(ANDROID_LOG_INFO, "BRRaftK", __VA_ARGS__)
typedef struct { void *klass, *monitor, *bounds; uintptr_t length; void *items[]; } Array;
typedef struct { float x,y,z; } Vec3;
typedef struct { float x,y; } Vec2;
static void *(*domain_get)(void);
static const void **(*assemblies)(void *,size_t *);
static void *(*image_get)(const void *);
static void *(*class_find)(void *,const char *,const char *);
static void *(*object_class)(void *);
static void *(*parent_class)(void *);
static const void *(*methods)(void *,void **);
static const char *(*method_name)(const void *);
static uint32_t (*param_count)(const void *);
static const void *(*param_type)(const void *,uint32_t);
static char *(*type_name)(const void *);
static void (*api_free)(void *);
static const void *(*class_type)(void *);
static void *(*type_object)(const void *);
static void *(*invoke)(const void *,void *,void **,void **);
static void *(*field_find)(void *,const char *);
static void (*field_get)(void *,void *,void *);
static void (*field_set)(void *,void *,void *);
static void *(*new_string)(const char *);
static void *(*unbox)(void *);
static void *(*nested_types)(void *,void **);
static void *(*object_new)(void *);
static Array *(*array_new)(void *,uintptr_t);
static uint32_t (*handle_new)(void *,uint8_t);
static void *(*handle_target)(uint32_t);
static void (*handle_free)(uint32_t);
static void (*original_update)(void *,const void *);
static void (*original_fill)(void *,const void *);
static void (*original_depart)(void *,void *,const void *);
static void *interaction_type, *game_type, *quest_type, *renderer_type;
static void *object_klass, *manager_type;
static unsigned ticks;
static int resolved, disabled;
static uint32_t spawned_handle;
static uint32_t boat_handle;
static uint32_t type_handles[5];
static void *reference(void *,const char *);

static void *klass(const char *ns,const char *name) {
    size_t count=0; const void **list=assemblies(domain_get(),&count);
    for(size_t i=0;i<count;i++) { void *k=class_find(image_get(list[i]),ns,name); if(k)return k; }
    return NULL;
}
/* Choose the non-generic overload by its first parameter, rather than method name alone. */
static const void *method(void *k,const char *name,int count,const char *first) {
    for(;k;k=parent_class(k)) {
        void *it=NULL; const void *m;
        while((m=methods(k,&it))) {
            if(strcmp(method_name(m),name)||param_count(m)!=(uint32_t)count)continue;
            if(first) {
                char *n=type_name(param_type(m,0)); int same=n&&!strcmp(n,first);
                if(n)api_free(n); if(!same)continue;
            }
            if(!strcmp(name,"SetValue")&&count==2) {
                char *n=type_name(param_type(m,1));int same=n&&!strcmp(n,"System.Int32");
                if(n)api_free(n);if(!same)continue;
            }
            return m;
        }
    }
    return NULL;
}
static void *call(void *k,void *self,const char *name,int count,const char *first,void **args) {
    const void *m=method(k,name,count,first); if(!m)return NULL;
    /* Stripped generic methods can retain metadata while lacking an invoker. */
    if(!((void **)m)[0]||!((void **)m)[1]) {LOG("Metodo sem codigo no APK original: %s",name);return NULL;}
    void *exception=NULL; void *result=invoke(m,self,args,&exception);
    if(exception) { LOG("Chamada recusada: %s",name); return NULL; }
    return result;
}
static void *field(void *self,const char *name) {
    for(void *k=object_class(self);k;k=parent_class(k)) {
        void *f=field_find(k,name); if(f)return f;
    }
    return NULL;
}
static int equals(void *str,const char *ascii) {
    if(!str)return 0;
    int n=*(int *)((char *)str+16); if(n!=(int)strlen(ascii))return 0;
    const uint16_t *s=(uint16_t *)((char *)str+20);
    for(int i=0;i<n;i++)if(s[i]!=(unsigned char)ascii[i])return 0;
    return 1;
}
static int resolve(void) {
    void *ik=klass("","ClientInteractionToDo"), *gk=klass("UnityEngine","GameObject");
    void *qk=klass("","ClientInteractionQuest"), *rk=klass("UnityEngine","Renderer");
    void *mk=klass("","AssetBundleManager"); object_klass=klass("UnityEngine","Object");
    if(!ik||!gk||!qk||!rk||!object_klass||!mk) {
        static int logged;if(!logged){logged=1;LOG("Classes K: interaction=%d game=%d quest=%d renderer=%d object=%d manager=%d",!!ik,!!gk,!!qk,!!rk,!!object_klass,!!mk);}return 0;
    }
    interaction_type=type_object(class_type(ik)); game_type=type_object(class_type(gk));
    quest_type=type_object(class_type(qk)); renderer_type=type_object(class_type(rk));
    manager_type=type_object(class_type(mk));
    if(!(interaction_type&&game_type&&quest_type&&renderer_type&&manager_type))return 0;
    /* Type objects are managed references retained by this native module.
     * Root them explicitly across collections during terrain/scene changes. */
    void *types[]={interaction_type,game_type,quest_type,renderer_type,manager_type};
    for(int i=0;i<5;i++)if(!type_handles[i])type_handles[i]=handle_new(types[i],0);
    return 1;
}
static void trace_guide(void *guide) {
    static char previous[128];
    void *event=reference(guide,"_currentEvent"),*name=reference(event,"Name");
    if(!name)return;
    int length=*(int *)((char *)name+16);
    if(length<1||length>110)return;
    const uint16_t *text=(uint16_t *)((char *)name+20);
    char stage[128]="GUIDE_";
    for(int i=0;i<length;i++) {
        unsigned c=text[i];if(c>='a'&&c<='z')c-=32;
        if(!((c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'))return;
        stage[6+i]=(char)c;
    }
    stage[6+length]=0;
    if(strcmp(previous,stage)){snprintf(previous,sizeof(previous),"%s",stage);br_stage(stage);}
}
static void fill(void *self,const void *method_info) {
    br_stage("TUTORIAL_RAFT_MATERIALS_SEND");
    original_fill(self,method_info);
    br_stage("TUTORIAL_RAFT_MATERIALS_RETURN");
}
static void depart(void *self,void *boat,const void *method_info) {
    br_stage("TUTORIAL_DEPART_SEND");
    original_depart(self,boat,method_info);
    br_stage("TUTORIAL_DEPART_RETURN");
}
static void *reference(void *obj,const char *name) {
    if(!obj)return NULL; void *f=field(obj,name),*value=NULL;
    if(f)field_get(obj,f,&value);return value;
}
static void *prefab(void) {
    /* Unity stripped the unused synchronous AssetBundle APIs from this build.
     * Reuse the original manager's async request and dependency loader instead. */
    void *find_args[]={manager_type};
    Array *managers=call(object_klass,NULL,"FindObjectsOfType",1,"System.Type",find_args);
    if(!managers||!managers->length)return NULL;
    void *manager=managers->items[0],*dict=reference(manager,"_assetBundleItemDict");
    if(!dict)return NULL;
    void *key=new_string("models$npc$f_npc_k_story.prefab.bundle"),*get_args[]={key};
    void *has=call(object_class(dict),dict,"ContainsKey",1,NULL,get_args);
    if(!has||!*(uint8_t *)unbox(has))return NULL;
    void *item=call(object_class(dict),dict,"get_Item",1,NULL,get_args);
    if(!item)return NULL;
    void *request=reference(item,"Request");
    if(!request) {
        void *parent=reference(item,"Parent");if(!parent)return NULL;
        void *load_args[]={parent};call(object_class(manager),manager,"TryLoadBundleFile",1,NULL,load_args);
        void *bundle=reference(parent,"Bundle"),*asset_name=reference(item,"Name");
        if(!bundle||!asset_name)return NULL;
        void *args[]={asset_name,game_type};
        request=call(object_class(bundle),bundle,"LoadAssetAsync",2,"System.String",args);
        void *f=field(item,"Request");if(!request||!f)return NULL;
        /* This Unity 2017 API takes reference objects directly, unlike value payloads. */
        field_set(item,f,request);
    }
    void *done=call(object_class(request),request,"get_isDone",0,NULL,NULL);
    if(!done||!*(uint8_t *)unbox(done))return NULL;
    return call(object_class(request),request,"get_asset",0,NULL,NULL);
}
static Array *components(void *go,void *type) {
    uint8_t inactive=1; void *args[]={type,&inactive};
    return call(object_class(go),go,"GetComponentsInChildren",2,"System.Type",args);
}
static int write_field(void *obj,const char *name,void *value) {
    void *f=field(obj,name); if(!f)return 0;field_set(obj,f,value);return 1;
}
static int write_reference(void *obj,const char *name,void *reference_object) {
    void *f=field(obj,name);if(!f)return 0;
    field_set(obj,f,reference_object);return 1;
}
static int is_alive(void *obj) {
    void *args[]={obj};void *boxed=call(object_klass,NULL,"op_Implicit",1,"UnityEngine.Object",args);
    return boxed&&*(uint8_t *)unbox(boxed);
}
static int raft_todo_active(void) {
    void *k=klass("","ToDoListSystem");if(!k)return 0;
    void *type=type_object(class_type(k)),*args[]={type};
    Array *systems=call(object_klass,NULL,"FindObjectsOfType",1,"System.Type",args);
    if(!systems||!systems->length)return 0;
    void *name=new_string("talk_npc_raft_ancora.meet_chief"),*todo_args[]={name};
    void *todo=call(k,systems->items[0],"FindToDo",1,"System.String",todo_args);
    if(!todo)return 0;
    void *done=call(object_class(todo),todo,"get_IsCompleted",0,NULL,NULL);
    return done&&!*(uint8_t *)unbox(done);
}
static void create_missing(void *guide) {
    void *event=reference(guide,"_currentEvent"),*event_name=reference(event,"Name");
    if(!equals(event_name,"trigger_arrive_shipyard")&&!equals(event_name,"talk_npc_raft_ancora")&&!raft_todo_active())return;
    static int logged;if(!logged){logged=1;LOG("Objetivo da K ativo; preparando modelo e jangada");}
    if(spawned_handle) {
        if(is_alive(handle_target(spawned_handle)))return;
        handle_free(spawned_handle);spawned_handle=0;
    }
    void *island=klass("","TutorialIslandSystem");if(!island)return;
    void *island_type=type_object(class_type(island)),*find_args[]={island_type};
    Array *systems=call(object_klass,NULL,"FindObjectsOfType",1,"System.Type",find_args);
    if(!systems||!systems->length)return;
    void *system=systems->items[0],*boat=reference(system,"_tutorialBoat");
    if(!is_alive(boat))return;
    void *tile=call(object_class(boat),boat,"get_WorldTile",0,NULL,NULL);if(!tile)return;
    int *coords=unbox(tile);Vec2 offset={coords[0]-2.0f,coords[1]+3.0f};
    uint8_t center=0;void *position_args[]={&offset,&center};
    void *util=klass("Durango.Terrain","Util");if(!util)return;
    void *position_box=call(util,NULL,"TilePositionToClientPosition",2,"UnityEngine.Vector2",position_args);
    if(!position_box)return;
    Vec3 position=*(Vec3 *)unbox(position_box);position.x+=100;position.z+=100;
    void *asset=prefab();if(!asset)return;
    LOG("Prefab de K carregado; criando NPC");
    void *instantiate_args[]={asset};
    void *clone=call(object_klass,NULL,"Instantiate",1,"UnityEngine.Object",instantiate_args);if(!clone)return;
    LOG("Modelo de K instanciado");
    Array *quests=components(clone,quest_type);if(!quests)goto fail;
    for(uintptr_t i=0;i<quests->length;i++) {
        void *args[]={quests->items[i]};call(object_klass,NULL,"DestroyImmediate",1,"UnityEngine.Object",args);
    }
    void *add_args[]={interaction_type};
    void *npc=call(object_class(clone),clone,"AddComponent",1,"System.Type",add_args);if(!npc)goto fail;
    LOG("Componente de conversa criado");
    void *ik=object_class(npc),*it=NULL,*elem_class=NULL,*nested;
    while((nested=nested_types(ik,&it))) {
        void *candidate=object_new(nested);
        if(candidate&&field(candidate,"ToDo")&&field(candidate,"Duration")) {elem_class=nested;break;}
    }
    if(!elem_class)goto fail;
    void *elem=object_new(elem_class);if(!elem)goto fail;
    void *name=new_string("Conversar"),*todo=new_string("talk_npc_raft_ancora.meet_chief"),*empty=new_string("");
    float duration=0.5f;int action=10250;uint8_t selectable=1;
    if(!write_reference(elem,"Name",name)||!write_reference(elem,"ToDo",todo)||
       !write_reference(elem,"MotionName",empty)||!write_reference(elem,"Icon",empty)||
       !write_field(elem,"Duration",&duration)||!write_field(elem,"Action",&action))goto fail;
    void *list=reference(npc,"_actionList");if(!list)goto fail;
    /* Populate the serialized list without calling stripped List<ActionElem> helpers. */
    Array *items=array_new(elem_class,1);if(!items)goto fail;
    int index=0;void *set_args[]={elem,&index};
    call(object_class(items),items,"SetValue",2,"System.Object",set_args);
    if(items->items[0]!=elem)goto fail;
    int one=1;
    if(!write_reference(list,"_items",items)||!write_field(list,"_size",&one)||
       !write_field(list,"_version",&one))goto fail;
    LOG("Acao Conversar vinculada ao objetivo");
    void *id=new_string("502"),*target=new_string("K");
    if(!write_reference(npc,"_entityId",id)||!write_reference(npc,"_targetName",target)||
       !write_field(npc,"Selectable",&selectable))goto fail;
    void *ct=call(object_class(clone),clone,"get_transform",0,NULL,NULL);
    void *bt=call(object_class(boat),boat,"get_transform",0,NULL,NULL);if(!ct||!bt)goto fail;
    /* Parenting inside Artifact makes the input picker select the raft first. */
    void *scene_parent=call(object_class(bt),bt,"get_parent",0,NULL,NULL);
    uint8_t world=1;void *parent_args[]={scene_parent,&world};
    call(object_class(ct),ct,"SetParent",2,"UnityEngine.Transform",parent_args);
    void *pos_args[]={&position};call(object_class(ct),ct,"set_position",1,"UnityEngine.Vector3",pos_args);
    Vec3 rotation={0,180,0};void *rot_args[]={&rotation};
    call(object_class(ct),ct,"set_eulerAngles",1,"UnityEngine.Vector3",rot_args);
    void *clone_name=new_string("NPC_K_Raft_Ancora"),*name_args[]={clone_name};
    call(object_klass,clone,"set_name",1,"System.String",name_args);
    spawned_handle=handle_new(clone,0);
    boat_handle=handle_new(boat,0);
    LOG("K criada junto a jangada tile=%d,%d; conversa meet_chief ativa",coords[0],coords[1]);return;
fail:
    {void *args[]={clone};call(object_klass,NULL,"DestroyImmediate",1,"UnityEngine.Object",args);}
    disabled=1;LOG("Criacao interrompida: estrutura de componentes inesperada");
}
static void restore(void *guide) {
    if(disabled)return;
    if(!resolved) { if(!resolve())return; resolved=1; }
    if(boat_handle&&!is_alive(handle_target(boat_handle))) {
        if(spawned_handle) {
            void *clone=handle_target(spawned_handle);
            if(is_alive(clone)) {void *args[]={clone};call(object_klass,NULL,"Destroy",1,"UnityEngine.Object",args);}
            handle_free(spawned_handle);spawned_handle=0;
        }
        handle_free(boat_handle);boat_handle=0;
    }
    if(spawned_handle&&!is_alive(handle_target(spawned_handle))) {
        handle_free(spawned_handle);spawned_handle=0;
        if(boat_handle) {handle_free(boat_handle);boat_handle=0;}
    }
    void *event=reference(guide,"_currentEvent"),*name=reference(event,"Name");
    if(!equals(name,"trigger_arrive_shipyard")&&!equals(name,"talk_npc_raft_ancora")&&!raft_todo_active())return;
    /* No global NPC scans or prefab changes after the raft conversation.
     * In particular, leave scene departure and teardown to the original game. */
    void *args[]={interaction_type};
    Array *npcs=call(object_klass,NULL,"FindObjectsOfType",1,"System.Type",args);
    if(!npcs)return;
    static unsigned scans;
    if(++scans==1)LOG("Busca do NPC: %zu objetos",(size_t)npcs->length);
    static int inspected;
    if(!inspected&&npcs->length) {inspected=1;LOG("Interacoes locais encontradas: %zu",(size_t)npcs->length);}
    int found=0;
    for(uintptr_t i=0;i<npcs->length;i++) {
        void *npc=npcs->items[i], *id=NULL; void *f=field(npc,"_entityId");
        if(!f)continue; field_get(npc,f,&id); if(!equals(id,"502"))continue;
        found=1;
        if(spawned_handle&&is_alive(handle_target(spawned_handle)))continue;
        void *go=call(object_class(npc),npc,"get_gameObject",0,NULL,NULL);
        if(!go)continue;
        void *transform=call(object_class(go),go,"get_transform",0,NULL,NULL);
        if(!transform)continue;
        void *name=new_string("NPC_K_Raft_Ancora"); void *find_args[]={name};
        void *child=call(object_class(transform),transform,"Find",1,"System.String",find_args);
        void *alive_args[]={child}; void *alive=call(object_klass,NULL,"op_Implicit",1,"UnityEngine.Object",alive_args);
        if(alive&&*(uint8_t *)unbox(alive))continue;
        void *asset=prefab(); if(!asset)return; void *instantiate_args[]={asset};
        void *clone=call(object_klass,NULL,"Instantiate",1,"UnityEngine.Object",instantiate_args);
        if(!clone)return;
        void *set_name[]={name}; call(object_klass,clone,"set_name",1,"System.String",set_name);
        Array *quests=components(clone,quest_type);
        if(!quests) { void *a[]={clone}; call(object_klass,NULL,"DestroyImmediate",1,"UnityEngine.Object",a); disabled=1; LOG("K mantida no fluxo original: componente indisponivel"); return; }
        for(uintptr_t j=0;j<quests->length;j++) {
            void *a[]={quests->items[j]};call(object_klass,NULL,"DestroyImmediate",1,"UnityEngine.Object",a);
        }
        Array *old=components(go,renderer_type);
        void *ct=call(object_class(clone),clone,"get_transform",0,NULL,NULL);
        if(!ct) { void *a[]={clone};call(object_klass,NULL,"DestroyImmediate",1,"UnityEngine.Object",a);return; }
        uint8_t keep_world=0; void *parent_args[]={transform,&keep_world};
        call(object_class(ct),ct,"SetParent",2,"UnityEngine.Transform",parent_args);
        Vec3 zero={0,0,0}; void *pos_args[]={&zero};
        call(object_class(ct),ct,"set_localPosition",1,"UnityEngine.Vector3",pos_args);
        call(object_class(ct),ct,"set_localEulerAngles",1,"UnityEngine.Vector3",pos_args);
        /* Hide only the placeholder mesh. Preserve all its colliders and tutorial actions. */
        if(old)for(uintptr_t j=0;j<old->length;j++) {
            uint8_t enabled=0;void *a[]={&enabled};
            call(object_class(old->items[j]),old->items[j],"set_enabled",1,"System.Boolean",a);
        }
        void *target=field(npc,"_targetName"); if(target) { void *k=new_string("K");field_set(npc,target,k); }
        LOG("K restaurada no NPC 502; collider e conversa do tutorial preservados");
    }
    if(!found)create_missing(guide);
}
static void update(void *self,const void *method_info) {
    if(!ticks)LOG("Update do tutorial ativo na thread Unity");
    original_update(self,method_info);
    if(++ticks%90==0){trace_guide(self);restore(self);}
}
int br_install_raft_k(void *library,uintptr_t base) {
#define API(var,name) do { *(void **)(&var)=dlsym(library,"il2cpp_" name); if(!var)return 0; } while(0)
    API(domain_get,"domain_get"); API(assemblies,"domain_get_assemblies"); API(image_get,"assembly_get_image");
    API(class_find,"class_from_name");API(object_class,"object_get_class"); API(parent_class,"class_get_parent");
    API(methods,"class_get_methods");API(method_name,"method_get_name");API(param_count,"method_get_param_count");
    API(param_type,"method_get_param");API(type_name,"type_get_name");API(api_free,"free");
    API(class_type,"class_get_type");API(type_object,"type_get_object");API(invoke,"runtime_invoke");
    API(field_find,"class_get_field_from_name");API(field_get,"field_get_value");API(field_set,"field_set_value");
    API(new_string,"string_new");API(unbox,"object_unbox");
    API(nested_types,"class_get_nested_types");API(object_new,"object_new");
    API(array_new,"array_new");
    API(handle_new,"gchandle_new");API(handle_target,"gchandle_get_target");API(handle_free,"gchandle_free");
    void *entry=(void *)(base+0x16f88ec);
    const unsigned char expected[16]={0xf6,0x57,0xbd,0xa9,0xf4,0x4f,0x01,0xa9,0xfd,0x7b,0x02,0xa9,0xfd,0x83,0x00,0x91};
    if(memcmp(entry,expected,16)) {LOG("Cliente inesperado; restauracao nao instalada");return 0;}
    if (!br_hook_install(entry,expected,update,(void **)&original_update)) return 0;
    const unsigned char fill_bytes[16]={0xeb,0x2b,0xba,0x6d,0xe9,0x23,0x01,0x6d,
        0xf8,0x5f,0x02,0xa9,0xf6,0x57,0x03,0xa9};
    if(!br_hook_install((void *)(base+0x169e338),fill_bytes,fill,(void **)&original_fill))
        br_stage("TUTORIAL_FILL_TRACE_UNAVAILABLE");
    if(!br_hook_install((void *)(base+0x169e5d4),expected,depart,(void **)&original_depart))
        br_stage("TUTORIAL_DEPART_TRACE_UNAVAILABLE");
    LOG("Restauracao visual da K instalada; motor original preservado");return 1;
}
