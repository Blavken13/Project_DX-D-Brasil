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
#include "runtime_compat.h"

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
    if (!br_hook_install(entry, expected, target_url, (void **)&original_url)) return 0;
    LOG("Recursos de K e Pia integrados ao APK");
    return 1;
}

int br_install_tutorial(void *library, uintptr_t base) {
    if (original_url) return 1;
    engine=base;
    string_new=dlsym(library,"il2cpp_string_new");
    if (!string_new) return 0;
    int resources=install();
    int raft=br_install_raft_k(library,engine);
    return resources && raft;
}
