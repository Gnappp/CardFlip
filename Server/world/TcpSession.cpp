#include "TcpSession.hpp"
#include "World.hpp"  
#include "../common/net.hpp"
#include "../common/protocol.hpp"
#include "../common/common.hpp"
#include <numeric>
#include <asio.hpp>
#include <istream>
#include <ostream>
#include <utility>
#include <cstring>

using asio::ip::tcp;
using namespace std;

TcpSession::TcpSession(tcp::socket s, World& w)
	: sock(move(s)), world(w)
{
}
void TcpSession::start()
{
	read_line();
}

void TcpSession::read_line()
{
	auto self = shared_from_this();
	asio::async_read_until(sock, buf, '\n', [this, self](error_code ec, size_t)
		{
			if (ec)
			{
				on_close();
				return;
			}
			istream is(&buf);
			string line;
			getline(is, line);
			if (!line.empty() && line.back() == '\r')
				line.pop_back();

			if (!line.empty())
				handle(line);

			read_line();
		});
}

void TcpSession::handle(const string& line)
{
	auto [cmd, kv] = net::parse_kv(line);
	if (cmd == "HELLO")
	{
		auto it = kv.find("actor");
		if (it == kv.end() || it->second.empty())
		{
			write_line("ERR code=BAD_HELLO");
			return;
		}
		actorId_ = it->second;
		asio::post(world.state_strand(), [self = shared_from_this(), this]
			{
				world.bind_session(actorId_, self);
				write_line("HELLO_OK actor=" + actorId_);
			});
		return;
	}

	if (cmd == proto::GW_REGISTER_UDP_TOKEN)
	{
		string tok = kv["token"];
		string actor = kv["actor"];
		int ttl = kv.count("ttl") ? stoi(kv["ttl"]) : 60000;

		world.register_udp_token_async(move(tok), actor, ttl);
		static const string ok = "OK\n";
		asio::async_write(sock, asio::buffer(ok), [](auto, auto) {});
		auto self = shared_from_this();
		world.bind_gateway_session(self);
		return;
	}

	if (cmd == "REQ_CREATE_ROOM")
	{
		string title = kv["title"];
		int rows = stoi(kv["rows"]);
		int cols = stoi(kv["cols"]);
		string actor = actorId_;

		asio::post(world.state_strand(), [this, self = shared_from_this(), title = move(title), rows, cols]()
			{
				auto roomId = world.create_room(actorId_, title, rows, cols);
				write_line("RES_CREATE_ROOM roomId=" + roomId + " master=" + actorId_ + " title=" + title);
				world.broadcast_create_room(roomId);
				roomId_ = roomId;
			});
		return;
	}

	if (cmd == "REQ_ENTER_ROOM")
	{
		string roomId = kv["roomId"];
		roomId_ = roomId;
		asio::post(world.state_strand(), [this, self = shared_from_this(), roomId = move(roomId)]()
			{
				if (!world.join_room(roomId, actorId_))
				{
					write_line("ERR code=ROOM_NOT_FOUND roomId=" + roomId);
					return;
				}
				auto snap = world.snapshot(roomId);
				world.cast_enter_room(roomId, snap);
				world.broadcast_enter_room(roomId, snap.title);
			});
		return;
	}
	if (cmd == "REQ_CHANGE_READY")
	{
		string roomId = kv["roomId"];
		bool isReady = kv["isReady"] == "True";

		asio::post(world.state_strand(), [this, self = shared_from_this(), roomId = move(roomId), isReady]()
			{
				world.change_ready(roomId, isReady);
				world.cast_change_ready(roomId, isReady);
			});
		return;
	}
	if (cmd == "REQ_GAME_START")
	{
		string roomId = kv["roomId"];
		asio::post(world.state_strand(), [this, self = shared_from_this(), roomId = move(roomId)]()
			{
				if (world.check_ready(roomId))
				{
					world.game_start(roomId);
					world.cast_game_start(roomId);
				}
			});
		return;
	}
	if (cmd == "REQ_FIRST_FLIP_END")
	{
		string roomId = kv["roomId"];
		string actor = kv["actor"];

		auto self = shared_from_this();
		asio::post(world.state_strand(), [this, self, roomId = move(roomId), actor = move(actor)] {
			if (world.game_peek_end(roomId, actor))
			{
				world.cast_game_peek_end(roomId);
			}
			});
		return;
	}
	if (cmd == "REQ_FLIP")
	{
		string roomId = kv["roomId"];
		string actor = kv["actor"];
		int idx = stoi(kv["index"]);
		asio::post(world.state_strand(), [this, self = shared_from_this(), roomId = move(roomId), actor, idx]()
			{
				world.flip_card(roomId, actor, idx);
				if (world.check_end_game(roomId))
					world.cast_end_game(roomId, idx);
				else
					world.cast_flip_result(roomId, idx);
			});
		return;
	}
	if (cmd == "REQ_ROOM_EXIT")
	{
		string roomId = kv["roomId"];
		string actor = kv["actor"];
		asio::post(world.state_strand(), [this, self = shared_from_this(), roomId = move(roomId), actor]()
			{
				auto snap = world.snapshot(roomId);

				if (snap.phase == 1)
					world.cast_forced_end_game(roomId);

				world.cast_exit_room(roomId, snap.master, actor);
				if (world.check_exit_room_master(roomId, actor))
				{
					world.change_room_master(roomId);
					world.broadcast_change_room_master(roomId);
				}
				else
				{
					world.exit_room_challenger(roomId);
					world.broadcast_exit_room(roomId);
				}

				if (world.check_exit_room_count(roomId) == 0)
				{
					if (snap.master == actor)
					{
						world.delete_room(roomId);
						world.broadcast_delete_room(roomId, actor);
					}
				}
				roomId_ = "";
			});
		return;
	}
	if (cmd == "REQ_CHANGE_RULE")
	{
		string roomId = kv["roomId"];
		string master = kv["master"];
		int cols = stoi(kv["cols"]);
		int rows = stoi(kv["rows"]);
		asio::post(world.state_strand(), [this, self = shared_from_this(), roomId = move(roomId), master = move(master), cols, rows]()
			{
				if (world.change_rule(roomId, master, cols, rows))
					world.cast_change_rule(roomId);
			});
		return;
	}
	static const string err = "ERR code=UNKNOWN\n";
	asio::async_write(sock, asio::buffer(err), [](auto, auto) {});
}

void TcpSession::write_line(string s)
{
	s.push_back('\n');
	bool writing = !writeQueue.empty();
	writeQueue.emplace_back(move(s));
	if (writing) 
		return;

	auto self = shared_from_this();
	asio::async_write(sock, asio::buffer(writeQueue.front()), [this, self](error_code ec, size_t)
		{
			if (ec)
			{
				on_close(); 
				return;
			}
			writeQueue.pop_front();
			if (!writeQueue.empty()) 
				write_more();
		});
}

void TcpSession::write_more()
{
	auto self = shared_from_this();
	asio::async_write(sock, asio::buffer(writeQueue.front()), [this, self](error_code ec, size_t)
		{
			if (ec) 
			{
				on_close();
				return; 
			}
			writeQueue.pop_front();
			if (!writeQueue.empty())
				write_more();
		});
}

void TcpSession::on_close()
{
	asio::post(world.state_strand(), [this, self = shared_from_this(), roomId = move(roomId_), actor = move(actorId_)]()
		{
			common::log("WORLD", "on_close " + actor);
			if (actor != "")
				world.broadcast_exit_server(actor, roomId, self.get());
		});

	error_code ec;
	sock.close(ec);
}


