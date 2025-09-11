#pragma once
#include <asio.hpp>
#include <deque>
#include <memory>
#include <string>
#include <unordered_set>
using namespace std;

class WorldServerLink : public enable_shared_from_this<WorldServerLink>
{
public:
    using Executor = asio::io_context::executor_type;
    WorldServerLink(asio::io_context& io, string host, unsigned short port);

    void start();
    void registerUdpToken(const string& token, string actor, int ttl_ms);
    bool check_actor_exist(const string& actor);

private:
    void connect();
    void schedule_reconnect();
    void do_write();
    void on_close(asio::error_code ec);
    void start_read();
    void handle_line(string line);

private:
    asio::ip::tcp::socket socket_;
    asio::ip::tcp::resolver resolver_;
    asio::strand<Executor> strand_;
    asio::steady_timer reconnect_timer_;
    asio::steady_timer hb_timer_;
    string host_;
    unsigned short port_;
    deque<string> outq_;
    bool sending_ = false;
    int backoff_ms_ = 500;
    unordered_set<string> enter_actors_;
    string recv_buf_;
};
