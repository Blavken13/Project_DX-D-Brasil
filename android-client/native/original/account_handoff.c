/* Account snapshot handoff. Installed before Unity; managed calls remain on
 * the game's HTTP Send thread. Chains the existing authentication bridge. */
#define _GNU_SOURCE
#include <jni.h>
#include <android/log.h>
#include <dlfcn.h>
#include <stdint.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>
#include <pthread.h>
#include <time.h>
#define LOG(stage) __android_log_print(ANDROID_LOG_INFO, "LHAccount", "%s", stage)
#define GATEWAY "http://179.197.72.129:8190/accounts"
typedef struct { void *klass, *monitor; int32_t length; uint16_t chars[]; } ManagedString;
static void *library;
static void *(*previous_send)(void *, const void *);
static void *(*invoke)(const void *, void *, void **, void **);
static void *(*string_new)(const char *);
static void *(*object_new)(void *);
static void *uri_class;
static const void *get_uri, *absolute_uri, *set_uri, *uri_constructor;
static char cache_url[192];
static int binding, consumed, installed;
static struct timespec deadline;
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;

static void *find_class(const char *space, const char *name) {
    void *(*domain)(void) = dlsym(library, "il2cpp_domain_get");
    const void **(*assemblies)(void *, size_t *) = dlsym(library, "il2cpp_domain_get_assemblies");
    void *(*image)(const void *) = dlsym(library, "il2cpp_assembly_get_image");
    void *(*find)(void *, const char *, const char *) = dlsym(library, "il2cpp_class_from_name");
    if (!domain || !assemblies || !image || !find) return NULL;
    void *current = domain(); if (!current) return NULL;
    size_t count = 0; const void **all = assemblies(current, &count);
    if (!all || count > 4096) return NULL;
    for (size_t i = 0; i < count; i++) {
        void *klass = find(image(all[i]), space, name);
        if (klass) return klass;
    }
    return NULL;
}
static int callable(const void *method) {
    return method && ((void *const *)method)[0] && ((void *const *)method)[1];
}
static int bind(void) {
    if (binding) return binding > 0;
    const void *(*method)(void *, const char *, int) = dlsym(library, "il2cpp_class_get_method_from_name");
    void *http = find_class("BestHTTP", "HTTPRequest"); uri_class = find_class("System", "Uri");
    invoke = dlsym(library, "il2cpp_runtime_invoke");
    string_new = dlsym(library, "il2cpp_string_new"); object_new = dlsym(library, "il2cpp_object_new");
    if (method && http && uri_class) {
        get_uri = method(http, "get_Uri", 0); set_uri = method(http, "set_Uri", 1);
        absolute_uri = method(uri_class, "get_AbsoluteUri", 0);
        uri_constructor = method(uri_class, ".ctor", 1);
    }
    binding = invoke && string_new && object_new && callable(get_uri) && callable(set_uri)
        && callable(absolute_uri) && callable(uri_constructor) ? 1 : -1;
    LOG(binding > 0 ? "ACCOUNT_CACHE_MANAGED_READY" : "ACCOUNT_CACHE_BINDING_FAILED");
    return binding > 0;
}
static int account_route(void *request) {
    void *exception = NULL;
    void *uri = invoke(get_uri, request, NULL, &exception);
    if (!uri || exception) return 0;
    ManagedString *address = invoke(absolute_uri, uri, NULL, &exception);
    if (!address || exception || address->length < 0 || address->length > 2047) return 0;
    size_t length = strlen(GATEWAY);
    if ((size_t)address->length < length) return 0;
    for (size_t i = 0; i < length; i++) if (address->chars[i] != (unsigned char)GATEWAY[i]) return 0;
    return (size_t)address->length == length || address->chars[length] == '?';
}
static void *send_cached(void *request, const void *method) {
    pthread_mutex_lock(&lock);
    struct timespec now; clock_gettime(CLOCK_MONOTONIC, &now);
    if (!consumed && now.tv_sec < deadline.tv_sec && request && bind() && account_route(request)) {
        void *uri = object_new(uri_class); void *text = string_new(cache_url); void *exception = NULL;
        if (uri && text) {
            void *args[] = {text}; invoke(uri_constructor, uri, args, &exception);
            if (!exception) {
                args[0] = uri; invoke(set_uri, request, args, &exception);
                if (!exception) { consumed = 1; LOG("ACCOUNT_PREFETCH_ROUTED"); }
            }
        }
        if (!consumed) LOG("ACCOUNT_CACHE_ROUTE_FAILED");
    }
    pthread_mutex_unlock(&lock);
    return previous_send(request, method);
}

