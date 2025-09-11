add_executable(gateway_server
    gateway.cpp
    Server.cpp
    Session.cpp
    WorldServerLinker.cpp
)
target_include_directories(world_server PRIVATE ${CMAKE_CURRENT_LIST_DIR} ../common)
target_link_libraries(world_server PRIVATE common ws2_32)

