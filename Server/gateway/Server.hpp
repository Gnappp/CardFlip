#pragma once
#include "../common/common.hpp"
#include "../common/net.hpp"
#include <unordered_map>
#include <memory>
#include <random>
#include <string>
#include <deque>

using  asio::ip::tcp;
using namespace std;

class Session;
class WorldServerLink;

struct WorldInfo
{
	int id;
	string name;
	string udp_host;
	int udp_port;
	shared_ptr<WorldServerLink> link; 
};

class Server
{
public:
	asio::io_context& io;
	tcp::acceptor acc;

	shared_ptr<WorldServerLink> world_; 

	unordered_map<int, WorldInfo> worlds;

	Server(asio::io_context& io_, unsigned short port);

	void accept();
};
