#pragma once
#include "../common/common.hpp"
#include "../common/net.hpp"
#include <unordered_map>
#include <memory>
#include <random>
#include <string>
#include <deque>
#include <asio.hpp>

using  asio::ip::tcp;
using namespace std;
using Exec = asio::any_io_executor;

class Server;
class WorldServerLink;

class Session : public enable_shared_from_this<Session>
{
public:
	tcp::socket socket;
	asio::streambuf buf;
	string uid;
	string login_token;
	asio::strand<Exec> strand_state;
	deque<shared_ptr<string>> outq_;
	bool sending = false;
	Server& server;

	Session(tcp::socket s, Server& svr);

	void start();

	static string rand_token();

	void read_line();

	void send_line(string s);

	void do_write();

	void on_close(error_code ec);

	void handle_line(const string& line);
};