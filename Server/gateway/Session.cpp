#include "../common/common.hpp"
#include "../common/net.hpp"
#include "Session.hpp"
#include "Server.hpp"
#include "WorldServerLinker.hpp"
#include <asio.hpp>
#include <memory>

Session::Session(tcp::socket s, Server& svr)
	: socket(move(s)),
	strand_state(asio::make_strand(socket.get_executor())),
	server(svr)
{
}
void Session::start()
{
	read_line();
}

string Session::rand_token()
{
	static mt19937_64 rng{ random_device{}() };
	static const char* abc = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
	string t(12, '0');
	for (auto& c : t)
		c = abc[rng() % 36];
	return t;
}

void Session::read_line()
{
	auto self = shared_from_this();
	asio::async_read_until(socket, buf, '\n',
		asio::bind_executor(strand_state, [this, self](error_code ec, size_t)
			{
				if (ec)
				{
					on_close(ec);
					common::log("GATEWAY", "client closed");
					return;
				}
				istream is(&buf);
				string line;
				getline(is, line);
				handle_line(line);
				read_line();
			}));
}

void Session::send_line(string s)
{
	if (s.empty() || s.back() != '\n') 
		s.push_back('\n');
	auto msg = make_shared<string>(move(s));
	auto self = shared_from_this();

	asio::post(strand_state, [this, self, msg]
		{
			outq_.push_back(msg);
			if (!sending)
			{
				sending = true;
				do_write();
			}
		}
	);
}

void Session::do_write()
{
	auto self = shared_from_this();
	asio::async_write(socket, asio::buffer(*outq_.front()),
		asio::bind_executor(strand_state, [this, self](error_code ec, size_t)
			{
				if (ec)
				{
					on_close(ec);
					return;
				}
				outq_.pop_front();
				if (!outq_.empty())
				{
					do_write();
				}
				else
				{
					sending = false;
				}
			}
		)
	);
}

void Session::on_close(error_code ec)
{
	common::log("GATEWAY", string("client closed: ") + ec.message());
	error_code ignore;
	socket.shutdown(tcp::socket::shutdown_both, ignore);
	socket.close(ignore);
}

void Session::handle_line(const string& line)
{
	if (line.rfind("LOGIN", 0) == 0)
	{
		auto m = net::kvparse(line.substr(5));
		string id = m["id"];
		string line = "LOGIN_OK token=" + login_token + " worldCount=" + to_string(server.worlds.size());
		send_line(line);
		for (const auto& [id, w] : server.worlds)
		{
			line.clear();
			line = "WORLD id=" + to_string(w.id) + " name=" + w.name + " udp_host=" + w.udp_host +
				" udp_port=" + to_string(w.udp_port);
			send_line(line);
		}

	}
	else if (line.rfind("ENTER_WORLD", 0) == 0)
	{
		auto m = net::kvparse(line.substr(12));
		string udp_token = rand_token();
		string host = "127.0.0.1";
		int port = 9001;
		string actor = m["actor"];

		if (server.worlds.find(stoi(m["world"])) != server.worlds.end())
		{
			if (!server.worlds[stoi(m["world"])].link->check_actor_exist(actor))
			{
				string line = "ERR_ID_EXSIT ";
				send_line(line);
				return;
			}
			server.worlds[stoi(m["world"])].link->registerUdpToken(udp_token, actor, 6000);
		}

		string line = "ENTER_OK udp_host=" + server.worlds[stoi(m["world"])].udp_host
			+ " udp_port=" + to_string(server.worlds[stoi(m["world"])].udp_port)
			+ " udp_token=" + udp_token + " actor=" + actor;

		send_line(line);
		common::log("GATEWAY", "ENTER actor=" + actor + " udp_token=" + udp_token);
	}
	else
	{
		common::log("GATEWAY", "unknown: " + line);
	}
}