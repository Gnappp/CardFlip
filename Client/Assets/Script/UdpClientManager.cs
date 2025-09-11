using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace Net
{
    public sealed class UdpClientManager : IDisposable
    {
        readonly ConcurrentQueue<Action> _mainQ = new();
        UdpClient _udp;
        IPEndPoint _remote;
        CancellationTokenSource _cts;
        int _seq;

        public event Action<string, float, float> OnActorPos;

        public async Task<bool> ConnectAsync(string host, int port, string token, string actor)
        {
            _cts = new CancellationTokenSource();
            _udp = new UdpClient();
            _remote = new IPEndPoint(IPAddress.Parse(host), port);
            try
            {
                _udp.Connect(_remote); 
            }
            catch (Exception e)
            {
                Debug.LogError(e); 
                return false; 
            }

            string line = $"HELLO token={token} actor={actor}";
            await SendRawAsync(line);

            _ = Task.Run(RecvLoop, _cts.Token);
            return true;
        }

        public async Task SendMove(Vector2 pos)
        {
            var kv = new Dictionary<string, string>
            {
                {"seq", (_seq++).ToString()},
                {"x", pos.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                {"y", pos.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},
            };
            string line = $"MOVE seq={_seq++} x={pos.x} y={pos.y}";
            await SendRawAsync(line);
        }

        async Task SendRawAsync(string s)
        {
            try
            {
                var buf = Encoding.ASCII.GetBytes(s);
                await _udp.SendAsync(buf, buf.Length);
            }
            catch (Exception ex) 
            {
                Debug.LogWarning($"UDP send failed: {ex.Message}"); 
            }
        }

        async Task RecvLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try 
                {
                    r = await _udp.ReceiveAsync();
                }
                catch (ObjectDisposedException)
                {
                    break; 
                }
                catch (SocketException) 
                {
                    continue;
                }

                string text = Encoding.ASCII.GetString(r.Buffer);
                foreach (var line in text.Split('\n'))
                {
                    if (line.StartsWith("ACTOR_POS"))
                    {
                        var kv = ParseKv(line);
                        if (kv.TryGetValue("id", out var id) &&
                            float.TryParse(kv.GetValueOrDefault("x", "0"), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                            float.TryParse(kv.GetValueOrDefault("y", "0"), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var y))
                        {
                            _mainQ.Enqueue(() => OnActorPos?.Invoke(id, x, y));
                        }
                    }
                }
            }
        }

        public void MainDequeueToUpdate()
        {
            while (_mainQ.TryDequeue(out var a))
                a();
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _udp?.Close(); } catch { }
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
}
