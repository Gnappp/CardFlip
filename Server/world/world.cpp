#include "../common/common.hpp"
#include "../common/net.hpp"
#include "world.hpp"
#include "UdpSessionManager.hpp"
#include "TcpAcceptor.hpp"
#include "TcpSession.hpp"
#include "Room.hpp"
#include <unordered_map>
#include <memory>
#include <array>
#include <cmath>
#include <string>
#include <asio.hpp>
#include <sstream>
#include <iomanip>

using asio::ip::udp;
using Executor = asio::io_context::executor_type;

World::World(asio::io_context& io, unsigned short udp_port, int tick_ms)
	: io_(io)
	, sock_(io, udp::endpoint(udp::v4(), udp_port))
	, tick_(io)
	, sweep_timer_(io)
	, tick_ms_(tick_ms)
	, strand_state_(io.get_executor())
	, strand_tx_(io.get_executor())
	, sessions_(make_unique<UdpSessionManager>(strand_state_))
{
	recv();
	schedule_tick();
	schedule_sweep();
}

World::~World() = default;

void World::register_udp_token_async(string token, string actor, int ttl_ms)
{
	sessions_->register_udp_token_async(move(token), actor, ttl_ms);
}

void World::recv()
{
	sock_.async_receive_from(asio::buffer(buf_), remote_,
		asio::bind_executor(strand_state_, [this](error_code ec, size_t n)
			{
				if (!ec && n > 0)
				{
					string s(buf_.data(), n);
					if (s.rfind("HELLO", 0) == 0)
					{
						auto m = net::kvparse(s.substr(6));
						const string tok = m["token"];
						string actor = m["actor"];
						sessions_->on_udp_hello(tok, actor, remote_);
					}
					else if (s.rfind("MOVE", 0) == 0)
					{
						auto m = net::kvparse(s.substr(5));
						uint32_t seq = m.count("seq") ? static_cast<uint32_t>(stoul(m["seq"])) : 0;
						float x = m.count("x") ? stof(m["x"]) : 0.f;
						float y = m.count("y") ? stof(m["y"]) : 0.f;
						sessions_->on_move(remote_, seq, x, y);
					}
				}
				recv();
			}
		)
	);
}

void World::schedule_tick()
{
	tick_.expires_after(chrono::milliseconds(tick_ms_));
	tick_.async_wait(asio::bind_executor(strand_state_, [this](error_code)
		{
			broadcast_snapshot_fast();
			tcp_heart_beat();
			schedule_tick();
		}
	)
	);
}

void World::schedule_sweep()
{
	sweep_timer_.expires_after(chrono::seconds(1));
	sweep_timer_.async_wait(asio::bind_executor(strand_state_, [this](error_code)
		{
			sessions_->sweep();
			schedule_sweep();
		}
	)
	);
}

void World::broadcast_snapshot_fast()
{
	vector<pair<string, ActorState>> actors;
	vector<udp::endpoint> eps;
	sessions_->copy_snapshot(actors);
	sessions_->copy_endpoints(eps);

	asio::post(strand_tx_, [this, actors = move(actors), eps = move(eps)]
		{
			string payload;
			payload.reserve(actors.size() * 40);
			for (const auto& kv : actors)
			{
				payload += "ACTOR_POS id=" + kv.first
					+ " x=" + to_string(kv.second.x)
					+ " y=" + to_string(kv.second.y) + "\n";
			}
			auto msg = make_shared<string>(move(payload));
			for (const auto& ep : eps)
				sock_.async_send_to(asio::buffer(*msg), ep, [msg](auto, auto) {});
		}
	);
}

void World::tcp_heart_beat()
{
	string line = "BROADCAST_HEART_BEAT";
	send_tcp_to_all(line);
}

inline void World::send_tcp_to(const string& actor, const string& line)
{
	auto it = ctrl_sessions_.find(actor);
	if (it == ctrl_sessions_.end()) return;
	if (auto s = it->second.lock())
	{
		s->write_line(line);
	}
}

inline void World::send_tcp_to_room(const string& roomId, const string& line)
{
	auto rit = rooms_.find(roomId);
	if (rit == rooms_.end()) return;
	for (const auto& actor : rit->second.members)
	{
		send_tcp_to(actor, line);
	}
}

inline void World::send_tcp_to_all(const string& line)
{
	for (auto& [actor, wp] : ctrl_sessions_)
	{
		if (auto s = wp.lock())
			s->write_line(line);
	}
}

void World::send_to_gateway(const string& line)
{
	if (auto p = gateway_session_.lock())
		p->write_line(move(line));
}

