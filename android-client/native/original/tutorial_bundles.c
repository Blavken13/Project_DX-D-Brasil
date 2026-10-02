/* Original Unity 2017.4.34f1 ARM64 only. Route four verified dependencies to the APK. */
#define _GNU_SOURCE
#include <android/log.h>
#include <dlfcn.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>

#define LOG(...) __android_log_print(ANDROID_LOG_INFO, "BRTutorial", __VA_ARGS__)
static uintptr_t engine;
extern int br_install_raft_k(void *, uintptr_t);
static void *(*string_new)(const char *);
static void *(*original_url)(void *, void *, void *, void *, const void *);
static const char *bundles[] = {
    "models$npc$materials$npc_k_body.mat.bundle",
    "models$npc$materials$npc_k_hair2.mat.bundle",
    "models$npc$materials$npc_k_skin2.mat.bundle",
    "particle$fx_materials$durango_common$fx_common_glow_05.mat.bundle"
};

static int text(void *string, char *output, size_t capacity) {
    if (!string) return 0;
    int length = *(int *)((char *)string + 16);
    if (length < 0 || (size_t)length >= capacity) return 0;
    const uint16_t *characters = (uint16_t *)((char *)string + 20);
    for (int i = 0; i < length; i++) {
        if (characters[i] > 127) return 0;
        output[i] = (char)characters[i];
    }
    output[length] = 0;
    return 1;
}

static void *target_url(void *self, void *name, void *crc, void *prefix, const void *method) {
    char logical[180];
    if (text(name, logical, sizeof(logical))) {
        for (size_t i = 0; i < sizeof(bundles) / sizeof(bundles[0]); i++) {
            if (strcmp(logical, bundles[i])) continue;
            /* Use Unity's Android streaming URL; no hardcoded package or filesystem path. */
            void *root = ((void *(*)(void *, const void *))(engine + 0x3117b5c))(NULL, NULL);
            char streaming[1024], url[1400];
            if (text(root, streaming, sizeof(streaming)) &&
                snprintf(url, sizeof(url), "%s/durango-br/tutorial/%s", streaming, logical) < (int)sizeof(url)) {
                void *result = string_new(url);
                if (result) { LOG("Recurso integrado: %s", logical); return result; }
            }
            break;
        }
    }
    return original_url(self, name, crc, prefix, method);
}

static int install(void) {
    /* CreateTargetUrl is inlined in the coroutine. Intercept its actual GetCrcName call. */
    void *entry = (void *)(engine + 0x157feb4);
    const unsigned char expected[16] = {0xf8,0x5f,0xbc,0xa9,0xf6,0x57,0x01,0xa9,
        0xf4,0x4f,0x02,0xa9,0xfd,0x7b,0x03,0xa9};
    if (memcmp(entry, expected, sizeof(expected))) { LOG("Versao nativa inesperada"); return 0; }
    long page_size = sysconf(_SC_PAGESIZE);
    if (page_size <= 0) return 0;
    void *stub = mmap(NULL, page_size, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (stub == MAP_FAILED) { LOG("Sem espaco para ponte; usando servidor"); return 0; }
    const uint32_t instructions[2] = {0x58000051, 0xd61f0220}; /* LDR X17,literal; BR X17 */
    /* The four saved instructions are stack stores, with no PC-relative operands. */
    memcpy(stub, entry, 16);
    memcpy((char *)stub + 16, instructions, 8);
    void *continuation = (char *)entry + 16;
    memcpy((char *)stub + 24, &continuation, 8);
    __builtin___clear_cache(stub, (char *)stub + 32);
    if (mprotect(stub, page_size, PROT_READ | PROT_EXEC)) { munmap(stub, page_size); return 0; }
    void *page = (void *)((uintptr_t)entry & ~((uintptr_t)page_size - 1));
    if (mprotect(page, page_size, PROT_READ | PROT_WRITE | PROT_EXEC)) { munmap(stub, page_size); return 0; }
    original_url = stub;
    void *replacement = target_url;
    memcpy(entry, instructions, 8);
    memcpy((char *)entry + 8, &replacement, 8);
    __builtin___clear_cache(entry, (char *)entry + 16);
    mprotect(page, page_size, PROT_READ | PROT_EXEC);
    LOG("Recursos de K e Pia integrados ao APK");
    return 1;
}

static void *worker(void *unused) {
    for (int i = 0; i < 1200; i++) {
        void *library = dlopen("libil2cpp.so", RTLD_NOW | RTLD_NOLOAD);
        if (library) {
            Dl_info info;
            void *init = dlsym(library, "il2cpp_init");
            string_new = dlsym(library, "il2cpp_string_new");
            if (init && string_new && dladdr(init, &info)) {
                engine = (uintptr_t)info.dli_fbase;
                install();
                br_install_raft_k(library, engine);
                return NULL;
            }
        }
        usleep(50000);
    }
    LOG("Inicializacao expirada; usando recursos do servidor");
    return NULL;
}

__attribute__((constructor)) static void initialize(void) {
    pthread_t thread;
    if (!pthread_create(&thread, NULL, worker, NULL)) pthread_detach(thread);
}
