using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using Net;

public class GameManager : MonoBehaviour
{
    public enum Phase
    {
        READY, PLAYING, END
    }
    class Avatar
    {
        public string Id;
        public GameObject Go;
        public Vector3 TargetPos;
    }

    public class RoomParticipant
    {
        public string actorId;
        public int score;
        public int time;
    }
    public class RoomConfig
    {
        public int rows = 4, cols = 4;
        public int[] spacers;
        public uint seed;
        public string presetId;
        public int dur_ms;
        public int all_dur_ms;
    }
    public class RoomState
    {
        public string roomId, masterId, challengerId, roomTitle, turnActorId;
        public uint version;
        public Dictionary<string, RoomParticipant> members = new();
        public RoomConfig config = new();
        public bool isReady;
        public Phase phase;
    }

    [Header("Prefabs & UI")]
    public Text pingDot;
    public GameObject circlePrefab;
    public GameObject roomCreatePanel;
    public Button roomCreateButton;
    public Button createCancelButton;
    public Button createConfirmButton;
    public GameObject gameRoomPanel;
    public GameObject gameBoardPanel;
    public GameObject cardGrid;
    public GameObject touchProtect;

    [Header("Control TCP")]
    public string controlHost = "127.0.0.1";
    public int controlPort = 7100;

    [Header("World UDP")]
    public string worldHost = "127.0.0.1";
    public int worldUdpPort = 9001;

    [Header("RoomSign")]
    public GameObject roomSignPrefab;

    // roomId, 말풍선
    readonly Dictionary<string, GameObject> _roomSigns = new();

    [SerializeField]
    Vector3 SignOffset = new Vector3(0f, 1.2f, 0f);

    public float moveSpeed = 5f;
    public float lerpSmoothing = 10f;

    TcpClientManager _tcp;
    UdpClientManager _udp;

    RoomState _roomState;
    CardGridManager cardGridManager;
    Image masterTurn;
    Image challengerTurn;
    Image touchProtector;
    Dropdown rule;
    string _myId;

    readonly Dictionary<string, Avatar> _avatars = new();

    // 반드시 Update에서 실행해야할떄
    readonly ConcurrentQueue<Action> _mainQ = new();
    void EnqueueToMain(Action a) => _mainQ.Enqueue(a);

