#pragma once
#define ASIO_STANDALONE
#include <asio.hpp>
#include <thread>
#include <string>
#include <unordered_map>
#include <sstream>

namespace net 
{

	inline void run_io_threads(asio::io_context& io, int n)
	{
		vector<thread> ths; 
		ths.reserve(n);
		for (int i = 0; i < n; i++)
		{
			ths.emplace_back([&] { io.run(); });
		}
		for (auto& t : ths)
			t.join();
	}

	inline unordered_map<string, string> kvparse(const string& s)
	{
		unordered_map<string, string> m;
		istringstream is(s);
		string tok;
		while (is >> tok)
		{
			auto p = tok.find('=');
			if (p != string::npos)
			{
				m.emplace(tok.substr(0, p), tok.substr(p + 1));
			}
		}
		return m;
	}

	inline pair<string, unordered_map<string, string>> parse_kv(const string& line) {
		istringstream iss(line);
		string cmd;
		iss >> cmd; 

		unordered_map<string, string> kv;
		string token;
		while (iss >> token) 
		{
			auto pos = token.find('=');
			if (pos != string::npos) {
				auto k = token.substr(0, pos);
				auto v = token.substr(pos + 1);
				kv.emplace(move(k), move(v));
			}
		}
		return { move(cmd), move(kv) };
	}

}