JNIEXPORT jboolean JNICALL Java_com_newdawn_launcher_AccountPreloader_nativeInstall(JNIEnv *env, jclass clazz, jstring target) {
    (void)clazz;
    if (!target) return JNI_FALSE;
    const char *url = (*env)->GetStringUTFChars(env, target, NULL);
    if (!url) return JNI_FALSE;
    int valid = !strncmp(url, "http://127.0.0.1:", 17) && strlen(url) < sizeof(cache_url);
    const char *path = strchr(url + (valid ? 17 : 0), '/');
    if (!path || strncmp(path, "/account-cache/", 15) || strlen(path + 15) != 48) valid = 0;
    if (valid) {
        for (const char *p = url + 17; p < path; p++) if (*p < '0' || *p > '9') valid = 0;
        for (const char *p = path + 15; *p; p++) if (!((*p >= '0' && *p <= '9') || (*p >= 'a' && *p <= 'f'))) valid = 0;
    }
    if (valid) strcpy(cache_url, url);
    (*env)->ReleaseStringUTFChars(env, target, url);
    if (!valid) return JNI_FALSE;
    clock_gettime(CLOCK_MONOTONIC, &deadline); deadline.tv_sec += 110; consumed = 0;
    if (installed) return JNI_TRUE;
    library = dlopen("libil2cpp.so", RTLD_NOW);
    if (!library) return JNI_FALSE;
    void *symbol = dlsym(library, "il2cpp_domain_get"); Dl_info info;
    if (!symbol || !dladdr(symbol, &info) || !info.dli_fbase) return JNI_FALSE;
    unsigned char *entry = (unsigned char *)info.dli_fbase + 0x24e3d3c;
    uint32_t prefix[] = {0x58000051u, 0xd61f0220u};
    if (memcmp(entry, prefix, 8)) return JNI_FALSE;
    void *previous; memcpy(&previous, entry + 8, 8);
    Dl_info owner;
    if (!previous || !dladdr(previous, &owner) || !owner.dli_fname) return JNI_FALSE;
    const char *filename = strrchr(owner.dli_fname, '/'); filename = filename ? filename + 1 : owner.dli_fname;
    if (strcmp(filename, "libnd.so")) return JNI_FALSE;
    long page_size = sysconf(_SC_PAGESIZE);
    if (page_size < 4096 || (page_size & (page_size - 1))) return JNI_FALSE;
    uintptr_t start = (uintptr_t)entry & ~((uintptr_t)page_size - 1);
    uintptr_t end = ((uintptr_t)entry + 16 + page_size - 1) & ~((uintptr_t)page_size - 1);
    if (mprotect((void *)start, end - start, PROT_READ | PROT_WRITE | PROT_EXEC)) return JNI_FALSE;
    previous_send = previous; void *replacement = (void *)send_cached;
    memcpy(entry + 8, &replacement, 8);
    __builtin___clear_cache((char *)entry, (char *)entry + 16);
    if (mprotect((void *)start, end - start, PROT_READ | PROT_EXEC)) {
        memcpy(entry + 8, &previous, 8);
        __builtin___clear_cache((char *)entry, (char *)entry + 16);
        mprotect((void *)start, end - start, PROT_READ | PROT_EXEC);
        return JNI_FALSE;
    }
    installed = 1; LOG("ACCOUNT_CACHE_NATIVE_READY"); return JNI_TRUE;
}
