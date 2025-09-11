#pragma once
#include <string>

namespace proto
{
    inline constexpr const char* GW_REGISTER_UDP_TOKEN = "GW_REGISTER_UDP_TOKEN";

    inline string CreateUdpToken(string token, string actor, int ttl_ms)
    {
        return string(GW_REGISTER_UDP_TOKEN) + " token=" + token +
            " actor=" + actor +
            " ttl=" + to_string(ttl_ms) + "\n";
    }
}