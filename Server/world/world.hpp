#pragma once
#include <iostream>
#include <asio.hpp>
#include <array>
#include <string>
#include <chrono>
#include <unordered_set>
#include <vector>

using namespace std;

class UdpSessionManager;
class TcpSession;
class Room;

class World
{
public:
	using Executor = asio::io_context::executor_type;

	struct RoomSnapshot 
	{
		string roomId, master, challenger, title;
		int rows = 0, cols = 0;
		int phase;
	};

	unordered_map<string, weak_ptr<TcpSession>> ctrl_sessions_; // actor, session
	unordered_map<string, Room> rooms_; // roomId, Room
	uint64_t room_seq_ = 1;

public:
	World(asio::io_context& io, unsigned short udp_port, int tick_ms = 100);
	~World();

	void register_udp_token_async(string token, string actor, int ttl_ms);
	asio::strand<Executor>& state_strand() { return strand_state_; }

	// 세션 바인딩/해제 (TCP 컨트롤 세션 관리) 
	void bind_session(const string& actor, shared_ptr<TcpSession> s);
	void on_disconnect(const string& actor, TcpSession* s);
	void bind_gateway_session(shared_ptr<TcpSession>& s);

	// 룸/게임 도메인
	string create_room(const string& master, const string& title, int rows, int cols);
	bool join_room(const string& roomId, const string& actor);
	bool change_ready(const string& roomId, const bool& isReady);
	bool check_ready(const string& roomId);
	bool game_start(const string& roomId);
	bool game_peek_end(const string& roomId, const string& actor);
	bool flip_card(const string& roomId, const string& actor, int index);
	bool check_end_game(const string& roomId);
	int check_exit_room_count(const string& roomId);
	bool delete_room(const string& roomId);
	bool check_exit_room_master(const string& roomId, const string& actor);
	bool change_room_master(const string& roomId);
	bool exit_room_challenger(const string& roomId);
	bool change_rule(const string& roomId, const string& master, int cols, int rows);
	int change_room_phase(const string& roomId, const int phase);

	RoomSnapshot snapshot(const string& roomId) const;

	// 브로드캐스트 및 룸 캐스트 (제어 이벤트)
	void broadcast_create_room(const string& roomId);
	void cast_enter_room(const string& roomId, const RoomSnapshot snap);  
	void broadcast_enter_room(const string& roomId, const string& roomTitle);
	void cast_change_ready(const string& roomId, const bool& isReady);
	void cast_game_start(const string& roomId);
	void cast_game_peek_end(const string& roomId);
	void cast_flip_result(const string& roomId, int index);
	void cast_end_game(const string& roomId, int index);
	void broadcast_delete_room(const string& roomId, const string& master);
	void broadcast_change_room_master(const string& roomId);
	void broadcast_exit_room(const string& roomId);
	void cast_exit_room(const string& roomId, const string& master, const string& exitActor);
	void cast_change_rule(const string& roomId);
	void cast_forced_end_game(const string& roomId);
	void broadcast_exit_server(const string& actor,const string& roomId, TcpSession* s);
    
private:
	void recv();
	void schedule_tick();
	void schedule_sweep();
	void broadcast_snapshot_fast();
	void tcp_heart_beat();

	void send_tcp_to(const string& actor, const string& line);
	void send_tcp_to_room(const string& roomId, const string& line);
	void send_tcp_to_all(const string& line);
	void send_to_gateway(const string& line);
private:
	// I/O
	asio::io_context& io_;
	asio::ip::udp::socket sock_;
	asio::ip::udp::endpoint remote_;
	array<char, 1500> buf_{};

	// 타이머
	asio::steady_timer  tick_;
	asio::steady_timer sweep_timer_;
	int tick_ms_;

	// 직렬화용 strand
	asio::strand<Executor> strand_state_;
	asio::strand<Executor> strand_tx_;

	// 세션/토큰 관리
	unique_ptr<UdpSessionManager> sessions_; 
	weak_ptr<TcpSession> gateway_session_;
};
