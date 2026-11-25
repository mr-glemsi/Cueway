using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cueway.Config;

namespace Cueway.Media;

internal readonly record struct BridgeTabRecord(string ConnectionId, string BrowserKey, BridgeTabDto Tab);

internal sealed class BrowserBridgeServer : IDisposable
{
    public const int DefaultPort = 47621;
    private const int MaxHandshakeBytes = 16 * 1024;
    private const int MaxPayloadBytes = 1 * 1024 * 1024;
    private const int MaxConnections = 16;
    private const int MaxTabsPerConnection = 100;
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private sealed class BridgeClientConnection : IDisposable
    {
        public string Id { get; }
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public string BrowserKey { get; set; } = "chrome";
        public int ProtocolVersion { get; set; }
        public List<BridgeTabDto> Tabs { get; set; } = [];
        private bool _disposed;

        public BridgeClientConnection(string id, TcpClient client, NetworkStream stream)
        {
            Id = id;
            Client = client;
            Stream = stream;
        }

        public async Task<bool> SendFrameAsync(byte opcode, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            if (_disposed) return false;
            await WriteLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_disposed) return false;
                int len = payload.Length;
                int hdrLen = len <= 125 ? 2 : (len <= 65535 ? 4 : 10);
                byte[] frame = new byte[hdrLen + len];
                frame[0] = (byte)(0x80 | (opcode & 0x0F));

                if (len <= 125)
                {
                    frame[1] = (byte)len;
                }
                else if (len <= 65535)
                {
                    frame[1] = 126;
                    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), (ushort)len);
                }
                else
                {
                    frame[1] = 127;
                    BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(2, 8), (ulong)len);
                }

                if (len > 0)
                    payload.Span.CopyTo(frame.AsSpan(hdrLen));

                await Stream.WriteAsync(frame, ct).ConfigureAwait(false);
                await Stream.FlushAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { WriteLock.Release(); } catch { }
            }
        }

        public Task<bool> SendTextAsync(string text, CancellationToken ct = default)
            => SendFrameAsync(0x1, Encoding.UTF8.GetBytes(text), ct);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stream.Dispose(); } catch { }
            try { Client.Dispose(); } catch { }
            try { WriteLock.Dispose(); } catch { }
        }
    }

    private readonly int _port;
    private readonly object _lifecycleLock = new();
    private readonly object _connLock = new();
    private readonly Dictionary<string, BridgeClientConnection> _connections = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private volatile bool _isListening;
    private bool _disposed;

    public event Action? SessionsChanged;

    public bool IsListening => _isListening;
    public int Port => _port;

    public BrowserBridgeServer(int port = DefaultPort)
    {
        _port = port;
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _cts != null)
                return;

            var cts = new CancellationTokenSource();
            _cts = cts;
            _ = Task.Run(() => RunListenerLoopAsync(cts.Token));
        }
    }

    private async Task RunListenerLoopAsync(CancellationToken ct)
    {
        int retryDelayMs = 400;
        while (!ct.IsCancellationRequested && !_disposed)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, _port);
                listener.Start(16);

                lock (_lifecycleLock)
                {
                    if (_disposed || ct.IsCancellationRequested)
                    {
                        try { listener.Stop(); } catch { }
                        return;
                    }
                    _listener = listener;
                }

                _isListening = true;
                retryDelayMs = 400;
                SessionsChanged?.Invoke();

                await AcceptLoopAsync(listener, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Port may still be releasing after an instance swap or elevation restart; retry cleanly
            }
            finally
            {
                _isListening = false;
                lock (_lifecycleLock)
                {
                    if (ReferenceEquals(_listener, listener))
                        _listener = null;
                }
                if (listener != null)
                {
                    try { listener.Stop(); } catch { }
                }
            }

            if (ct.IsCancellationRequested || _disposed)
                break;

            try
            {
                await Task.Delay(retryDelayMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            retryDelayMs = Math.Min(1500, retryDelayMs * 2);
        }
    }

    public (int ConnectedBrowsers, int TabCount) GetStatusSnapshot()
    {
        lock (_connLock)
        {
            int tabs = 0;
            foreach (var c in _connections.Values)
                tabs += c.Tabs.Count;
            return (_connections.Count, tabs);
        }
    }

    public List<BridgeTabRecord> GetAllTabsSnapshot()
    {
        lock (_connLock)
        {
            var list = new List<BridgeTabRecord>();
            foreach (var conn in _connections.Values)
            {
                foreach (var tab in conn.Tabs)
                {
                    list.Add(new BridgeTabRecord(conn.Id, conn.BrowserKey, tab));
                }
            }
            return list;
        }
    }

    public bool? TryGetTabIsPlaying(string connectionId, int tabId)
    {
        lock (_connLock)
        {
            if (_connections.TryGetValue(connectionId, out var conn))
            {
                foreach (var tab in conn.Tabs)
                {
                    if (tab.TabId == tabId)
                        return tab.IsPlaying;
                }
            }
        }
        return null;
    }

    public int GetConnectionProtocolVersion(string connectionId)
    {
        lock (_connLock)
        {
            return _connections.TryGetValue(connectionId, out var conn) ? conn.ProtocolVersion : 0;
        }
    }

    public bool TryFindMatchingTab(
        string? browserKey,
        string? siteKey,
        string? title,
        out string connectionId,
        out int tabId)
    {
        connectionId = string.Empty;
        tabId = 0;

        lock (_connLock)
        {
            int bestScore = 0;
            foreach (var conn in _connections.Values)
            {
                foreach (var tab in conn.Tabs)
                {
                    int score = 0;
                    if (!string.IsNullOrEmpty(browserKey) &&
                        (conn.BrowserKey.Equals(browserKey, StringComparison.OrdinalIgnoreCase) ||
                         (browserKey.Equals("zen", StringComparison.OrdinalIgnoreCase) && conn.BrowserKey.Equals("firefox", StringComparison.OrdinalIgnoreCase))))
                    {
                        score += 10;
                    }

                    string? tabSite = BrowserSiteScanner.DetectSite(tab.Url) ??
                                      BrowserSiteScanner.DetectSite(tab.Title, allowStandaloneX: true);
                    if (!string.IsNullOrEmpty(siteKey) && !string.IsNullOrEmpty(tabSite) &&
                        tabSite.Equals(siteKey, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 30;
                    }

                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(tab.Title) &&
                        (tab.Title.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                         title.Contains(tab.Title, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 60;
                    }

                    if (score > bestScore && score >= 30)
                    {
                        bestScore = score;
                        connectionId = conn.Id;
                        tabId = tab.TabId;
                    }
                }
            }
            return bestScore > 0;
        }
    }

    public async Task<bool> SendCommandAsync(string connectionId, int tabId, string action, int delta = 0)
    {
        BridgeClientConnection? conn;
        lock (_connLock)
        {
            _connections.TryGetValue(connectionId, out conn);
        }
        if (conn == null)
            return false;

        string json = JsonSerializer.Serialize(
            new BridgeCommandMessage
            {
                Type = "cmd",
                TabId = tabId,
                Action = action,
                Delta = delta
            },
            AppJsonContext.Default.BridgeCommandMessage);

        return await conn.SendTextAsync(json).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }
        catch
        {
            // Listener stopped cleanly on shutdown
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        BridgeClientConnection? conn = null;
        try
        {
            using var stream = client.GetStream();

            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(3500);

            string? headerText = await ReadHttpHeaderAsync(stream, handshakeCts.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(headerText))
                return;

            var headers = ParseHeaders(headerText, out string requestLine);
            if (!requestLine.StartsWith("GET ", StringComparison.OrdinalIgnoreCase))
            {
                await WriteHttpErrorAsync(stream, "400 Bad Request").ConfigureAwait(false);
                return;
            }

            if (!headers.TryGetValue("Origin", out string? origin) || !IsAllowedExtensionOrigin(origin))
            {
                await WriteHttpErrorAsync(stream, "403 Forbidden").ConfigureAwait(false);
                return;
            }

            if (!headers.TryGetValue("Upgrade", out string? upgrade) ||
                !upgrade.Contains("websocket", StringComparison.OrdinalIgnoreCase) ||
                !headers.TryGetValue("Sec-WebSocket-Key", out string? secKey) ||
                string.IsNullOrWhiteSpace(secKey))
            {
                await WriteHttpErrorAsync(stream, "400 Bad Request").ConfigureAwait(false);
                return;
            }

            bool limitReached;
            lock (_connLock)
            {
                limitReached = _connections.Count >= MaxConnections;
            }
            if (limitReached)
            {
                await WriteHttpErrorAsync(stream, "503 Service Unavailable").ConfigureAwait(false);
                return;
            }

            byte[] acceptHash = SHA1.HashData(Encoding.ASCII.GetBytes(secKey.Trim() + WebSocketGuid));
            string acceptKey = Convert.ToBase64String(acceptHash);
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), handshakeCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(handshakeCts.Token).ConfigureAwait(false);

            string connId = Guid.NewGuid().ToString("N")[..8];
            conn = new BridgeClientConnection(connId, client, stream);
            lock (_connLock)
            {
                if (_connections.Count >= MaxConnections)
                {
                    conn.Dispose();
                    conn = null;
                    return;
                }
                _connections[connId] = conn;
            }
            SessionsChanged?.Invoke();

            await RunWebSocketReadLoopAsync(conn, ct).ConfigureAwait(false);
        }
        catch
        {
            // Client disconnected or invalid handshake
        }
        finally
        {
            if (conn != null)
            {
                bool removed;
                lock (_connLock)
                {
                    removed = _connections.Remove(conn.Id);
                }
                conn.Dispose();
                if (removed)
                    SessionsChanged?.Invoke();
            }
            else
            {
                try { client.Dispose(); } catch { }
            }
        }
    }

    private async Task RunWebSocketReadLoopAsync(BridgeClientConnection conn, CancellationToken ct)
    {
        var headerBuf = new byte[2];
        var lenBuf = new byte[8];
        var maskKey = new byte[4];
        using var fragmentBuffer = new MemoryStream();
        byte fragmentedOpcode = 0;

        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExactAsync(conn.Stream, headerBuf, 2, ct).ConfigureAwait(false))
                break;

            bool fin = (headerBuf[0] & 0x80) != 0;
            byte opcode = (byte)(headerBuf[0] & 0x0F);
            bool masked = (headerBuf[1] & 0x80) != 0;
            ulong payloadLen = (ulong)(headerBuf[1] & 0x7F);

            if (!masked)
                break; // RFC 6455 requires masked frames from clients

            if (payloadLen == 126)
            {
                if (!await ReadExactAsync(conn.Stream, lenBuf, 2, ct).ConfigureAwait(false))
                    break;
                payloadLen = BinaryPrimitives.ReadUInt16BigEndian(lenBuf.AsSpan(0, 2));
            }
            else if (payloadLen == 127)
            {
                if (!await ReadExactAsync(conn.Stream, lenBuf, 8, ct).ConfigureAwait(false))
                    break;
                payloadLen = BinaryPrimitives.ReadUInt64BigEndian(lenBuf);
            }

            if (payloadLen > MaxPayloadBytes)
                break;

            if (!await ReadExactAsync(conn.Stream, maskKey, 4, ct).ConfigureAwait(false))
                break;

            int intLen = (int)payloadLen;
            byte[] payload = intLen > 0 ? new byte[intLen] : [];
            if (intLen > 0)
            {
                if (!await ReadExactAsync(conn.Stream, payload, intLen, ct).ConfigureAwait(false))
                    break;

                for (int i = 0; i < intLen; i++)
                    payload[i] ^= maskKey[i & 3];
            }

            switch (opcode)
            {
                case 0x8: // Close
                    await conn.SendFrameAsync(0x8, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
                    return;

                case 0x9: // Ping
                    await conn.SendFrameAsync(0xA, payload, ct).ConfigureAwait(false);
                    break;

                case 0xA: // Pong
                    break;

                case 0x1: // Text
                    if (fin && fragmentBuffer.Length == 0)
                    {
                        await HandleJsonMessageAsync(conn, Encoding.UTF8.GetString(payload)).ConfigureAwait(false);
                    }
                    else
                    {
                        fragmentedOpcode = 0x1;
                        fragmentBuffer.SetLength(0);
                        fragmentBuffer.Write(payload, 0, payload.Length);
                        if (fin)
                        {
                            string full = Encoding.UTF8.GetString(fragmentBuffer.GetBuffer(), 0, (int)fragmentBuffer.Length);
                            fragmentBuffer.SetLength(0);
                            await HandleJsonMessageAsync(conn, full).ConfigureAwait(false);
                        }
                    }
                    break;

                case 0x0: // Continuation
                    if (fragmentedOpcode == 0x1)
                    {
                        if (fragmentBuffer.Length + payload.Length > MaxPayloadBytes)
                            return;
                        fragmentBuffer.Write(payload, 0, payload.Length);
                        if (fin)
                        {
                            string full = Encoding.UTF8.GetString(fragmentBuffer.GetBuffer(), 0, (int)fragmentBuffer.Length);
                            fragmentBuffer.SetLength(0);
                            fragmentedOpcode = 0;
                            await HandleJsonMessageAsync(conn, full).ConfigureAwait(false);
                        }
                    }
                    break;
            }
        }
    }

    private async Task HandleJsonMessageAsync(BridgeClientConnection conn, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        BridgeIncomingMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize(json, AppJsonContext.Default.BridgeIncomingMessage);
        }
        catch
        {
            return;
        }
        if (msg == null)
            return;

        string? normalizedBrowser = string.IsNullOrWhiteSpace(msg.Browser)
            ? null
            : msg.Browser.Trim().ToLowerInvariant();

        switch (msg.Type.ToLowerInvariant())
        {
            case "hello":
                lock (_connLock)
                {
                    if (normalizedBrowser != null)
                        conn.BrowserKey = normalizedBrowser;
                    if (msg.Version > 0)
                        conn.ProtocolVersion = msg.Version;
                }
                SessionsChanged?.Invoke();
                break;

            case "ping":
                lock (_connLock)
                {
                    if (normalizedBrowser != null)
                        conn.BrowserKey = normalizedBrowser;
                    if (msg.Version > 0)
                        conn.ProtocolVersion = msg.Version;
                }
                await conn.SendTextAsync("{\"type\":\"pong\"}").ConfigureAwait(false);
                break;

            case "tabs":
                lock (_connLock)
                {
                    if (normalizedBrowser != null)
                        conn.BrowserKey = normalizedBrowser;
                    if (msg.Version > 0)
                        conn.ProtocolVersion = msg.Version;
                    if (msg.Tabs != null)
                    {
                        int cap = Math.Min(msg.Tabs.Count, MaxTabsPerConnection);
                        conn.Tabs = msg.Tabs.GetRange(0, cap);
                    }
                    else
                    {
                        conn.Tabs = [];
                    }
                }
                SessionsChanged?.Invoke();
                break;
        }
    }

    private static bool IsAllowedExtensionOrigin(string origin)
    {
        string o = origin.Trim();
        return o.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
               o.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
               o.StartsWith("extension://", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadHttpHeaderAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHandshakeBytes];
        int total = 0;
        var one = new byte[1];

        while (total < MaxHandshakeBytes)
        {
            int n = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (n <= 0)
                return null;
            if (total == 0 && (one[0] < 0x20 || one[0] > 0x7E))
                return null;
            buffer[total++] = one[0];
            if (total >= 4 &&
                buffer[total - 4] == (byte)'\r' &&
                buffer[total - 3] == (byte)'\n' &&
                buffer[total - 2] == (byte)'\r' &&
                buffer[total - 1] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(buffer, 0, total);
            }
        }
        return null;
    }

    private static Dictionary<string, string> ParseHeaders(string raw, out string requestLine)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = raw.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        requestLine = lines.Length > 0 ? lines[0] : string.Empty;

        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            string k = lines[i][..colon].Trim();
            string v = lines[i][(colon + 1)..].Trim();
            dict[k] = v;
        }
        return dict;
    }

    private static async Task WriteHttpErrorAsync(NetworkStream stream, string status)
    {
        byte[] bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buf, int count, CancellationToken ct)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buf.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n <= 0)
                return false;
            read += n;
        }
        return true;
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        TcpListener? listener;
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            _isListening = false;
            cts = _cts;
            listener = _listener;
            _cts = null;
            _listener = null;
        }

        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { cts?.Dispose(); } catch { }

        lock (_connLock)
        {
            foreach (var c in _connections.Values)
                c.Dispose();
            _connections.Clear();
        }
    }
}
