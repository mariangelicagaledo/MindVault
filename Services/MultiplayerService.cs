using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Networking;
using System.Diagnostics;
using System.Text.Json;

namespace mindvault.Services;

// Network connectivity state used by hosting/join flows
public enum LocalNetworkStatus
{
    Wifi,
    Ethernet,
    Cellular,
    Unknown
}

public partial class MultiplayerService
{
    public string SelfId { get; private set; } = string.Empty;

    public class ParticipantInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public bool Ready { get; set; }
    }

    private static readonly Random _rng = new();

    public string? CurrentRoomCode { get; private set; }
    public int HostPort { get; private set; }
    public IPEndPoint? HostEndpoint { get; private set; }

    private CancellationTokenSource? _hostCts;
    private TcpListener? _listener;

    // sessions keyed by ID; maintain maps
    private readonly Dictionary<string, (TcpClient client, ParticipantInfo info)> _sessionsById = new();
    private readonly Dictionary<string, string> _nameToId = new();

    // scoreboard keyed by ID
    private readonly Dictionary<string, int> _scores = new();

    // anti-spam
    private readonly Dictionary<string, DateTime> _lastBuzzAt = new();

    // client side connection
    private TcpClient? _client;
    private CancellationTokenSource? _clientCts;
    private NetworkStream? _clientStream;

    // Buzz state (host)
    private volatile bool _buzzLocked = false;
    private volatile string? _buzzWinnerId;
    private volatile string? _buzzWinnerName;
    private readonly HashSet<string> _disabledBuzzers = new(); // IDs
    private CancellationTokenSource? _answerCts;

    // Question state (host)
    private int _currentIndex = 0; // 1-based
    private int _totalCards = 0;

    // Host-side events
    public event Action<ParticipantInfo>? HostParticipantJoined;
    public event Action<string>? HostParticipantLeft; // id
    public event Action<string, bool>? HostParticipantReadyChanged; // id, ready
    public event Action<ParticipantInfo>? HostBuzzWinner;

    // Client-side events
    public event Action<ParticipantInfo>? ClientParticipantJoined;
    public event Action<string>? ClientParticipantLeft; // id
    public event Action<string, bool>? ClientParticipantReadyChanged; // id, ready
    public event Action? ClientGameStarted;
    public event Action<string, string, long>? ClientBuzzingStarted; // id, name, deadlineUtcTicks
    public event Action? ClientBuzzReset;
    public event Action<string, int>? ClientScoreUpdated; // id, score
    public event Action<string, bool>? ClientBuzzerEnabledChanged; // id|* , enabled
    public event Action<int, int>? ClientQuestionStateChanged;
    public event Action<string>? ClientTimeUp; // id
    public event Action<string>? ClientStopTimer; // id
    public event Action<string>? ClientCorrectAnswer; // text
    public event Action<GameOverPayload>? ClientGameOver;
    public event Action<string, string>? ClientWrong; // id, name

    public record GameOverPayload(List<(string id, string name, int score)> FinalScores, List<string> Winners, string DeckTitle);

    public string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = Enumerable.Range(0, 5)
            .Select(_ => alphabet[_rng.Next(alphabet.Length)])
            .ToArray();
        var code = new string(chars);
        CurrentRoomCode = code;
        return code;
    }

    public LocalNetworkStatus GetLocalNetworkStatus()
    {
        try
        {
            var profiles = Connectivity.Current.ConnectionProfiles;
            if (profiles.Contains(ConnectionProfile.WiFi))
                return LocalNetworkStatus.Wifi;
            if (profiles.Contains(ConnectionProfile.Ethernet))
                return LocalNetworkStatus.Ethernet;
            if (profiles.Contains(ConnectionProfile.Cellular))
                return LocalNetworkStatus.Cellular;
        }
        catch { }
        return LocalNetworkStatus.Unknown;
    }

    public bool HasLocalNetworkPath() => GetLocalNetworkStatus() is LocalNetworkStatus.Wifi or LocalNetworkStatus.Ethernet;

    public void SetJoinedRoom(string code) => CurrentRoomCode = code;

    public async Task<(bool ok, string? error)> StartHostingAsync(string code, CancellationToken cancellationToken = default)
    {
        StopHosting();
        CurrentRoomCode = code;
        _sessionsById.Clear();
        _nameToId.Clear();
        _scores.Clear();
        _buzzLocked = false; _buzzWinnerId = null; _buzzWinnerName = null; _disabledBuzzers.Clear();
        CancelAnswerTimer();
        _currentIndex = 0; _totalCards = 0;

        try
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            HostPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _hostCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _hostCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                        _ = Task.Run(() => HandleClientAsync(client, ct));
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }, ct);

            _ = Task.Run(() => BroadcastBeaconLoopAsync(code, HostPort, ct), ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            StopHosting();
            return (false, ex.Message);
        }
    }

    public void StopHosting()
    {
        try { _hostCts?.Cancel(); } catch { }
        _hostCts = null;
        try { _listener?.Stop(); } catch { }
        _listener = null;
        foreach (var s in _sessionsById.Values) { try { s.client.Close(); } catch { } }
        _sessionsById.Clear();
        _nameToId.Clear();
        _scores.Clear();
        _buzzLocked = false; _buzzWinnerId = null; _buzzWinnerName = null; _disabledBuzzers.Clear();
        CancelAnswerTimer();
        _currentIndex = 0; _totalCards = 0;
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null || !line.StartsWith("JOIN|")) return;
            var parts = line.Split('|');
            var name = parts.Length > 1 ? parts[1] : $"Player{_sessionsById.Count + 1}";
            var avatar = parts.Length > 2 ? parts[2] : string.Empty;

            var id = Guid.NewGuid().ToString("N");
            var info = new ParticipantInfo { Id = id, Name = name, Avatar = avatar, Ready = false };
            lock (_sessionsById)
            {
                _sessionsById[id] = (client, info);
                _nameToId[name] = id;
                if (!_scores.ContainsKey(id)) _scores[id] = 0;
            }

            // Send WELCOME with assigned id
            await writer.WriteLineAsync($"WELCOME|{id}").ConfigureAwait(false);

            HostParticipantJoined?.Invoke(info);

            // bootstrap new client
            foreach (var kv in _sessionsById.Values)
                await writer.WriteLineAsync($"PJOIN|{kv.info.Id}|{kv.info.Name}|{kv.info.Avatar}|{(kv.info.Ready ? 1 : 0)}").ConfigureAwait(false);
            foreach (var sc in _scores)
                await writer.WriteLineAsync($"SCORE|{sc.Key}|{sc.Value}").ConfigureAwait(false);
            if (_totalCards > 0)
                await writer.WriteLineAsync($"STATE|{_currentIndex}|{_totalCards}").ConfigureAwait(false);
            if (_buzzLocked && !string.IsNullOrEmpty(_buzzWinnerId))
            {
                var deadline = DateTime.UtcNow.AddSeconds(5).Ticks; // rough guess on reconnect
                await writer.WriteLineAsync($"BUZZWIN|{_buzzWinnerId}|{_buzzWinnerName}|{deadline}").ConfigureAwait(false);
            }
            foreach (var n in _disabledBuzzers)
                await writer.WriteLineAsync($"DISABLEUSER|{n}").ConfigureAwait(false);

            await BroadcastAsync($"PJOIN|{info.Id}|{info.Name}|{info.Avatar}|0").ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                var msg = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (msg is null) break;

                if (msg.StartsWith("READY|"))
                {
                    // Accept both READY|1 and READY|id|1
                    var pp = msg.Split('|');
                    string pid; bool ready;
                    if (pp.Length == 2)
                    {
                        pid = id; ready = pp[1] == "1";
                    }
                    else
                    {
                        pid = pp.Length > 1 ? pp[1] : id;
                        ready = pp.Length > 2 && pp[2] == "1";
                    }
                    if (_sessionsById.TryGetValue(pid, out var tuple))
                    {
                        tuple.info.Ready = ready;
                        HostParticipantReadyChanged?.Invoke(pid, ready);
                        await BroadcastAsync($"PREADY|{pid}|{(ready ? 1 : 0)}").ConfigureAwait(false);
                    }
                }
                else if (msg == "LEAVE")
                {
                    break; // client will be removed in finally
                }
                else if (msg.StartsWith("BUZZ"))
                {
                    var pid = id;
                    if (_disabledBuzzers.Contains(pid)) continue;
                    var now = DateTime.UtcNow;
                    lock (_lastBuzzAt)
                    {
                        if (_lastBuzzAt.TryGetValue(pid, out var last) && (now - last).TotalMilliseconds < 250)
                            continue;
                        _lastBuzzAt[pid] = now;
                    }
                    if (!_buzzLocked)
                    {
                        _buzzLocked = true;
                        _buzzWinnerId = pid;
                        _buzzWinnerName = _sessionsById[pid].info.Name;
                        var winnerInfo = _sessionsById[pid].info;
                        HostBuzzWinner?.Invoke(winnerInfo);
                        var deadline = DateTime.UtcNow.AddSeconds(10).Ticks;
                        await BroadcastAsync($"BUZZWIN|{winnerInfo.Id}|{winnerInfo.Name}|{deadline}").ConfigureAwait(false);
                        StartAnswerTimer(TimeSpan.FromSeconds(10), pid);
                    }
                }
            }
        }
        catch { }
        finally
        {
            string? removedId = null;
            lock (_sessionsById)
            {
                foreach (var k in _sessionsById.Keys.ToList())
                {
                    if (_sessionsById[k].client == client)
                    {
                        removedId = k;
                        _sessionsById.Remove(k);
                        break;
                    }
                }
            }
            if (removedId is not null)
            {
                HostParticipantLeft?.Invoke(removedId);
                await BroadcastAsync($"PLEFT|{removedId}").ConfigureAwait(false);
                if (_buzzWinnerId == removedId)
                {
                    _buzzLocked = false; _buzzWinnerId = null; _buzzWinnerName = null;
                    await BroadcastAsync("BUZZRESET").ConfigureAwait(false);
                }
                lock (_disabledBuzzers) _disabledBuzzers.Remove(removedId);
            }
        }
    }

    private async Task BroadcastAsync(string line)
    {
        List<TcpClient> clients;
        lock (_sessionsById) clients = _sessionsById.Values.Select(v => v.client).ToList();
        foreach (var c in clients)
        {
            try
            {
                using var writer = new StreamWriter(c.GetStream(), new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private static async Task BroadcastBeaconLoopAsync(string code, int port, CancellationToken ct)
    {
        var message = $"MINDVAULT|CODE={code}|PORT={port}";
        var data = Encoding.UTF8.GetBytes(message);
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        var endPoint = new IPEndPoint(IPAddress.Broadcast, 41500);
        while (!ct.IsCancellationRequested)
        {
            try { await udp.SendAsync(data, data.Length, endPoint).ConfigureAwait(false); }
            catch { }
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    public bool AreAllParticipantsReady()
    {
        lock (_sessionsById)
        {
            if (_sessionsById.Count == 0) return false;
            return _sessionsById.Values.All(s => s.info.Ready);
        }
    }

    public async Task<(bool started, string? error)> TryStartGameAsync()
    {
        if (!AreAllParticipantsReady())
            return (false, "Not all participants are ready");
        OpenBuzzForAll();
        await BroadcastAsync("START").ConfigureAwait(false);
        await BroadcastAsync("BUZZRESET").ConfigureAwait(false);
        await BroadcastAsync("ENABLEALL").ConfigureAwait(false);
        return (true, null);
    }

    public void OpenBuzzForAll()
    {
        _buzzLocked = false; _buzzWinnerId = null; _buzzWinnerName = null;
        lock (_disabledBuzzers) _disabledBuzzers.Clear();
        CancelAnswerTimer();
        _ = BroadcastAsync("ENABLEALL");
    }

    public async Task ReopenBuzzExceptWinnerAsync()
    {
        var loser = _buzzWinnerId;
        _buzzLocked = false; _buzzWinnerId = null; _buzzWinnerName = null;
        CancelAnswerTimer();
        if (!string.IsNullOrEmpty(loser))
        {
            lock (_disabledBuzzers) _disabledBuzzers.Add(loser);
            await BroadcastAsync($"DISABLEUSER|{loser}");
            // Announce wrong answer, chance to steal
            var name = _sessionsById.TryGetValue(loser, out var tuple) ? tuple.info.Name : "";
            await BroadcastAsync($"WRONG|{loser}|{name}");
        }
        await BroadcastAsync("BUZZRESET");
    }

    public void HostAwardPoint(string id, int delta)
    {
        lock (_scores)
        {
            _scores.TryGetValue(id, out var cur);
            cur = cur + delta;
            if (cur < 0) cur = 0;
            _scores[id] = cur;
            _ = BroadcastAsync($"SCORE|{id}|{cur}");
        }
    }

    public void UpdateQuestionState(int index, int total)
    {
        _currentIndex = index; _totalCards = total;
        _ = BroadcastAsync($"STATE|{index}|{total}");
    }

    private void StartAnswerTimer(TimeSpan duration, string winnerId)
    {
        CancelAnswerTimer();
        _answerCts = new CancellationTokenSource();
        var ct = _answerCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(duration, ct); }
            catch (TaskCanceledException) { return; }
            catch { return; }
            if (ct.IsCancellationRequested) return;
            if (_buzzLocked && string.Equals(_buzzWinnerId, winnerId, StringComparison.Ordinal))
            {
                Debug.WriteLine($"[Multiplayer] TIMEUP for {winnerId}");
                await BroadcastAsync($"TIMEUP|{winnerId}");
            }
        }, ct);
    }

    private void CancelAnswerTimer()
    {
        try { _answerCts?.Cancel(); } catch { }
        _answerCts = null;
    }

    public void HostStopTimerFor(string id)
    {
        _ = BroadcastAsync($"STOPTIMER|{id}");
    }

    public void HostAnnounceCorrectAnswer(string answer)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(answer ?? string.Empty));
        _ = BroadcastAsync($"CORRECT|{b64}");
    }

    public void HostGameOver(string deckTitle)
    {
        var scores = new List<(string id, string name, int score)>();
        lock (_sessionsById)
        {
            foreach (var kv in _scores)
            {
                var id = kv.Key; var score = kv.Value;
                var name = _sessionsById.TryGetValue(id, out var t) ? t.info.Name : id;
                scores.Add((id, name, score));
            }
        }
        var sorted = scores.OrderByDescending(s => s.score).ToList();
        var top = sorted.FirstOrDefault().score;
        var winners = sorted.Where(s => s.score == top).Select(s => s.name).ToList();
        var payload = new GameOverPayload(sorted, winners, deckTitle);
        var json = JsonSerializer.Serialize(payload);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        _ = BroadcastAsync($"GAMEOVER|{b64}");
        OpenBuzzForAll();
    }

    public async Task<(bool ok, string? error)> DiscoverHostAsync(string code, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(6);
        var tcs = new TaskCompletionSource<(bool ok, string? error)>();
        using var cts = new CancellationTokenSource(timeout.Value);
        _ = Task.Run(async () =>
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 41500));
                while (!cts.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    var msg = Encoding.UTF8.GetString(result.Buffer);
                    if (!msg.StartsWith("MINDVAULT|")) continue;
                    var parts = msg.Split('|');
                    var codePart = parts.FirstOrDefault(p => p.StartsWith("CODE="));
                    var portPart = parts.FirstOrDefault(p => p.StartsWith("PORT="));
                    if (codePart is null || portPart is null) continue;
                    var foundCode = codePart.Substring("CODE=".Length);
                    if (!string.Equals(foundCode, code, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!int.TryParse(portPart.Substring("PORT=".Length), out var port)) continue;

                    HostEndpoint = new IPEndPoint(result.RemoteEndPoint.Address, port);
                    HostPort = port;
                    CurrentRoomCode = code;
                    tcs.TrySetResult((true, null));
                    return;
                }
                tcs.TrySetResult((false, "Timed out searching for host"));
            }
            catch (OperationCanceledException) { tcs.TrySetResult((false, "Timed out searching for host")); }
            catch (Exception ex) { tcs.TrySetResult((false, ex.Message)); }
        });
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<(bool ok, string? error)> ConnectToHostAsync()
    {
        if (HostEndpoint is null)
            return (false, "Host endpoint not set.");
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(HostEndpoint.Address, HostEndpoint.Port);
            _clientStream = _client.GetStream();
            _clientCts = new CancellationTokenSource();
            var ct = _clientCts.Token;
            _ = Task.Run(() => ClientListenLoopAsync(ct), ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            DisconnectClient();
            return (false, ex.Message);
        }
    }

    public async Task SendJoinAsync(string name, string avatar)
    {
        if (_clientStream is null) return;
        var writer = new StreamWriter(_clientStream, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync($"JOIN|{name}|{avatar}");
    }

    public async Task SendReadyAsync(bool ready)
    {
        if (_clientStream is null) return;
        var writer = new StreamWriter(_clientStream, new UTF8Encoding(false)) { AutoFlush = true };
        // Send two-part READY for broad compatibility
        await writer.WriteLineAsync($"READY|{(ready ? 1 : 0)}");
    }

    public async Task SendBuzzAsync()
    {
        if (_clientStream is null) return;
        var writer = new StreamWriter(_clientStream, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync("BUZZ");
    }

    public async Task SendLeaveAsync()
    {
        if (_clientStream is null) return;
        try
        {
            var writer = new StreamWriter(_clientStream, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteLineAsync("LEAVE");
        }
        catch { }
    }

    public void DisconnectClient()
    {
        try { _clientCts?.Cancel(); } catch { }
        _clientCts = null;
        try { _clientStream?.Close(); } catch { }
        _clientStream = null;
        try { _client?.Close(); } catch { }
        _client = null;
    }

    private async Task ClientListenLoopAsync(CancellationToken ct)
    {
        try
        {
            if (_clientStream is null) return;
            using var reader = new StreamReader(_clientStream, Encoding.UTF8, false, 1024, leaveOpen: true);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (line.StartsWith("WELCOME|"))
                {
                    // cache assigned client id
                    SelfId = line.Substring("WELCOME|".Length);
                }
                else if (line.StartsWith("PJOIN|"))
                {
                    // PJOIN|id|name|avatar|ready
                    var parts = line.Split('|');
                    var p = new ParticipantInfo
                    {
                        Id = parts.Length > 1 ? parts[1] : string.Empty,
                        Name = parts.Length > 2 ? parts[2] : string.Empty,
                        Avatar = parts.Length > 3 ? parts[3] : string.Empty,
                        Ready = parts.Length > 4 && parts[4] == "1"
                    };
                    ClientParticipantJoined?.Invoke(p);
                }
                else if (line.StartsWith("PLEFT|"))
                {
                    var id = line.Substring("PLEFT|".Length);
                    ClientParticipantLeft?.Invoke(id);
                }
                else if (line.StartsWith("PREADY|"))
                {
                    var parts = line.Split('|');
                    var id = parts.Length > 1 ? parts[1] : string.Empty;
                    var ready = parts.Length > 2 && parts[2] == "1";
                    ClientParticipantReadyChanged?.Invoke(id, ready);
                }
                else if (line == "START")
                {
                    ClientGameStarted?.Invoke();
                }
                else if (line.StartsWith("BUZZWIN|"))
                {
                    // BUZZWIN|id|name|deadlineTicks
                    var parts = line.Split('|');
                    var id = parts.Length > 1 ? parts[1] : string.Empty;
                    var name = parts.Length > 2 ? parts[2] : string.Empty;
                    long ticks = 0; if (parts.Length > 3) long.TryParse(parts[3], out ticks);
                    ClientBuzzingStarted?.Invoke(id, name, ticks);
                }
                else if (line == "BUZZRESET")
                {
                    ClientBuzzReset?.Invoke();
                }
                else if (line.StartsWith("SCORE|"))
                {
                    var parts = line.Split('|');
                    var id = parts.Length > 1 ? parts[1] : string.Empty;
                    var score = 0;
                    if (parts.Length > 2) int.TryParse(parts[2], out score);
                    ClientScoreUpdated?.Invoke(id, score);
                }
                else if (line.StartsWith("DISABLEUSER|"))
                {
                    var id = line.Substring("DISABLEUSER|".Length);
                    ClientBuzzerEnabledChanged?.Invoke(id, false);
                }
                else if (line == "ENABLEALL")
                {
                    ClientBuzzerEnabledChanged?.Invoke("*", true);
                }
                else if (line.StartsWith("STATE|"))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3 && int.TryParse(parts[1], out var idx) && int.TryParse(parts[2], out var total))
                        ClientQuestionStateChanged?.Invoke(idx, total);
                }
                else if (line.StartsWith("TIMEUP|"))
                {
                    var id = line.Substring("TIMEUP|".Length);
                    ClientTimeUp?.Invoke(id);
                }
                else if (line.StartsWith("STOPTIMER|"))
                {
                    var id = line.Substring("STOPTIMER|".Length);
                    ClientStopTimer?.Invoke(id);
                }
                else if (line.StartsWith("CORRECT|"))
                {
                    var b64 = line.Substring("CORRECT|".Length);
                    string text = string.Empty;
                    try { text = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); } catch { }
                    ClientCorrectAnswer?.Invoke(text);
                }
                else if (line.StartsWith("WRONG|"))
                {
                    var parts = line.Split('|');
                    var id = parts.Length > 1 ? parts[1] : string.Empty;
                    var name = parts.Length > 2 ? parts[2] : string.Empty;
                    ClientWrong?.Invoke(id, name);
                }
                else if (line.StartsWith("GAMEOVER|"))
                {
                    var b64 = line.Substring("GAMEOVER|".Length);
                    try
                    {
                        var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                        var payload = JsonSerializer.Deserialize<GameOverPayload>(json);
                        if (payload is not null) ClientGameOver?.Invoke(payload);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}
