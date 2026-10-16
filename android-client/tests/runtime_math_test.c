#include "../native/original/runtime_math.h"
#define EXPORT __declspec(dllexport)
EXPORT int test_token(const char *text){return lh_token_valid(text);}
EXPORT int test_route(const char *text){return lh_auth_route(text);}
EXPORT int test_span(uintptr_t address,size_t length,size_t page,uintptr_t *start,size_t *span){return lh_span(address,length,page,start,span);}
EXPORT int test_relocate(uint32_t word,uintptr_t pc,uint32_t *out,uintptr_t *literal,int index){return lh_relocate(word,pc,out,literal,index);}
