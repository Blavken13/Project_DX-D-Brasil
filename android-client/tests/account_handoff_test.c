/* Execute the actual Send wrapper with managed API test doubles. */
#include "../native/original/account_handoff.c"
#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
static struct { void *klass,*monitor; int32_t length; uint16_t chars[2048]; } address;
static int sends, constructors, setters, ctor_error, setter_error;
static int fake_uri, fake_text;
static void *fake_invoke(const void *method, void *receiver, void **args, void **error) {
    (void)receiver; (void)args; *error=NULL;
    if (method==get_uri) return &fake_uri;
    if (method==absolute_uri) return &address;
    if (method==uri_constructor) { constructors++; if(ctor_error)*error=&fake_text; return NULL; }
    if (method==set_uri) { setters++; if(setter_error)*error=&fake_text; return NULL; }
    assert(0); return NULL;
}
static void *fake_string(const char *text) { assert(!strcmp(text,cache_url)); return &fake_text; }
static void *fake_object(void *klass) { (void)klass; return &fake_uri; }
static void *fake_send(void *request,const void *method) { (void)method; sends++; return request; }
static void reset(const char *url) {
    memset(&address,0,sizeof(address)); address.length=(int32_t)strlen(url);
    for(int i=0;i<address.length;i++)address.chars[i]=(unsigned char)url[i];
    binding=1; consumed=0; sends=constructors=setters=ctor_error=setter_error=0;
    clock_gettime(CLOCK_MONOTONIC,&deadline);deadline.tv_sec+=100;
}
int main(void) {
    get_uri=(void *)1;absolute_uri=(void *)2;set_uri=(void *)3;uri_constructor=(void *)4;
    invoke=fake_invoke;string_new=fake_string;object_new=fake_object;previous_send=fake_send;
    strcpy(cache_url,"http://127.0.0.1:12345/account-cache/0123456789abcdef0123456789abcdef0123456789abcdef");
    int request;
    reset(GATEWAY); assert(send_cached(&request,NULL)==&request); assert(consumed&&sends==1&&setters==1);
    send_cached(&request,NULL);assert(sends==2&&setters==1);
    reset(GATEWAY "?version=1");send_cached(&request,NULL);assert(consumed&&setters==1);
    const char *other[]={"http://179.197.72.129:8190/sessions",GATEWAY "/other","http://untrusted/accounts","http://179.197.72.129:8190/knock"};
    for(unsigned i=0;i<sizeof(other)/sizeof(other[0]);i++) { reset(other[i]);send_cached(&request,NULL);assert(!consumed&&sends==1&&setters==0); }
    reset(GATEWAY);ctor_error=1;send_cached(&request,NULL);assert(!consumed&&setters==0&&sends==1);
    reset(GATEWAY);setter_error=1;send_cached(&request,NULL);assert(!consumed&&setters==1&&sends==1);
    reset(GATEWAY);deadline.tv_sec=0;send_cached(&request,NULL);assert(!consumed&&setters==0&&sends==1);
    reset(GATEWAY);send_cached(NULL,NULL);assert(!consumed&&setters==0&&sends==1);
    puts("PASS: 11 native account handoff cases (actual wrapper, managed APIs stubbed)");return 0;
}