    [DllImport("kernel32.dll")] static extern bool AllocConsole();
    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int nStdHandle);
    const int STD_OUTPUT_HANDLE = -11;

    void Awake()
    {
        if (circlePrefab == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            circlePrefab = tmp;
            tmp.SetActive(false);
        }
        try
        {
            AllocConsole();

            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            var fs = new FileStream(handle, FileAccess.Write);
            var sw = new StreamWriter(fs) { AutoFlush = true };
            Console.SetOut(sw);

            Application.logMessageReceived += (cond, stack, type) =>
            {
                Console.WriteLine($"[{type}] {cond}");
                if (type == LogType.Error || type == LogType.Exception)
                    Console.WriteLine(stack);
            };

            Application.runInBackground = true; 
            Debug.Log("[WinConsole] attached");
            Console.WriteLine("Console attached.");
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    async void Start()
    {
        // UI 바인딩
        masterTurn = gameRoomPanel.transform.Find("RoomMasterPanel").GetComponent<Image>();
        challengerTurn = gameRoomPanel.transform.Find("RoomChallengerPanel").GetComponent<Image>();
        cardGridManager = cardGrid.GetComponent<CardGridManager>();
        touchProtector = touchProtect.GetComponent<Image>();
        rule = gameRoomPanel.transform.Find("MasterControlPanel").Find("GameRuleSet").GetComponent<Dropdown>();

        roomCreateButton.onClick.AddListener(() => roomCreatePanel.SetActive(true));
        createCancelButton.onClick.AddListener(() => roomCreatePanel.SetActive(false));
        createConfirmButton.onClick.AddListener(OnClickCreateRoomConfirm_TCP);

        cardGridManager.OnFlipRequest += OnClickCardFlip;
        gameRoomPanel.transform.Find("MasterControlPanel").Find("StartButton").GetComponent<Button>().onClick.AddListener(OnClickGameStartButton);
        gameRoomPanel.transform.Find("ChallengerControlPanel").Find("ReadyButton").GetComponent<Button>().onClick.AddListener(OnClickReadyButton);
        gameRoomPanel.transform.Find("ExitButton").GetComponent<Button>().onClick.AddListener(OnClickRoomExitButton);
        rule.onValueChanged.AddListener(OnRuleChanged);

        touchProtector.raycastTarget = true;

        // 세션 정보(로그인 결과)
        var s = SessionInfo.I;
        if (s == null || string.IsNullOrEmpty(s.token))
        {
            Debug.LogError("세션 없음: LoginScene을 통해 진입하세요.");
            return;
        }

        // === UDP 시작 ===
        _udp = new UdpClientManager();
        _udp.OnActorPos += OnActorPos;
        bool uok = await _udp.ConnectAsync(
            string.IsNullOrEmpty(worldHost) ? s.worldHost : worldHost,
            worldUdpPort > 0 ? worldUdpPort : s.worldUdpPort,
            s.token, s.actorName);
        if (!uok) { Debug.LogError("UDP connect failed"); return; }

        // 로컬 아바타
        ExistAvatar(s.actorName, Vector3.zero, true);

        // === TCP 시작 ===
        _tcp = new TcpClientManager();
        _tcp.OnLine += OnTcpLine;
        bool tok = await _tcp.ConnectAsync(
            string.IsNullOrEmpty(controlHost) ? s.worldHost : controlHost,
            controlPort > 0 ? controlPort : 7100,
            s.actorName);
        if (!tok) { Debug.LogError("TCP connect failed"); }
    }

    void Update()
    {
        // 입력 → UDP MOVE
        if (SessionInfo.I != null && _avatars.TryGetValue(SessionInfo.I.actorName, out var me))
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            var dir = new Vector3(h, v, 0).normalized;
            if (dir.sqrMagnitude > 0.01f)
            {
                me.Go.transform.position += dir * moveSpeed * Time.deltaTime;
                _ = _udp.SendMove(new Vector2(me.Go.transform.position.x, me.Go.transform.position.y));
            }
        }

        // 메인스레드
        _udp?.MainDequeueToUpdate();
        _tcp?.MainDequeueToUpdate();
        while (_mainQ.TryDequeue(out var a))
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }
        // 원격 아바타 보간
        foreach (var av in _avatars.Values)
        {
            if (SessionInfo.I != null && av.Id == SessionInfo.I.actorName) continue;
            av.Go.transform.position = Vector3.Lerp(av.Go.transform.position, av.TargetPos, lerpSmoothing * Time.deltaTime);
        }
    }

    void OnDestroy()
    {
        try { _udp?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
        try { FreeConsole(); } catch { }
    }

    void OnActorPos(string id, float x, float y)
    {
        var pos = new Vector3(x, y, 0f);
        var av = ExistAvatar(id, pos, SessionInfo.I != null && id == SessionInfo.I.actorName);
        av.TargetPos = pos;
    }


    // === TCP 수신 ===
    async void OnTcpLine(string line)
    {
        try
        {
            var (cmd, kv) = ParseCmd(line);
            switch (cmd)
            {
                case "BROADCAST_HEART_BEAT":
                    {
                        pingDot.text = DateTime.Now.ToString("HH:mm:ss.fff");
                        break;
                    }
                case "HELLO_OK":
                    {
                        Debug.Log(line);
                        break;

                    }
                case "RES_CREATE_ROOM":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var master = kv.GetValueOrDefault("master", "");
                        var title = kv.GetValueOrDefault("title", "");

                        _roomState = new RoomState();
                        _roomState.roomId = rid;
                        _roomState.masterId = master;
                        _roomState.roomTitle = title;
                        _roomState.challengerId = "";
                        ExistMember(master);

                        roomCreateButton.interactable = false;
                        gameRoomPanel.SetActive(true);
                        gameRoomPanel.transform.Find("RoomMasterPanel").Find("RoomMasterId").GetComponent<Text>().text = master;
                        gameRoomPanel.transform.Find("RoomTitle").GetComponent<Text>().text = title;
                        gameRoomPanel.transform.Find("GameRule").GetComponent<Text>().text = "4x4"; // 기본 4x4

                        SetActiveControlPanel();
                        cardGridManager.SetupBoard(_roomState.config.cols, _roomState.config.rows);
                        Debug.Log("RES_CREATE_ROOM");
                        break;
                    }
                case "BROADCAST_CREATE_ROOM":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var master = kv.GetValueOrDefault("master", "");
                        var title = kv.GetValueOrDefault("title", "");
                        Debug.Log($"[CAST_CREATE_ROOM] room={rid} {master} {title}");
                        CreateOrGetSign(rid, master, title, count: 1);  
                        Debug.Log("BROADCAST_CREATE_ROOM");
                        break;
                    }
                case "CAST_ENTER_ROOM":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var master = kv.GetValueOrDefault("master", "");
                        var challenger = kv.GetValueOrDefault("challenger", "");
                        var title = kv.GetValueOrDefault("title", "");
                        int rows = TryI(kv, "rows");
                        int cols = TryI(kv, "cols");
                        if (_roomState is null)
                        {
                            _roomState = new RoomState();
                        }

                        gameRoomPanel.SetActive(true);
                        roomCreateButton.interactable = false;

                        _roomState.roomId = rid;
                        _roomState.masterId = master;
                        _roomState.challengerId = challenger;
                        _roomState.roomTitle = title;
                        _roomState.config.cols = cols;
                        _roomState.config.rows = rows;

                        ExistMember(master);
                        ExistMember(challenger);

                        gameRoomPanel.SetActive(true);
                        gameRoomPanel.transform.Find("RoomTitle").GetComponent<Text>().text = title;
                        gameRoomPanel.transform.Find("GameRule").GetComponent<Text>().text = $"{_roomState.config.cols}x{_roomState.config.rows}";

                        InitGameBoard();
                        SetActiveControlPanel();
                        cardGridManager.SetupBoard(_roomState.config.cols, _roomState.config.rows);
                        Debug.Log("CAST_ENTER_ROOM");
                        break;
                    }
                case "BROADCAST_ENTER_ROOM":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var title = kv.GetValueOrDefault("title", "");
                        if (_roomSigns.TryGetValue(rid, out var go))
                            UpdateSignText(go, title, count: 2);
                        Debug.Log("BROADCAST_ENTER_ROOM");
                        break;
                    }
                case "CAST_CHANGE_READY":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        bool isReady = kv.GetValueOrDefault("isReady", "") == "True";
                        if (_roomState.challengerId == _myId)
                            gameRoomPanel.transform.Find("ExitButton").GetComponent<Button>().interactable = !isReady;

                        _roomState.isReady = isReady;
                        gameRoomPanel.transform.Find("RoomChallengerPanel").Find("ReadyComplete").gameObject.SetActive(isReady);
                        gameRoomPanel.transform.Find("MasterControlPanel").Find("StartButton").GetComponent<Button>().interactable = isReady;
                        InitGameBoard();
                        Debug.Log("CAST_CHANGE_READY");
                        break;
                    }
                case "CAST_GAME_START":
                    {
                        if (_roomState.challengerId == _myId)
                            OnClickReadyButton();
                        cardGridManager.SetupBoard(_roomState.config.cols, _roomState.config.rows);
                        SetDeactiveControlPanel();
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var cardsStr = kv.GetValueOrDefault("cards", "");
                        List<int> cards = cardsStr
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => int.TryParse(s, out var n) ? n : -1) 
                            .ToList();
                        int dur = TryI(kv, "dur");
                        int all_dur = TryI(kv, "all_dur");
                        int pahse = TryI(kv, "pahse");

                        _roomState.config.dur_ms = dur;
                        _roomState.config.all_dur_ms = all_dur;
                        _roomState.phase = (Phase)pahse;

                        try
                        {
                            bool check = await cardGridManager.FirstShowFlip(cards, dur, all_dur);
                            Debug.Log($"REQ_FIRST_FLIP_END {rid} {check} {_myId}");
                            if (check)
                                _tcp?.SendLine($"REQ_FIRST_FLIP_END roomId={rid} actor={_myId}");
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"FirstShowFlip failed: {e}");
                        }
                        Debug.Log("CAST_GAME_START");
                        break;
                    }
                case "CAST_FIRST_FLIP_END":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var turn = kv.GetValueOrDefault("turn", "");
                        int masterScore = TryI(kv, "masterScore");
                        int challengerScore = TryI(kv, "challengerScore");

                        _roomState.turnActorId = turn;
                        _roomState.members[_roomState.masterId].score = masterScore;
                        _roomState.members[_roomState.challengerId].score = challengerScore;

                        ChangedTurn(turn);
                        Debug.Log("CAST_FIRST_FLIP_END");
                        break;
                    }
                case "CAST_FLIP_RESULT":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var turn = kv.GetValueOrDefault("turn", "");
                        int index = TryI(kv, "index");
                        int card = TryI(kv, "card");
                        int masterScore = TryI(kv, "masterScore");
                        int challengerScore = TryI(kv, "challengerScore");

                        masterTurn.transform.Find("Score").GetComponent<Text>().text = masterScore.ToString();
                        challengerTurn.transform.Find("Score").GetComponent<Text>().text = challengerScore.ToString();
                        if (_roomState.turnActorId == turn)
                        {
                            if (cardGridManager.GetFirstIndex() == -1)
                            {
                                cardGridManager.ShowFirstFlip(index, card);
                            }
                            else
                            {
                                cardGridManager.ShowSecondFlipComplete(index, card);
                            }
                            _roomState.turnActorId = turn;
                        }
                        else
                        {
                            try
                            {
                                _roomState.turnActorId = turn;
                                bool check = await cardGridManager.ShowSecondFlipFail(index, card, _roomState.config.dur_ms);
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"FirstShowFlip failed: {e}");
                            }
                        }
                        ChangedTurn(turn);
                        Debug.Log("CAST_FLIP_RESULT");
                        break;
                    }
                case "CAST_END_GAME":
                    {
                        Debug.Log($"CAST_END_GAME");
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var turn = kv.GetValueOrDefault("turn", "");
                        var winner = kv.GetValueOrDefault("winner", "");
                        int index = TryI(kv, "index");
                        int card = TryI(kv, "card");
                        int masterScore = TryI(kv, "masterScore");
                        int challengerScore = TryI(kv, "challengerScore");

                        masterTurn.transform.Find("Score").GetComponent<Text>().text = masterScore.ToString();
                        challengerTurn.transform.Find("Score").GetComponent<Text>().text = challengerScore.ToString();
                        if (_roomState.turnActorId == turn)
                        {
                            if (cardGridManager.GetFirstIndex() == -1)
                            {
                                cardGridManager.ShowFirstFlip(index, card);
                            }
                            else
                            {
                                cardGridManager.ShowSecondFlipComplete(index, card);
                            }
                            _roomState.turnActorId = turn;
                        }
                        if (winner == "-")
                        {
                            masterTurn.transform.Find("Result").GetComponent<Text>().text = "무";
                            challengerTurn.transform.Find("Result").GetComponent<Text>().text = "무";
                        }
                        else if (winner == _roomState.masterId)
                            masterTurn.transform.Find("Result").GetComponent<Text>().text = "승";
                        else
                            challengerTurn.transform.Find("Result").GetComponent<Text>().text = "승";
                        SetActiveControlPanel();
                        await cardGridManager.ResetCardGrid();
                        Debug.Log("CAST_END_GAME");
                        break;
                    }

                case "CAST_EXIT_ROOM":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var master = kv.GetValueOrDefault("master", "");
                        var exitActor = kv.GetValueOrDefault("exitActor", "");
                        Debug.Log($"[CAST_EXIT_ROOM] {rid} {master} {exitActor}");
                        if (exitActor == master)
                        {
                            _roomState.masterId = _roomState.challengerId;
                            _roomState.challengerId = "";
                            _roomState.members.Remove(exitActor);
                        }
                        else
                        {
                            _roomState.challengerId = "";
                            _roomState.members.Remove(exitActor);
                        }

                        SetActiveControlPanel();
                        InitGameBoard();

                        if (exitActor == _myId)
                            ExitRoom();

                        Debug.Log("CAST_EXIT_ROOM");
                        break;
                    }
                case "BROADCAST_CHANGE_ROOM_MASTER":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var master = kv.GetValueOrDefault("master", "");

                        if (_roomSigns.TryGetValue(rid, out var go))
                            UpdateSignText(go, SucceedRoomSign(rid, master), count: 1);
                        Debug.Log("BROADCAST_CHANGE_ROOM_MASTER");
                        break;
                    }
                case "BROADCAST_EXIT_ROOM":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var title = kv.GetValueOrDefault("title", "");
                        if (_roomSigns.TryGetValue(rid, out var go))
                            UpdateSignText(go, title, count: 1);
                        Debug.Log("BROADCAST_EXIT_ROOM");
                        break;
                    }
                case "BROADCAST_DELETE_ROOM":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        var master = kv.GetValueOrDefault("master", "");
                        if (master == _myId)
                        {
                            InitGameBoard();
                            ExitRoom();
                        }

                        GameObject.Destroy(_roomSigns[rid]);
                        _roomSigns.Remove(rid);
                        Debug.Log("BROADCAST_DELETE_ROOM");
                        break;
                    }
                case "CAST_CHANGE_RULE":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        int rows = TryI(kv, "rows");
                        int cols = TryI(kv, "cols");
                        Debug.Log($"CAST_CHANGE_RULE {rid} {cols} {rows}");
                        if (_roomState.roomId == rid)
                        {
                            _roomState.config.rows = rows;
                            _roomState.config.cols = cols;
                            cardGridManager.SetupBoard(_roomState.config.cols, _roomState.config.rows);
                        }
                        break;
                    }
                case "CAST_FORCED_END_GAME":
                    {
                        var rid = kv.GetValueOrDefault("roomId", "");
                        int phase = TryI(kv, "phase");

                        int masterScore = 0;
                        int challengerScore = 0;

                        masterTurn.transform.Find("Score").GetComponent<Text>().text = masterScore.ToString();
                        challengerTurn.transform.Find("Score").GetComponent<Text>().text = challengerScore.ToString();
                        masterTurn.transform.Find("Result").GetComponent<Text>().text = "승";
                        _roomState.phase = (Phase)phase;
                        SetActiveControlPanel();
                        Debug.Log($"CAST_FORCED_END_GAME");
                        break;
                    }
                case "BROADCAST_EXIT_SERVER":
                    {
                        var actor = kv.GetValueOrDefault("actor", "");
                        EnqueueToMain(() => RemoveAvatar(actor));

                        Debug.Log(actor);
                        break;
                    }
                default:
                    Debug.Log($"[TCP] {line}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[OnTcpLine] fatal: " + e + " line=" + line);
        }
    }

    // === 유틸 ===

    // 아바타 삭제
    void RemoveAvatar(string actorId)
    {
        if (string.IsNullOrEmpty(actorId)) return;

        if (_avatars.TryGetValue(actorId, out var go))
        {
            Destroy(go.Go);
            _avatars.Remove(actorId);
        }
        else
        {
            Debug.LogWarning($"[Avatar] not found: {actorId}");
        }
    }

    // 퇴장퇴장시 방 초기화
    void ExitRoom()
    {
        _roomState = null;
        roomCreateButton.interactable = true;
        gameRoomPanel.transform.Find("RoomMasterPanel").Find("RoomMasterId").GetComponent<Text>().text = "";
        gameRoomPanel.transform.Find("RoomChallengerPanel").Find("RoomChallengerId").GetComponent<Text>().text = "";
        gameRoomPanel.transform.Find("RoomTitle").GetComponent<Text>().text = "";
        gameRoomPanel.transform.Find("GameRule").GetComponent<Text>().text = "";
        gameRoomPanel.SetActive(false);
    }

    // 말풍선 전달하기
    string SucceedRoomSign(string roomId, string masterId)
    {
        if (!_roomSigns.TryGetValue(roomId, out var sign)) return "";
        if (!_avatars.TryGetValue(masterId, out var masterAvatar)) return "";

        Transform anchor = masterAvatar.Go.transform;
        var t = sign.transform;

        t.SetParent(anchor, worldPositionStays: false);
        t.localPosition = SignOffset;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        return t.Find("Canvas").Find("Title").GetComponent<Text>().text;
    }

    // 게임중일때 준비, 시작버튼 등 있으면 안되니까
    void SetDeactiveControlPanel()
    {
        gameRoomPanel.transform.Find("MasterControlPanel").gameObject.SetActive(false);
        gameRoomPanel.transform.Find("ChallengerControlPanel").gameObject.SetActive(false);
    }

    // Master, Challenger 정보 표기
    void SetActiveControlPanel()
    {
        Debug.Log($"{_roomState.masterId} {_myId}");
        gameRoomPanel.transform.Find("MasterControlPanel").gameObject.SetActive(_roomState.masterId == _myId);
        gameRoomPanel.transform.Find("ChallengerControlPanel").gameObject.SetActive(_roomState.challengerId == _myId);
        gameRoomPanel.transform.Find("RoomMasterPanel").Find("RoomMasterId").GetComponent<Text>().text = _roomState.masterId;
        gameRoomPanel.transform.Find("RoomChallengerPanel").Find("RoomChallengerId").GetComponent<Text>().text = _roomState.challengerId;
    }

    // 게임 정보 초기화
    void InitGameBoard()
    {
        masterTurn.color = Color.white;
        challengerTurn.color = Color.white;

        touchProtector.raycastTarget = true;

        masterTurn.transform.Find("Score").GetComponent<Text>().text = "";
        challengerTurn.transform.Find("Score").GetComponent<Text>().text = "";

        masterTurn.transform.Find("Result").GetComponent<Text>().text = "";
        challengerTurn.transform.Find("Result").GetComponent<Text>().text = "";

    }

    void ChangedTurn(string turn)
    {

        if (turn == _roomState.masterId)
        {
            masterTurn.color = Color.magenta;
            challengerTurn.color = Color.white;
        }
        else
        {
            masterTurn.color = Color.white;
            challengerTurn.color = Color.magenta;
        }

        if (turn == _myId)
            touchProtector.raycastTarget = false;
        else
            touchProtector.raycastTarget = true;
    }

    // members에 없으면 넣어주기
    RoomParticipant ExistMember(string actorId)
    {
        if (!_roomState.members.TryGetValue(actorId, out var p))
        {
            p = new RoomParticipant { actorId = actorId };
            _roomState.members[actorId] = p;
        }
        return p;
    }

    // Avatar가 없으면 만들어주기
    Avatar ExistAvatar(string id, Vector3 pos, bool isLocal)
    {
        if (_avatars.TryGetValue(id, out var av)) return av;

        var go = Instantiate(circlePrefab);
        go.name = $"Actor_{id}";
        go.SetActive(true);
        go.transform.position = pos;

        var sr = go.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingLayerName = "Default";
            sr.sortingOrder = 0;
            if (isLocal) sr.color = new Color(0.6f, 0.8f, 1f);
            sr.sortingOrder = 100;
        }

        var label = new GameObject("Label");
        label.transform.SetParent(go.transform, false);
        label.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        var tm = label.AddComponent<TextMesh>();
        tm.text = isLocal ? $"{id} (you)" : id;
        if (isLocal)
            _myId = id;
        tm.characterSize = 0.15f;
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.black;
        var labelMr = label.GetComponent<MeshRenderer>();
        if (labelMr != null)
        {
            labelMr.sortingLayerName = sr != null ? sr.sortingLayerName : "Default";
            labelMr.sortingOrder = 10;
        }

        av = new Avatar { Id = id, Go = go, TargetPos = pos };
        _avatars[id] = av;
        return av;
    }

    static (string cmd, Dictionary<string, string> kv) ParseCmd(string line)
    {
        int sp = line.IndexOf(' ');
        string cmd = sp < 0 ? line : line[..sp];
        var kv = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sp >= 0)
        {
            var rest = line[(sp + 1)..];
            var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                int eq = p.IndexOf('=');
                if (eq > 0) kv[p[..eq]] = p[(eq + 1)..];
            }
        }
        return (cmd, kv);
    }

    static int TryI(Dictionary<string, string> kv, string k, int def = 0) => kv.TryGetValue(k, out var s) && int.TryParse(s, out var v) ? v : def;

    static string ShortTitle(string title, int max = 12)
    {
        if (string.IsNullOrEmpty(title)) return "";
        if (title.Length <= max) return title;
        return title.Substring(0, max - 1) + "…";
    }

    GameObject CreateOrGetSign(string roomId, string masterId, string title, int count /*1 or 2*/)
    {
        if (!_avatars.TryGetValue(masterId, out var av))
        {
            Debug.Log("캐릭터 정보가 없음");
        }

        // 이미 있으면 텍스트만 바꿈
        if (_roomSigns.TryGetValue(roomId, out var go))
        {
            Debug.Log("이미 있으면 텍스트만 바꿈");
            UpdateSignText(go, title, count);
            return go;
        }

        // 새로 만들기
        if (roomSignPrefab != null)
        {
            go = Instantiate(roomSignPrefab);
            go.GetComponentInChildren<Canvas>(true).worldCamera = Camera.main;
            Debug.Log("새로 만들기");
        }

        go.name = $"RoomSign_{roomId}"; Debug.Log($"{go.name}");
        go.transform.SetParent(av.Go.transform, false);
        go.transform.localPosition = SignOffset;
        go.SetActive(true);
        var sr = av.Go.GetComponentInChildren<SpriteRenderer>();
        var canvas = go.GetComponentInChildren<Canvas>(true);
        //canvas.worldCamera = Camera.main;
        if (canvas != null && sr != null)
        {
            canvas.sortingLayerName = sr.sortingLayerName;
            canvas.sortingOrder = sr.sortingOrder + 5; // 스프라이트보다 앞
        }
        if (count == 2)
        {
            canvas.transform.Find("JoinButton").GetComponent<Button>().interactable = false;
        }

        UpdateSignText(go, title, count);
        UpdateJoinButton(go, roomId, count);
        _roomSigns[roomId] = go;
        return go;
    }

    public void EnterRoom_TCP(string roomId) => _tcp?.SendLine($"REQ_ENTER_ROOM roomId={roomId}");

    void UpdateJoinButton(GameObject sign, string roomId, int count)
    {
        var t = sign.transform.Find("Canvas/JoinButton");
        if (t == null) { Debug.LogWarning("Canvas/JoinButton 못 찾음"); return; }

        var btn = t.GetComponent<Button>();
        if (btn == null) { Debug.LogWarning("JoinButton에 Button 컴포넌트 없음"); return; }

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => EnterRoom_TCP(roomId));

        // 2/2면 비활성화
        btn.interactable = (count < 2);
    }

    void UpdateSignText(GameObject Sign, string title, int count)
    {
        var titleText = Sign.transform.Find("Canvas").Find("Title").GetComponent<Text>();
        var countText = Sign.transform.Find("Canvas").Find("Count").GetComponent<Text>();
        if (titleText is not null)
            titleText.text = $"{ShortTitle(title)}";
        if (countText is not null)
            countText.text = $"{count}/2";
    }

    public void OnClickCreateRoomConfirm_TCP()
    {
        var s = SessionInfo.I; if (s == null) return;
        var t = roomCreatePanel.transform.Find("RoomTitleInput").GetComponent<InputField>();
        var title = string.IsNullOrEmpty(t.text) ? "room" : t.text;
        _tcp?.SendLine($"REQ_CREATE_ROOM title={title} rows=4 cols=4");
        roomCreatePanel.SetActive(false);
    }

    void OnClickReadyButton()
    {
        var rid = _roomState.roomId;
        var isReady = !_roomState.isReady;
        _tcp?.SendLine($"REQ_CHANGE_READY roomId={rid} isReady={isReady}");
    }

    void OnClickGameStartButton()
    {
        var rid = _roomState.roomId;
        _tcp?.SendLine($"REQ_GAME_START roomId={rid}");
    }
    void OnClickCardFlip(int idx)
    {
        _tcp?.SendLine($"REQ_FLIP roomId={_roomState.roomId} actor={_myId} index={idx}");
    }
    void OnClickRoomExitButton()
    {
        _tcp?.SendLine($"REQ_ROOM_EXIT roomId={_roomState.roomId} actor={_myId}");
    }
    void OnRuleChanged(int index)
    {
        string t = rule.options[index].text;
        string[] parts = t.Split('x');
        _tcp?.SendLine($"REQ_CHANGE_RULE roomId={_roomState.roomId} master={_myId} cols={parts[0]} rows={parts[1]}");
        Debug.Log($"Change Dropdown {index} cols={parts[0]} rows={parts[1]}");
    }
}
