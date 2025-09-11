C++ (ASIO)기반 서버와 Unity 클라이언트로 구현한 온라인 카드 뒤집기 게임 프로젝트입니다.  
Gateway ↔ World 서버 구조로 설계하였으며, 
클라이언트와 TCP로 방생성, 입장/퇴장, 게임진행을 구현했으며 UDP로 실시간 이동을 구현했습니다.

구조
CardFlip/
├── server/ C++ asio 서버 (Gateway, World, Common)
│ ├── gateway/ 로그인/월드 진입 관리 (TCP)
│ ├── world/ 방 생성/게임 진행 관리 (TCP + UDP)
│ └── common/ 공용 유틸 (net.hpp, protocol.hpp 등)
└── client/ Unity 클라이언트
  ├── Assets/
  ├── Packages/
  └── ProjectSettings/
