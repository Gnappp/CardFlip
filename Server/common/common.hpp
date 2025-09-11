#pragma once
#include <chrono>
#include <format>
#include <iostream>
#include <mutex>
#include <string>

using namespace std;

namespace common 
{
    inline string now()
    {
        using namespace chrono;
        auto tp = floor<seconds>(system_clock::now());
        
        ostringstream os;
        os << format("{:%H:%M:%S}", zoned_time{ current_zone(), tp });
        return os.str();
    }
    inline void log(const char* tag, const string& msg) 
    {
        static mutex m; lock_guard<mutex> lk(m);
        cout << "[" << now() << "][" << tag << "] " << msg << endl;
    }
    inline int to_int(const char* s, int d) 
    { 
        try 
        { 
            return s ? stoi(s) : d;
        }
        catch (...) 
        {
            return d; 
        } 
    }
    inline void title(const char*) {} 
}