void World::bind_session(const string& actor, shared_ptr<TcpSession> s)
{
	ctrl_sessions_[actor] = move(s);
}

void World::on_disconnect(const string& actor, TcpSession* s)
{
	auto it = ctrl_sessions_.find(actor);
	if (it != ctrl_sessions_.end())
	{
		if (auto cur = it->second.lock())
		{
			if (cur.get() == s) ctrl_sessions_.erase(it);
		}
		else {
			ctrl_sessions_.erase(it);
		}
	}
}
void World::bind_gateway_session(shared_ptr<TcpSession>& s)
{
	gateway_session_ = s;
}

// 룸/게임 도메인
string World::create_room(const string& master, const string& title, int rows, int cols)
{
	ostringstream oss;
	oss << "r" << setw(6) << setfill('0') << room_seq_++;
	string roomId = oss.str();

	Room r;
	r.roomId = roomId;
	r.master = master;
	r.title = title;
	r.rows = rows;
	r.cols = cols;
	r.members.insert(master);

	rooms_.emplace(roomId, move(r));
	return roomId;
}

bool World::join_room(const string& roomId, const string& actor)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	it->second.members.insert(actor);
	it->second.challenger = actor;
	return true;
}
bool World::change_ready(const string& roomId, const bool& isReady)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	it->second.isReady = isReady;
	return true;
}
bool World::check_ready(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	return it->second.isReady;
}
bool World::game_start(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	it->second.start_game();
	return true;
}
bool World::game_peek_end(const string& roomId, const string& actor)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	if (it->second.phase != Phase::PLAYING) return false;
	return it->second.peek_end(actor);
}
bool World::flip_card(const string& roomId, const string& actor, int index)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;

	return it->second.card_flip(actor, index);
}
bool World::check_end_game(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	return it->second.phase == Phase::END;
}
int World::check_exit_room_count(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	return it->second.members.size();
}
bool World::delete_room(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	rooms_.erase(roomId);
	return true;
}
bool World::check_exit_room_master(const string& roomId, const string& actor)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	return it->second.master == actor;
}
bool World::change_room_master(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	it->second.score.erase(it->second.master);
	it->second.members.erase(it->second.master);
	it->second.master = it->second.challenger;
	it->second.challenger = "";
	return true;
}
bool World::exit_room_challenger(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	it->second.score.erase(it->second.challenger);
	it->second.members.erase(it->second.challenger);
	it->second.challenger = "";
	return true;
}
bool World::change_rule(const string& roomId, const string& master, int cols, int rows)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return false;
	if (it->second.master != master) return false;
	it->second.cols = cols;
	it->second.rows = rows;
	return true;
}
int World::change_room_phase(const string& roomId, const int phase)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return 0;
	it->second.phase = (Phase)phase;
	return phase;
}

World::RoomSnapshot World::snapshot(const string& roomId) const
{
	RoomSnapshot snap;
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return snap;
	const Room& r = it->second;
	snap.roomId = r.roomId;
	snap.master = r.master;
	snap.challenger = r.challenger;
	snap.title = r.title;
	snap.rows = r.rows;
	snap.cols = r.cols;
	snap.phase = (int)r.phase;
	return snap;
}

void World::broadcast_create_room(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;

	string line = "BROADCAST_CREATE_ROOM roomId=" + r.roomId + " master=" + r.master + " title=" + r.title;
	send_tcp_to_all(line);
}

void World::cast_enter_room(const string& roomId, const RoomSnapshot snap)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;

	string line = "CAST_ENTER_ROOM roomId=" + roomId + " master=" + snap.master + " challenger=" + snap.challenger +
		" title=" + snap.title + " rows=" + to_string(snap.rows) + " cols=" + to_string(snap.cols);
	send_tcp_to_room(roomId, line);
}

void World::broadcast_enter_room(const string& roomId, const string& roomTitle)
{
	string line = "BROADCAST_ENTER_ROOM roomId=" + roomId + " title=" + roomTitle;
	send_tcp_to_all(line);
}

void World::cast_change_ready(const string& roomId, const bool& isReady)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;
	string ready = isReady ? "True" : "False";
	string line = "CAST_CHANGE_READY roomId=" + r.roomId + " isReady=" + ready;
	send_tcp_to_room(roomId, line);
}

void World::cast_game_start(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;
	string line = "CAST_GAME_START roomId=" + r.roomId;
	string s = " cards=";
	for (size_t i = 0; i < r.deck.cards.size(); i++) {
		if (i) s += ",";
		s += to_string(r.deck.cards[i]);
	}
	line += s;
	line += " dur=500 all_dur=500 phase=1";
	send_tcp_to_room(roomId, line);
}

