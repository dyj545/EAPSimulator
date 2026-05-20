using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using EAPSimulator.Core.Protocols.SecsGem.SecsII;
using Microsoft.Extensions.Logging;

namespace EAPSimulator.Core.Protocols.SecsGem.Hsms;

/// <summary>
/// Manages a single HSMS TCP connection (client or server).
/// Handles raw byte framing, control messages, and T3/T5/T6/T7/T8 timers.
/// </summary>
public class HsmsConnection : IAsyncDisposable
{
    private readonly ILogger _logger;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private Timer? _t6Timer;
    private Timer? _t7Timer;
    private uint _systemBytesCounter;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly int _t3Timeout;
    private readonly int _t5Timeout;
    private readonly int _t6Timeout;
    private readonly int _t7Timeout;
    private readonly int _t8Timeout;
    private readonly ushort _sessionId;

    private readonly ConcurrentDictionary<uint, TaskCompletionSource<HsmsMessage>> _pendingReplies = new();

    public HsmsStateMachine StateMachine { get; } = new();
    public bool IsConnected => _tcpClient?.Connected == true && StateMachine.State != HsmsState.NotConnected;
    public string? RemoteEndpoint { get; private set; }
    public ushort SessionId => _sessionId;

    public event EventHandler<HsmsMessage>? MessageReceived;
    public event EventHandler<HsmsMessage>? MessageSent;
    public event EventHandler? ConnectionClosed;

    public HsmsConnection(ILogger logger, ushort sessionId, int t3 = 45000, int t5 = 10000, int t6 = 5000, int t7 = 10000, int t8 = 5000)
    {
        _logger = logger;
        _sessionId = sessionId;
        _t3Timeout = t3;
        _t5Timeout = t5;
        _t6Timeout = t6;
        _t7Timeout = t7;
        _t8Timeout = t8;
    }

