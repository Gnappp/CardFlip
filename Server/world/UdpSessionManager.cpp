#include "UdpSessionManager.hpp"
#include "../common/common.hpp"

using namespace std;
using udp = asio::ip::udp;
using Clock = chrono::steady_clock;

UdpSessionManager::UdpSessionManager(asio::strand<Executor>& strand)
    : strand_(strand)
{
}

void UdpSessionManager::register_udp_token_async(string token, string actor, int ttl_ms)
{
    asio::post(strand_, [this, token = move(token), actor, ttl_ms]
        {
            token_table_[token] = { actor, Clock::now() + chrono::milliseconds(ttl_ms) };
            common::log("WORLD", "REGISTER token=" + token + " actor=" + actor);
        });
}

bool UdpSessionManager::on_udp_hello(const string& tok,  string actor, const udp::endpoint& ep)
{
    auto it = token_table_.find(tok);
    if (it == token_table_.end()) return false;
    if (it->second.actor.compare(actor)) return false;
    if (it->second.expires < Clock::now()) 
    {
        token_table_.erase(it); 
        return false; 
    }

    ep_to_actor_[ep] = actor;
    actors_.try_emplace(actor, ActorState{});
    token_table_.erase(it);
    return true;
}

bool UdpSessionManager::on_move(const udp::endpoint& ep, uint32_t seq, float x, float y)
{
    auto it = ep_to_actor_.find(ep);
    if (it == ep_to_actor_.end()) return false;

    auto& st = actors_[it->second];
    if (seq <= st.last_seq) return false;
    st.last_seq = seq;

    const float dx = x - st.x, dy = y - st.y;
    if (hypot(dx, dy) < 5.0f) 
    {
        st.x = x; 
        st.y = y; 
    }
    return true;
}

void UdpSessionManager::copy_snapshot(vector<pair<string, ActorState>>& out) const
{
    out.clear(); 
    out.reserve(actors_.size());
    for (const auto& kv : actors_) 
        out.emplace_back(kv.first, kv.second);
}

void UdpSessionManager::copy_endpoints(vector<udp::endpoint>& out) const
{
    out.clear(); 
    out.reserve(ep_to_actor_.size());
    for (const auto& kv : ep_to_actor_) 
        out.emplace_back(kv.first);
}
void UdpSessionManager::remove_actor(const string& actor)
{
    actors_.erase(actor);

    for (auto it = token_table_.begin(); it != token_table_.end(); )
        it = (it->second.actor == actor) ? token_table_.erase(it) : next(it);

    for (auto it = ep_to_actor_.begin(); it != ep_to_actor_.end(); )
        it = (it->second == actor) ? ep_to_actor_.erase(it) : next(it);
}

void UdpSessionManager::sweep()
{
    auto now = Clock::now();
    for (auto it = token_table_.begin(); it != token_table_.end(); )
        it = (it->second.expires <= now) ? token_table_.erase(it) : next(it);
}
