using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Cueway.Media;

internal readonly record struct MpvState(
    bool IsConnected,
    bool IsPaused,
    bool HasVideo,
    string Title);

internal sealed class MpvIpcClient
{
    public MpvState QueryState(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            return default;

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(30);

            string title = GetStringProperty(pipe, "media-title") ?? GetStringProperty(pipe, "filename") ?? "mpv";
            bool? paused = GetBoolProperty(pipe, "pause");
            bool? hasVideo = HasVideoStream(pipe);

            return new MpvState(
                IsConnected: true,
                IsPaused: paused ?? false,
                HasVideo: hasVideo ?? true,
                Title: string.IsNullOrWhiteSpace(title) ? "mpv" : title);
        }
        catch
        {
            return default;
        }
    }

    public bool TogglePlayPause(string pipeName)
        => SendCommand(pipeName, """{"command":["cycle","pause"]}""");

    public bool SetPause(string pipeName, bool paused)
        => SendCommand(pipeName, paused
            ? """{"command":["set_property","pause",true]}"""
            : """{"command":["set_property","pause",false]}""");

    public bool Next(string pipeName)
        => SendCommand(pipeName, """{"command":["playlist-next"]}""");

    public bool Previous(string pipeName)
        => SendCommand(pipeName, """{"command":["playlist-prev"]}""");

    public bool Seek(string pipeName, int deltaSeconds)
        => SendCommand(pipeName, $"{{\"command\":[\"seek\",{deltaSeconds},\"relative\"]}}");

    private static bool SendCommand(string pipeName, string jsonCommand)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            return false;

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(40);
            byte[] bytes = Encoding.UTF8.GetBytes(jsonCommand + "\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetStringProperty(NamedPipeClientStream pipe, string prop)
    {
        string? line = SendAndReadLine(pipe, $"{{\"command\":[\"get_property\",\"{prop}\"]}}\n");
        if (line == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
                return data.GetString();
        }
        catch { }
        return null;
    }

    private static bool? GetBoolProperty(NamedPipeClientStream pipe, string prop)
    {
        string? line = SendAndReadLine(pipe, $"{{\"command\":[\"get_property\",\"{prop}\"]}}\n");
        if (line == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.True) return true;
                if (data.ValueKind == JsonValueKind.False) return false;
            }
        }
        catch { }
        return null;
    }

    private static bool? HasVideoStream(NamedPipeClientStream pipe)
    {
        string? line = SendAndReadLine(pipe, """{"command":["get_property","vid"]}""" + "\n");
        if (line == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.Number) return true;
                if (data.ValueKind == JsonValueKind.False) return false;
            }
        }
        catch { }
        return null;
    }

    private static string? SendAndReadLine(NamedPipeClientStream pipe, string commandLine)
    {
        try
        {
            byte[] req = Encoding.UTF8.GetBytes(commandLine);
            using var writeCts = new CancellationTokenSource(60);
            pipe.WriteAsync(req, 0, req.Length, writeCts.Token).GetAwaiter().GetResult();
            pipe.FlushAsync(writeCts.Token).GetAwaiter().GetResult();

            using var cts = new CancellationTokenSource(80);
            byte[] buf = new byte[1024];
            int buffered = 0;

            while (!cts.IsCancellationRequested)
            {
                if (buffered >= buf.Length)
                    Array.Resize(ref buf, buf.Length * 2);

                int read = pipe.ReadAsync(buf, buffered, buf.Length - buffered, cts.Token).GetAwaiter().GetResult();
                if (read <= 0)
                    break;
                buffered += read;

                int lineStart = 0;
                while (true)
                {
                    int nlRel = Array.IndexOf(buf, (byte)'\n', lineStart, buffered - lineStart);
                    if (nlRel < 0)
                        break;

                    string line = Encoding.UTF8.GetString(buf, lineStart, nlRel - lineStart).Trim();
                    lineStart = nlRel + 1;
                    // Skip async event lines until we receive the command response ("error" field)
                    if (line.Contains("\"error\"", StringComparison.Ordinal))
                        return line;
                }

                if (lineStart > 0)
                {
                    int rem = buffered - lineStart;
                    if (rem > 0)
                        Buffer.BlockCopy(buf, lineStart, buf, 0, rem);
                    buffered = rem;
                }
            }
        }
        catch
        {
            // Pipe read/write timeout or closed
        }
        return null;
    }
}
