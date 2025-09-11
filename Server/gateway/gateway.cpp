#include "../common/common.hpp"
#include "../common/net.hpp"
#include "Server.hpp"
#include "Session.hpp"
#include <unordered_map>
#include <memory>
#include <random>
#include <string>
#include <deque>

using  asio::ip::tcp;
using namespace std;
using Executor = asio::io_context::executor_type;
using Exec = asio::any_io_executor;


int main(int argc, char* argv[])
{
	common::title("GATEWAY");
	int port = common::to_int(argc > 1 ? argv[1] : nullptr, 7000);

	asio::io_context io;
	Server s(io, static_cast<unsigned short>(port));

	int n = max(1u, thread::hardware_concurrency());
	net::run_io_threads(io, n);
	return 0;
}