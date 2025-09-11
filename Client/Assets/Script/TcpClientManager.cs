using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Net
{
    public class TcpClientManager : IDisposable
    {
        readonly ConcurrentQueue<string> _sendQ = new();
        readonly ConcurrentQueue<Action> _mainQ = new();
        CancellationTokenSource _cts;
        TcpClient _tcp;
        StreamReader _reader;
        StreamWriter _writer;

        public event Action<string> OnLine;
        public bool IsConnected => _tcp != null && _tcp.Connected;

        public async Task<bool> ConnectAsync(string host, int port, string actorId = null, int timeoutMs = 3000)
        {
            _cts = new CancellationTokenSource();
            _tcp = new TcpClient();

            var connectTask = _tcp.ConnectAsync(host, port);
            var delay = Task.Delay(timeoutMs, _cts.Token);
            var done = await Task.WhenAny(connectTask, delay);
            if (done == delay || !_tcp.Connected) return false;

            var ns = _tcp.GetStream();
            _reader = new StreamReader(ns, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            _writer = new StreamWriter(ns, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

            _ = Task.Run(RecvLoop, _cts.Token);
            _ = Task.Run(SendLoop, _cts.Token);

            if (!string.IsNullOrEmpty(actorId))
                SendLine($"HELLO actor={actorId}");

            return true;
        }

        public void SendLine(string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                _sendQ.Enqueue(line);
        }

        async Task RecvLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        var raw = await _reader.ReadLineAsync();
                        if (raw == null) break;

                        var line = raw.TrimEnd('\r');
                        if (line.Length == 0) continue;

                        _mainQ.Enqueue(() => OnLine?.Invoke(line));
                    }
                    catch (Exception e)
                    {
                        Debug.Log("[TCP] RecvLoop error: " + e);
                    }
                }
            }
            catch { }
            finally
            {
                Close();
            }
        }

        async Task SendLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (_sendQ.TryDequeue(out var line))
                        await _writer.WriteLineAsync(line);
                    else
                        await Task.Delay(5, _cts.Token);
                }
            }
            catch { }
        }

        public void MainDequeueToUpdate()
        {
            while (_mainQ.TryDequeue(out var act))
            {
                try
                {
                    act();
                }
                catch (Exception e)
                {
                    Debug.Log("[TCP] dequeue error: " + e);
                }
            }

        }

        public void Close()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }
            try 
            {
                _tcp?.Close();
            } 
            catch { }
            _tcp = null;
        }

        public void Dispose() => Close();
    }
}
