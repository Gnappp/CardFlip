#include "Room.hpp"
#include "../common/common.hpp"
#include <iostream>
#include <vector>
#include <numeric>
#include <sstream>
#include <random>
using namespace std;

void Room::create_deck(uint32_t seed)
{
    Deck out;
    const int N = rows * cols;
    if (N % 2) 
        throw runtime_error("board size odd");
    const int pairCount = N / 2;

    vector<int> values; 
    values.reserve(N);
    for (int v = 0; v < pairCount; ++v) 
    { 
        values.push_back(v); 
        values.push_back(v); 
    }

    mt19937 rng(seed ? seed : mt19937::result_type(random_device{}()));
    //shuffle(values.begin(), values.end(), rng);

    out.cards = move(values); 
    for (int i = 0; i < N; i++)
        out.peek_cards.push_back(false);
    deck = move(out);
}

void Room::start_game()
{
    uint32_t seed = chrono::steady_clock::now().time_since_epoch().count();
    int idx = 0;
    int turnRandom = seed % 2;

    create_deck(seed);
    //turn = turnRandom == 0 ? master : challenger;

    turn = turnRandom == 0 ? challenger : challenger;
    score[master] = -1;
    score[challenger] = -1;
    phase = Phase::PLAYING;
}

bool Room::card_flip(const string& actor, int idx)
{
    if (phase != Phase::PLAYING) return false;
    if (actor != turn) return false;
    if (idx < 0 || idx >= (int)deck.cards.size()) return false;
    if (idx == firstIndex) return false;

    if (firstIndex < 0) 
    {
        firstIndex = idx;
        return true;
    }
    bool ok = deck.cards[firstIndex] == deck.cards[idx];
    if (ok)
    {
        deck.peek_cards[firstIndex] = true;
        deck.peek_cards[idx] = true;
        score[turn]++;
    }
    else 
    {
        turn = turn == master ? challenger : master;
    }
    bool isEnd = true;
    for (const bool& check : deck.peek_cards)
    {
        if (!check)
        {
            isEnd = false;
            break;
        }
    }
    if (isEnd)
    {
        phase = Phase::END;
    }
    firstIndex = -1;
    return true;
}

bool Room::peek_end(const string& actor)
{
    auto it = score.find(actor);
    if (it == score.end())
    {
        common::log("WORLD", actor + " peek_end start");
        return false;
    }
    if (it->second == 0)
    {
        return false; 
    }

    it->second = 0;

    for (auto& kv : score) 
    {
        if (kv.second != 0)
        {
            common::log("WORLD", actor + " peek_end false" );
            return false; 
        }
    }
    common::log("WORLD", "peek_end true");
    return true; 
}