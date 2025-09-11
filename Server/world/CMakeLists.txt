add_executable(world_server
    world.cpp
    Room.cpp
    UdpSessionManager.cpp
    TcpAcceptor.cpp
    TcpSession.cpp
)
target_include_directories(world_server PRIVATE ${CMAKE_CURRENT_LIST_DIR} ../common)
target_link_libraries(world_server PRIVATE common)
target_link_libraries(world_server PRIVATE ws2_32)

