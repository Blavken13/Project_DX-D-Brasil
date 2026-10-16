/* On-device regression checks for credential routing and forwarding. */
#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include "../native/unity6_auth_bridge.c"

static const char *request_url;
static int added, forwarded;
static ManagedString *mock_string(const char *value) {
    size_t length = strlen(value);
    ManagedString *out = calloc(1, sizeof(*out) + length * 2);
    assert(out);
    out->length = (int32_t)length;
    for (size_t i = 0; i < length; i++) out->chars[i] = (uint16_t)value[i];
    return out;
}
static void *mock_invoke(Method method, void *object, void **args, void **exception) {
    (void)object; *exception = NULL;
    if (method == get_uri) return (void *)1;
    if (method == absolute_uri) return mock_string(request_url);
    assert(method == add_field && args);
    char key[32], token[64];
    assert(to_ascii(args[0], key, sizeof(key)) && !strcmp(key, "token"));
    assert(to_ascii(args[1], token, sizeof(token)) && valid_token(token));
    assert(!strcmp(token, auth_token));
    added++;
    return NULL;
}
static void *mock_forward(void *request, const void *method) {
    assert(request == (void *)7 && method == (void *)8);
    forwarded++;
    return (void *)9;
}
int main(void) {
    assert(valid_token("abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"));
    assert(!valid_token(NULL) && !valid_token("") && !valid_token("short"));
    assert(!valid_token("abcdefghijklmnopqrstuvwxyz0123456789ABCDEF!"));
    get_uri=(Method)1; absolute_uri=(Method)2; add_field=(Method)3;
    new_string=mock_string; runtime_invoke=mock_invoke; original_send=mock_forward;
    strcpy(auth_token,"abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG");
    const char *routes[] = {
        GATEWAY "/accounts", GATEWAY "/sessions", GATEWAY "/sessions?version=5.2.1",
        GATEWAY "/entry", GATEWAY "/players", GATEWAY "/assets/body",
        GATEWAY "/accounts/other", GATEWAY "/sessions-extra", GATEWAY ".attacker/accounts",
        "http://179.197.72.129:81900/accounts", "http://example.org/accounts"
    };
    for (unsigned i=0; i<sizeof(routes)/sizeof(routes[0]); i++) {
        int previous=added; request_url=routes[i];
        assert(authenticated_send((void *)7,(void *)8)==(void *)9);
        assert(added-previous==(i<3));
    }
    memset(auth_token,0,sizeof(auth_token)); request_url=GATEWAY "/accounts";
    assert(authenticated_send((void *)7,(void *)8)==(void *)9);
    assert(added==3 && forwarded==12);
    puts("PASS: auth token validation, exact origin and paths, unchanged request forwarding.");
    return 0;
}
