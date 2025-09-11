#include "../world/TcpAcceptor.hpp"
#include "world.hpp"
#include "../common/common.hpp"
#include "../common/net.hpp"
#include "../common/protocol.hpp"
#include "TcpSession.hpp"
#include <asio.hpp>
#include <sstream>
#include <cstring>
#include <deque>

using asio::ip::tcp;
using namespace std;

TcpAcceptor::TcpAcceptor(asio::io_context& io, unsigned short port, World& world)
    : acc_(io, tcp::endpoint(tcp::v4(), port)), world_(world)
{
    accept();
    common::log("WORLD", "control listen TCP " + to_string(port));
}

void TcpAcceptor::accept()
{
    acc_.async_accept([this](error_code ec, tcp::socket s)
        {
            if (!ec) 
                make_shared<TcpSession>(move(s), world_)->start();
            accept();
        });
}