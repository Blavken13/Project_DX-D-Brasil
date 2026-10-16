#pragma once
#include <stdint.h>
#include <stddef.h>
/* Pure helpers shared by the Android bridge and host regression tests. */
static inline int lh_token_valid(const char *t) {
    if(!t)return 0;
    for(int i=0;i<43;i++)if(!((t[i]>='a'&&t[i]<='z')||(t[i]>='A'&&t[i]<='Z')||(t[i]>='0'&&t[i]<='9')||t[i]=='_'||t[i]=='-'))return 0;
    return t[43]==0;
}
static inline int lh_auth_route(const char *url) {
    static const char gateway[]="http://179.197.72.129:8190";
    if(!url)return 0;
    for(size_t i=0;i<sizeof(gateway)-1;i++)if(url[i]!=gateway[i])return 0;
    url+=sizeof(gateway)-1;
    const char *paths[]={"/accounts","/sessions"};
    for(int i=0;i<2;i++){size_t n=0;while(paths[i][n]&&url[n]==paths[i][n])n++;
        if(!paths[i][n]&&(url[n]==0||url[n]=='?'))return 1;}
    return 0;
}
static inline int lh_span(uintptr_t address,size_t length,size_t page,uintptr_t *start,size_t *span) {
    if(page<4096||(page&(page-1))||!length||address>UINTPTR_MAX-length||address+length>UINTPTR_MAX-(page-1))return 0;
    *start=address&~((uintptr_t)page-1);
    uintptr_t end=(address+length+page-1)&~((uintptr_t)page-1);
    *span=end-*start;return 1;
}
/* Architecture ADRP pages remain 4 KB even on a 16 KB Android kernel. */
static inline int lh_relocate(uint32_t word,uintptr_t pc,uint32_t *out,uintptr_t *literal,int index) {
    if(index<0||index>3)return 0;
    if((word&0x1f000000u)==0x10000000u){
        int64_t imm=((word>>5)&0x7ffffu)*4+((word>>29)&3);
        if(imm&(1<<20))imm-=1<<21;
        *literal=(word&0x80000000u)?(pc&~(uintptr_t)4095)+(imm*4096):pc+imm;
        *out=0x58000000u|((uint32_t)((64+index*8-index*4)/4)<<5)|(word&31);return 1;
    }
    if((word&0x7c000000u)==0x14000000u||(word&0x7e000000u)==0x34000000u||
       (word&0x7e000000u)==0x36000000u||(word&0xff000010u)==0x54000000u||
       (word&0x3b000000u)==0x18000000u||word==0xd65f03c0u)return 0;
    *out=word;return 1;
}