    public void AttachTcpClient(TcpClient client)
    {
        _tcpClient = client;
        _stream = client.GetStream();
        RemoteEndpoint = client.Client.RemoteEndPoint?.ToString();
        StateMachine.TryTrigger(HsmsEvent.Connect);
        StartReceiveLoop();
        StartT7Timer();
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct);
        _stream = _tcpClient.GetStream();
        RemoteEndpoint = $"{host}:{port}";
        StateMachine.TryTrigger(HsmsEvent.Connect);
        StartReceiveLoop();
        StartT7Timer();
    }

    private void StartReceiveLoop()
    {
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var headerBuffer = new byte[HsmsHeader.HeaderLength];

        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                // Read HSMS header (14 bytes)
                int headerRead = 0;
                while (headerRead < HsmsHeader.HeaderLength)
                {
                    int n = await _stream.ReadAsync(headerBuffer, headerRead, HsmsHeader.HeaderLength - headerRead, ct);
                    if (n == 0) goto ConnectionClosed;
                    headerRead += n;
                    ResetT8Timer();
                }

                var header = HsmsHeader.Decode(headerBuffer);

                // Read remaining data (Length - 10, since Length covers bytes after the 4-byte length field)
                int dataLen = (int)header.Length - 10;
                byte[]? data = null;

                if (dataLen > 0)
                {
                    data = new byte[dataLen];
                    int dataRead = 0;
                    while (dataRead < dataLen)
                    {
                        int n = await _stream.ReadAsync(data, dataRead, dataLen - dataRead, ct);
                        if (n == 0) goto ConnectionClosed;
                        dataRead += n;
                        ResetT8Timer();
                    }
                }

                // Message fully received — stop T8 until next message starts
                StopT8Timer();

                var hsmsMsg = new HsmsMessage { Header = header, Data = data };
                HandleReceivedMessage(hsmsMsg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HSMS receive loop error");
        }

    ConnectionClosed:
        CloseConnection();
    }

    private void HandleReceivedMessage(HsmsMessage msg)
    {
        MessageReceived?.Invoke(this, msg);

        if (msg.IsControlMessage)
        {
            HandleControlMessage(msg);
        }
        else
        {
            // Data message - resolve pending reply if this is a reply (W bit = 0)
            var secsMsg = msg.DecodeSecsMessage();
            if (secsMsg != null && !secsMsg.WBit && _pendingReplies.TryRemove(msg.Header.SystemBytes, out var tcs))
            {
                tcs.TrySetResult(msg);
            }
        }
    }

    private void HandleControlMessage(HsmsMessage msg)
    {
        switch (msg.Header.SType)
        {
            case HsmsMessageType.SelectReq:
                StateMachine.TryTrigger(HsmsEvent.ReceiveSelectReq);
                StopT7Timer();
                // Send SelectRsp
                var selectRsp = HsmsMessage.CreateControlMessage(HsmsMessageType.SelectRsp, _sessionId, msg.Header.SystemBytes);
                _ = SendHsmsAsync(selectRsp, CancellationToken.None);
                break;

            case HsmsMessageType.SelectRsp:
                StateMachine.TryTrigger(HsmsEvent.ReceiveSelectRsp);
                StopT6Timer();
                StopT7Timer();
                if (_pendingReplies.TryRemove(msg.Header.SystemBytes, out var tcs))
                    tcs.TrySetResult(msg);
                break;

            case HsmsMessageType.DeselectReq:
                StateMachine.TryTrigger(HsmsEvent.ReceiveDeselectReq);
                var deselectRsp = HsmsMessage.CreateControlMessage(HsmsMessageType.DeselectRsp, _sessionId, msg.Header.SystemBytes);
                _ = SendHsmsAsync(deselectRsp, CancellationToken.None);
                break;

            case HsmsMessageType.DeselectRsp:
                StateMachine.TryTrigger(HsmsEvent.ReceiveDeselectRsp);
                if (_pendingReplies.TryRemove(msg.Header.SystemBytes, out var dcs))
                    dcs.TrySetResult(msg);
                break;

            case HsmsMessageType.LinkTestReq:
                StateMachine.TryTrigger(HsmsEvent.ReceiveLinkTestReq);
                var ltRsp = HsmsMessage.CreateControlMessage(HsmsMessageType.LinkTestRsp, _sessionId, msg.Header.SystemBytes);
                _ = SendHsmsAsync(ltRsp, CancellationToken.None);
                break;

            case HsmsMessageType.LinkTestRsp:
                StateMachine.TryTrigger(HsmsEvent.ReceiveLinkTestRsp);
                break;

            case HsmsMessageType.SeparateReq:
                StateMachine.TryTrigger(HsmsEvent.ReceiveSeparateReq);
                CloseConnection();
                break;

            case HsmsMessageType.RejectReq:
                StateMachine.TryTrigger(HsmsEvent.ReceiveRejectReq);
                break;
        }
    }

    private uint NextSystemBytes() => Interlocked.Increment(ref _systemBytesCounter);

    public async Task<HsmsMessage> SendSelectAsync(CancellationToken ct)
    {
        var sysBytes = NextSystemBytes();
        var selectReq = HsmsMessage.CreateControlMessage(HsmsMessageType.SelectReq, _sessionId, sysBytes);

        var tcs = new TaskCompletionSource<HsmsMessage>();
        _pendingReplies[sysBytes] = tcs;

        await SendHsmsAsync(selectReq, ct);
        StateMachine.TryTrigger(HsmsEvent.Select);
        StartT6Timer();

        using var timeoutCts = new CancellationTokenSource(_t6Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            StateMachine.TryTrigger(HsmsEvent.SelectTimeout);
            throw new TimeoutException("HSMS Select timeout (T6)");
        }
    }

    public async Task<HsmsMessage> SendDataMessageAsync(SecsMessage secsMsg, bool waitForReply, CancellationToken ct)
    {
        var sysBytes = NextSystemBytes();
        var hsmsMsg = HsmsMessage.CreateDataMessage(secsMsg, _sessionId, sysBytes);

        TaskCompletionSource<HsmsMessage>? tcs = null;
        if (waitForReply && secsMsg.WBit)
        {
            tcs = new TaskCompletionSource<HsmsMessage>();
            _pendingReplies[sysBytes] = tcs;
        }

        // Send bytes on wire and fire MessageSent immediately
        // so the message appears in the log before the reply arrives
        await SendHsmsAsync(hsmsMsg, ct);

        if (tcs != null)
        {
            // Wait for reply with T3 timeout
            using var timeoutCts = new CancellationTokenSource(_t3Timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                return await tcs.Task.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                _pendingReplies.TryRemove(sysBytes, out _);
                throw new TimeoutException($"HSMS T3 timeout waiting for reply to S{secsMsg.Stream}F{secsMsg.Function}");
            }
        }

        return hsmsMsg;
    }

    public async Task SendHsmsAsync(HsmsMessage msg, CancellationToken ct)
    {
        var data = msg.Encode();
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_stream == null) throw new InvalidOperationException("Not connected");
            await _stream.WriteAsync(data, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }

        MessageSent?.Invoke(this, msg);
    }

    // T7 timer: not-selected timeout
    private void StartT7Timer()
    {
        _t7Timer?.Dispose();
        _t7Timer = new Timer(_ =>
        {
            if (StateMachine.State == HsmsState.ConnectedNotSelected)
            {
                StateMachine.TryTrigger(HsmsEvent.T7Timeout);
                CloseConnection();
            }
        }, null, _t7Timeout, Timeout.Infinite);
    }

    private void StopT7Timer() => _t7Timer?.Dispose();

    // T6 timer: select timeout
    private void StartT6Timer()
    {
        _t6Timer?.Dispose();
        _t6Timer = new Timer(_ =>
        {
            StateMachine.TryTrigger(HsmsEvent.SelectTimeout);
        }, null, _t6Timeout, Timeout.Infinite);
    }

    private void StopT6Timer() => _t6Timer?.Dispose();

    // T8 timer: inter-byte timeout
    private Timer? _t8Timer;
    private volatile bool _t8Stopped = true;

    private void ResetT8Timer()
    {
        _t8Stopped = false;
        _t8Timer?.Dispose();
        _t8Timer = new Timer(_ =>
        {
            if (_t8Stopped) return;
            _logger.LogWarning("HSMS T8 timeout");
            CloseConnection();
        }, null, _t8Timeout, Timeout.Infinite);
    }

    private void StopT8Timer()
    {
        _t8Stopped = true;
        _t8Timer?.Dispose();
        _t8Timer = null;
    }

    public void CloseConnection()
    {
        _t6Timer?.Dispose();
        _t7Timer?.Dispose();
        _t8Timer?.Dispose();
        _receiveCts?.Cancel();
        _stream?.Close();
        _tcpClient?.Close();

        if (StateMachine.State != HsmsState.NotConnected)
        {
            StateMachine.TryTrigger(HsmsEvent.Disconnect);
        }

        // Reject all pending replies
        foreach (var kvp in _pendingReplies)
        {
            if (_pendingReplies.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetCanceled();
        }

        ConnectionClosed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        CloseConnection();
        _sendLock.Dispose();
        if (_receiveTask != null)
            await _receiveTask;
    }
}
