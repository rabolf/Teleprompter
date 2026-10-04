using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("TELEPROMPTER_URLS") ?? "http://0.0.0.0:5000");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

var hub = new Hub();

// "/view" is the page opened on the phone.
app.MapGet("/view", () => Results.File(
    Path.Combine(app.Environment.WebRootPath, "prompter.html"), "text/html"));

app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await hub.RunAsync(socket);
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine();
    Console.WriteLine("Teleprompter running.");
    Console.WriteLine("  Computer: http://localhost:5000");
    foreach (var ip in GetLanAddresses())
        Console.WriteLine($"  Phone:    http://{ip}:5000/view");
    Console.WriteLine();
});

app.Run();

static IEnumerable<IPAddress> GetLanAddresses() =>
    NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork);

/// <summary>
/// Relays messages between all connected sockets and remembers the current
/// session (config + scroll position) so late joiners catch up.
/// Incoming: {type:"start",config:{...}} | {type:"stop"} | {type:"scroll",dir:"up"|"down"}
/// </summary>
class Hub
{
    readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _clients = new();
    readonly object _lock = new();
    JsonElement? _config;
    int _steps;

    public async Task RunAsync(WebSocket socket)
    {
        _clients[socket] = new SemaphoreSlim(1, 1);
        try
        {
            string state;
            lock (_lock) state = StateMessage();
            await SendAsync(socket, state);

            var buffer = new byte[64 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                await HandleAsync(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        catch (WebSocketException) { }
        finally
        {
            _clients.TryRemove(socket, out _);
        }
    }

    async Task HandleAsync(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return; }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var t)) return;

            string outgoing;
            lock (_lock)
            {
                switch (t.GetString())
                {
                    case "start":
                        if (!root.TryGetProperty("config", out var cfg)) return;
                        _config = cfg.Clone();
                        _steps = 0;
                        outgoing = StateMessage();
                        break;
                    case "stop":
                        _config = null;
                        _steps = 0;
                        outgoing = StateMessage();
                        break;
                    case "scroll":
                        if (_config is null) return;
                        _steps += root.TryGetProperty("dir", out var d) && d.GetString() == "up" ? -1 : 1;
                        if (_steps < 0) _steps = 0;
                        outgoing = json;
                        break;
                    default:
                        return;
                }
            }
            await BroadcastAsync(outgoing);
        }
    }

    string StateMessage() => JsonSerializer.Serialize(new
    {
        type = "state",
        running = _config is not null,
        config = _config,
        steps = _steps
    });

    Task BroadcastAsync(string message) =>
        Task.WhenAll(_clients.Keys.Select(s => SendAsync(s, message)));

    async Task SendAsync(WebSocket socket, string message)
    {
        if (!_clients.TryGetValue(socket, out var gate)) return;
        await gate.WaitAsync();
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(Encoding.UTF8.GetBytes(message),
                    WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (WebSocketException) { }
        finally { gate.Release(); }
    }
}
