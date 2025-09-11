#pragma once
#include <asio.hpp>
#include <unordered_map>
#include <vector>
#include <string>
#include <chrono>

using namespace std;

struct ActorState
{
    float x = 0.f, y = 0.f;
    uint32_t last_seq = 0;
};

// UDP endpoint 해시 (ip:port 를 키로)
struct UdpEndpointHash
{
    size_t operator()(const asio::ip::udp::endpoint& ep) const noexcept
    {
        return hash<string>{}(ep.address().to_string()) ^ (ep.port() << 16);
    }
};

class UdpSessionManager
{
public:
    using Executor = asio::io_context::executor_type;
    explicit UdpSessionManager(asio::strand<Executor>& strand); 

    bool on_udp_hello(const string& token, string actor, const asio::ip::udp::endpoint& ep);
    bool on_move(const asio::ip::udp::endpoint& ep, uint32_t seq, float x, float y);

    void register_udp_token_async(string token, string actor, int ttl_ms);
    void copy_snapshot(vector<pair<string, ActorState>>& out) const;
    void copy_endpoints(vector<asio::ip::udp::endpoint>& out) const;
    void remove_actor(const string& actor);
    void sweep();

private:
    struct TokenRow
    {
        string actor = "";
        chrono::steady_clock::time_point expires;
    };

    asio::strand<Executor>& strand_; // world
    unordered_map<string, TokenRow> token_table_; // token, TokenRow
    unordered_map<asio::ip::udp::endpoint, string, UdpEndpointHash> ep_to_actor_; // endpoint Hash, actorId
    unordered_map<string, ActorState> actors_; // actorId, ActorState
};
