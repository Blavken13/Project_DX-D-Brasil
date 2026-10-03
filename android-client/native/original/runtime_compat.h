#pragma once
#include <stdint.h>
#include <stddef.h>
/* Hooks are installed synchronously, before UnityPlayer is created. Never call
 * this installer from a worker while game methods might be executing. */
int br_hook_install(void *entry, const unsigned char expected[16], void *replacement, void **original);
void br_stage(const char *stage);
int br_safe_profile(void);
int br_install_tutorial(void *library, uintptr_t base);