void World::cast_game_peek_end(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;
	string line = "CAST_FIRST_FLIP_END roomId=" + r.roomId + " turn=" + r.turn +
		" masterScore=" + to_string(r.score.at(r.master)) + " challengerScore=" + to_string(r.score.at(r.challenger));
	send_tcp_to_room(roomId, line);
}

void World::cast_flip_result(const string& roomId, int index)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;
	int value = r.deck.cards[index];
	string line = "CAST_FLIP_RESULT roomId=" + roomId + " index=" + to_string(index) + " card=" + to_string(value) +
		" turn=" + r.turn + " masterScore=" + to_string(r.score.at(r.master)) +
		" challengerScore=" + to_string(r.score.at(r.challenger));
	send_tcp_to_room(roomId, line);
}

void World::cast_end_game(const string& roomId, int index)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;

	int value = r.deck.cards[index];
	string winner = r.score.at(r.master) > r.score.at(r.challenger) ? r.master : r.challenger;
	if (r.score.at(r.master) == r.score.at(r.challenger))
		winner = "-";

	string line = "CAST_END_GAME roomId=" + roomId + " index=" + to_string(index) + " card=" + to_string(value) +
		" turn=" + r.turn + " masterScore=" + to_string(r.score.at(r.master)) +
		" challengerScore=" + to_string(r.score.at(r.challenger)) + " winner=" + winner;
	send_tcp_to_room(roomId, line);
}

void World::broadcast_delete_room(const string& roomId, const string& master)
{
	string line = "BROADCAST_DELETE_ROOM roomId=" + roomId + " master=" + master;
	send_tcp_to_all(line);
}

void World::broadcast_change_room_master(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;
	string line = "BROADCAST_CHANGE_ROOM_MASTER roomId=" + roomId + " master=" + r.master;
	send_tcp_to_all(line);
}
void World::broadcast_exit_room(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;
	string line = "BROADCAST_EXIT_ROOM roomId=" + roomId + " title=" + r.title;
	send_tcp_to_all(line);
}

void World::cast_exit_room(const string& roomId, const string& master, const string& exitActor)
{
	string line = "CAST_EXIT_ROOM roomId=" + roomId + " master=" + master + " exitActor=" + exitActor;
	send_tcp_to_room(roomId, line);
}

void World::cast_change_rule(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;
	string line = "CAST_CHANGE_RULE roomId=" + roomId + " cols=" + to_string(r.cols) + " rows=" + to_string(r.rows);
	send_tcp_to_room(roomId, line);
}

void World::cast_forced_end_game(const string& roomId)
{
	auto it = rooms_.find(roomId);
	if (it == rooms_.end()) return;
	const Room& r = it->second;

	int phase = change_room_phase(roomId, (int)Phase::END);

	string line = "CAST_FORCED_END_GAME roomId=" + roomId + " phase=" + to_string(phase);
	send_tcp_to_room(roomId, line);
}

void World::broadcast_exit_server(const string& actor, const string& roomId, TcpSession* s)
{
	auto it = rooms_.find(roomId);
	if (it != rooms_.end())
	{
		const Room& r = it->second;
		if (r.phase == Phase::PLAYING)
			cast_forced_end_game(roomId);

		cast_exit_room(roomId, r.master, actor);
		if (check_exit_room_master(roomId, actor))
		{
			change_room_master(roomId);
			broadcast_change_room_master(roomId);
		}
		else
		{
			exit_room_challenger(roomId);
			broadcast_exit_room(roomId);
		}

		if (check_exit_room_count(roomId) == 0)
		{
			if (r.master == actor)
			{
				broadcast_delete_room(roomId, actor);
				delete_room(roomId);
			}
		}
	}
	on_disconnect(actor, s);
	string line = "BROADCAST_EXIT_SERVER actor=" + actor;
	send_tcp_to_all(line);
	sessions_->remove_actor(actor);
	send_to_gateway("EXIT_USER id=" + actor + "\n");
}

int main(int argc, char* argv[])
{
	common::title("WORLD");
	int tcp = common::to_int(argc > 1 ? argv[1] : nullptr, 7100);
	int udp_port = common::to_int(argc > 2 ? argv[2] : nullptr, 9001);

	asio::io_context io;
	World w(io, static_cast<unsigned short>(udp_port));
	TcpAcceptor tm(io, tcp, w);
	int n = max(1u, thread::hardware_concurrency());
	net::run_io_threads(io, n);
	return 0;
}