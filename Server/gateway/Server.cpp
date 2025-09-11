#include "Server.hpp"
#include "Session.hpp" 
#include "WorldServerLinker.hpp"
#include <asio.hpp>
#include <memory>

Server::Server(asio::io_context& io_, unsigned short port)
	: io(io_), acc(io_, tcp::endpoint(tcp::v4(), port))
{
	worlds.emplace(1, WorldInfo{ 1, "Test1", "127.0.0.1", 9001,	make_shared<WorldServerLink>(io, "127.0.0.1", 7100) });
	accept();
	for (auto& [id, w] : worlds) w.link->start();
}

void Server::accept()
{
	acc.async_accept([this](error_code ec, tcp::socket s)
		{
			if (!ec)
			{
				auto sp = make_shared<Session>(move(s), *this); 
				sp->start();
			}
			accept();
		});
}