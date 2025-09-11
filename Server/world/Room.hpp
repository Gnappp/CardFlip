#pragma once
#include <iostream>
#include <asio.hpp>
#include <array>
#include <string>
#include <chrono>
#include <unordered_set>
#include <unordered_map>
#include <vector>

using namespace std;

struct Deck
{
	vector<int> cards;
	vector<bool> peek_cards;
};
enum class Phase 
{ 
	READY, PLAYING, END 
};
class Room
{
public:
	string roomId;
	string master;
	string challenger;
	string title;
	int rows = 0;
	int cols = 0;
	unordered_set<string> members;
	bool isReady;
	Deck deck;
	int firstIndex = -1;
	string turn;
	unordered_map<string, int> score;
	Phase phase = Phase::READY;

	void create_deck(uint32_t seed);
	void start_game();
	bool card_flip(const string& actor, int idx);
	bool peek_end(const string& actor);
	
};