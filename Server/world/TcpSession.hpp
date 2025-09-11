#pragma once
#include <asio.hpp>
#include <deque>
#include <memory>
#include <string>

using namespace std;

class World; 

class TcpSession : public enable_shared_from_this<TcpSession>
{
public:
    using tcp = asio::ip::tcp;

    TcpSession(tcp::socket s, World& w);
    void start();
    void write_line(string s);
    void on_close();

    string actorId_="";
    string roomId_="";
private:
    void read_line();
    void handle(const string& line);
    void write_more();

private:
    tcp::socket sock;
    asio::streambuf buf;
    World& world;
    deque<string> writeQueue;
};