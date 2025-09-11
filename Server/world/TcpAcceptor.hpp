#pragma once
#include <asio.hpp>
#include <memory>

struct World;

class TcpAcceptor
{
public:
    TcpAcceptor(asio::io_context& io, unsigned short port, World& world);

private:
    void accept();

    asio::ip::tcp::acceptor acc_;
    World& world_;
};