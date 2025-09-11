#include "WorldServerLinker.hpp"
#include "../common/common.hpp"
#include "../common/protocol.hpp"
#include "../common/net.hpp"

using namespace std;
using asio::ip::tcp;

WorldServerLink::WorldServerLink(asio::io_context& io, string host, unsigned short port)
	: socket_(io)
	, resolver_(io)
	, strand_(io.get_executor())
	, reconnect_timer_(io)
	, hb_timer_(io)
	, host_(move(host))
	, port_(port)
{
}

void WorldServerLink::start()
{
	connect();
}

void WorldServerLink::connect()
{
	auto self = shared_from_this();

	resolver_.async_resolve(host_, to_string(port_),
		asio::bind_executor(strand_, [this, self](error_code ec, tcp::resolver::results_type results)
			{
				if (ec)
				{
					common::log("GATEWAY", "world resolve fail: " + ec.message());
					schedule_reconnect();
					return;
				}
				asio::async_connect(socket_, results,
					asio::bind_executor(strand_, [this, self](error_code ec2, const tcp::endpoint&)
						{
							if (ec2)
							{
								common::log("GATEWAY", "world connect fail: " + ec2.message());
								schedule_reconnect();
								return;
							}
							backoff_ms_ = 500;
							common::log("GATEWAY", "world connected");
							start_read();
						}
					));
			}
		));
}
void WorldServerLink::start_read()
{
	auto self = shared_from_this();
	asio::async_read_until(socket_, asio::dynamic_string_buffer(recv_buf_), '\n',
		asio::bind_executor(strand_, [this, self](error_code ec, size_t n)
			{
				if (ec)
				{
					on_close(ec);
					return;
				}
				size_t pos = 0;
				while (true)
				{
					auto eol = recv_buf_.find('\n', pos);
					if (eol == string::npos) break;

					string line = recv_buf_.substr(pos, eol - pos);
					if (!line.empty() && line.back() == '\r') line.pop_back();

					handle_line(move(line));
					pos = eol + 1;
				}
				recv_buf_.erase(0, pos);
				start_read();
			}));
}
void WorldServerLink::handle_line(string line)
{
	if (line.empty()) return;

	if (line.rfind("EXIT_USER", 0) == 0)
	{
		auto m = net::kvparse(line.substr(5));
		string id = m["id"];
		enter_actors_.erase(id);
		common::log("WorldServerLinker", "Delete Id = " + id);
	}
}
void WorldServerLink::schedule_reconnect()
{
	backoff_ms_ = min(backoff_ms_ * 2, 5000);
	reconnect_timer_.expires_after(chrono::milliseconds(backoff_ms_));
	reconnect_timer_.async_wait(asio::bind_executor(strand_, [this](error_code)
		{
			asio::error_code ignore;
			socket_.close(ignore);
			connect();
		}));
}


void WorldServerLink::registerUdpToken(const string& token, string actor, int ttl_ms)
{
	string line = proto::CreateUdpToken(token, actor, ttl_ms);
	enter_actors_.insert(actor);
	auto self = shared_from_this();
	asio::post(strand_, [this, self, line = move(line)]
		{
			outq_.push_back(line);
			if (!sending_)
			{
				sending_ = true;
				do_write();
			}
		});
}

void WorldServerLink::do_write()
{
	auto self = shared_from_this();
	asio::async_write(socket_, asio::buffer(outq_.front()),
		asio::bind_executor(strand_, [this, self](error_code ec, size_t)
			{
				if (ec)
				{
					on_close(ec);
					return;
				}
				outq_.pop_front();
				if (!outq_.empty())
					do_write();
				else
					sending_ = false;
			}));
}

bool WorldServerLink::check_actor_exist(const string& actor)
{
	return !(enter_actors_.find(actor) != enter_actors_.end());
}

void WorldServerLink::on_close(error_code ec)
{
	common::log("GATEWAY", "world link closed: " + ec.message());
	asio::error_code ignore;
	socket_.close(ignore);
	schedule_reconnect();
}
