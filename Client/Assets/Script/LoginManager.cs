using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Net;

public class LoginManager : MonoBehaviour
{
    [Header("Gateway (TCP)")]
    public string gatewayHost = "127.0.0.1";
    public int gatewayPort = 7000;

    [Header("World (UDP)")]
    public string defaultWorldHost = "127.0.0.1"; 
    public int defaultWorldUdpPort = 9001;

    [Header("UI")]
    public InputField idField;
    public InputField pwField;
    public Button loginButton;
    public Dropdown serverDropdown;
    public Button enterButton;
    public GameObject loginPanel;
    public GameObject selectPanel;
    public GameObject messageBox;

    [Header("Client")]
    public float moveSpeed = 5f;
    public float lerpSmoothing = 10f;
    public GameObject capsulePrefab;

    NetworkStream _tcpStream;
    UdpClient _udp;
    IPEndPoint _worldEndPoint;
    CancellationTokenSource _cts;

    string _userId;        
    string _token;             
    int _seq;

    // 서버 목록 항목
    class WorldInfo
    {
        public string id, name, host;
        public int udp;
    }
    readonly List<WorldInfo> _servers = new List<WorldInfo>();

    TcpClientManager _tcp;

    void Awake()
    {
        if (capsulePrefab == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsulePrefab = tmp;
            tmp.SetActive(false);
        }
    }

    async void Start()
    {
        Application.runInBackground = true;
        loginButton.onClick.AddListener(OnClickLogin);
        enterButton.onClick.AddListener(OnClickEnterWorld);
        messageBox.transform.Find("CloseButton").GetComponent<Button>().onClick.AddListener(OnClickClose);

        loginPanel.SetActive(true);
        selectPanel.SetActive(false); 

        _tcp = new TcpClientManager();
        _tcp.OnLine += OnTcpLine;
        bool tok = await _tcp.ConnectAsync(gatewayHost, gatewayPort);
        if (!tok) { Debug.LogError("TCP connect failed"); }
    }

    void Update()
    {
        _tcp?.MainDequeueToUpdate();
    }

    async void OnTcpLine(string line)
    {
        try
        {
            var (cmd, kv) = ParseCmd(line);
            switch (cmd)
            {
                case "LOGIN_OK":
                    {
                        var token = kv.GetValueOrDefault("token", "");
                        int worldCount = TryI(kv, "worldCount");

                        if (token == null)
                        {
                            Debug.LogError("LOGIN_OK 토큰 누락");
                            loginButton.interactable = true;
                            return;
                        }
                        Debug.Log("LOGIN_OK");
                        _servers.Clear();
                        serverDropdown.ClearOptions();
                        break;
                    }
                case "WORLD":
                    {
                        var id = kv.GetValueOrDefault("id", "");
                        var name = kv.GetValueOrDefault("name", "");
                        var udp_host = kv.GetValueOrDefault("udp_host", "");
                        int udp_port = TryI(kv, "udp_port");

                        var info = new WorldInfo
                        {
                            id = id,
                            name = name,
                            host = udp_host,
                            udp = udp_port,
                        };
                        _servers.Add(info);

                        // 드롭다운 채우기
                        var labels = _servers.Select(s => $"{s.id} | {s.name} | {s.host}:{s.udp}").ToList();
                        if (labels.Count == 0)
                        {
                            labels.Add("(서버 없음) 기본 사용");
                            _servers.Add(new WorldInfo { id = "", name = "default", host = defaultWorldHost, udp = defaultWorldUdpPort });
                        }
                        serverDropdown.AddOptions(labels);
                        serverDropdown.value = 0;
                        serverDropdown.RefreshShownValue();
                        Debug.Log("WORLD");
                        break;
                    }
                case "ERR_ID_EXSIT":
                    {
                        messageBox.SetActive(true);
                        Debug.Log("ID 중복");
                        break;
                    }
                case "ENTER_OK":
                    {
                        var udp_token = kv.GetValueOrDefault("udp_token", "");
                        var actor = kv.GetValueOrDefault("actor", "");
                        var udp_host = kv.GetValueOrDefault("udp_host", "");
                        int udp_port = TryI(kv, "udp_port");

                        Debug.Log($"{actor} {udp_token} {udp_host} {udp_port}");

                        SessionInfo.I.actorName = actor;
                        SessionInfo.I.token = udp_token;
                        SessionInfo.I.worldHost = udp_host;
                        SessionInfo.I.worldUdpPort = udp_port;

                        try { _tcp?.Close(); } catch { }

                        SceneManager.LoadScene("GameScene");
                        break;
                    }
                default:
                    Debug.Log($"[TCP] {line}");
                    break;
            }
        }
        catch { Debug.Log("TCP ERROR"); }
    }
    static int TryI(Dictionary<string, string> kv, string k, int def = 0) => kv.TryGetValue(k, out var s) && int.TryParse(s, out var v) ? v : def;

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
    void OnDestroy()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Close(); } catch { }
        try { _tcpStream?.Close(); } catch { }
        try { _tcp?.Close(); } catch { }
    }

    void OnClickClose()
    {
        loginPanel.SetActive(true);
        selectPanel.SetActive(false);
        messageBox.SetActive(false);
        loginButton.interactable = true;
        idField.text = "";
    }
    async void OnClickLogin()
    {
        _userId = idField.text.Trim();
        if (string.IsNullOrEmpty(_userId))
        {
            Debug.LogWarning("ID가 비어있습니다.");
            return;
        }
        loginButton.interactable = false;

        try
        {
            _tcp?.SendLine($"LOGIN id={_userId}");

            _servers.Clear();
            serverDropdown.ClearOptions();

            loginPanel.SetActive(false);
            selectPanel.SetActive(true);

        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            loginButton.interactable = true;
        }
    }

    async void OnClickEnterWorld()
    {
        var sel = _servers[Mathf.Clamp(serverDropdown.value, 0, _servers.Count - 1)];
        Debug.Log($"ENTER_WORLD actor={_userId} world={sel.id}");
        _tcp?.SendLine($"ENTER_WORLD actor={_userId} world={sel.id}");
    }

    async Task SendTcpLine(string line, CancellationToken ct)
    {
        byte[] buf = Encoding.ASCII.GetBytes(line);
        await _tcpStream.WriteAsync(buf, 0, buf.Length, ct);
        await _tcpStream.FlushAsync(ct);
    }

    async Task<string> ReadTcpLine(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            int n = await _tcpStream.ReadAsync(buffer, 0, 1, ct);
            if (n == 0) return null;
            char c = (char)buffer[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }
        return sb.ToString();
    }

    static Dictionary<string, string> ParseKv(string line)
    {
        var dict = new Dictionary<string, string>();
        var parts = line.Trim().Split(' ');
        for (int i = 1; i < parts.Length; i++)
        {
            var tok = parts[i];
            int eq = tok.IndexOf('=');
            if (eq <= 0) continue;
            var k = tok.Substring(0, eq);
            var v = tok.Substring(eq + 1);
            dict[k] = v;
        }
        return dict;
    }
}
