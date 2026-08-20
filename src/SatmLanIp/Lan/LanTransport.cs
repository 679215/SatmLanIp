using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SatmLanIp;

internal sealed class LanTransport
{
    private const float ProbeIntervalSec = 0.5f;
    private const float EchoWaitTimeoutSec = 1.5f;
    private const float HelloIntervalSec = 0.5f;
    private const float SnapIntervalSec = 0.5f;
    private const int DrainCap = 64;

    private readonly LanSession _session = new LanSession();
    private readonly IPEndPoint[] _clients = new IPEndPoint[LanRoom.SlotCap];
    private readonly float[] _clientLastRx = new float[LanRoom.SlotCap];
    private readonly byte[] _pkt16 = new byte[LanProtocol.PacketSize];
    private readonly byte[] _pktPose = new byte[LanProtocol.PacketSize + LanPose.PayloadSize];
    private readonly byte[] _pktSnap = new byte[LanProtocol.PacketSize + LanRoom.SnapPayloadSize];
    private readonly RxPkt[] _drainBuf = new RxPkt[DrainCap];
    private UdpClient _udp;
    private IPEndPoint _hostEp;
    private IPEndPoint _clientTarget;
    private string _clientHost = "";
    private int _clientPort;
    private readonly ConcurrentQueue<RxPkt> _inbox = new ConcurrentQueue<RxPkt>();
    private Thread _rxThread;
    private Thread _kaThread;
    private volatile bool _rxRun;
    private int _rxLogLeft;
    private bool _loggedFirstHello;
    private float _nextHelloOrHb;
    private float _connectDeadline;
    private ushort _seq;
    private ushort _pendingProbeSeq;
    private bool _awaitingEcho;
    private float _echoDeadline;
    private float _nextProbe;
    private float _nextSnap;
    private float _nextReadySend;

    public LanSession Session => _session;

    public int ConnectSecondsLeft()
    {
        if (_session.State != LanState.Connecting)
            return -1;
        float d = _connectDeadline - UnityEngine.Time.unscaledTime;
        if (d < 0f)
            d = 0f;
        return (int)Math.Ceiling(d);
    }

    public void StartHost(int port, int maxPlayers)
    {
        DisconnectSocketsOnly();
        try
        {
            _udp = OpenUdp4(port);
            StartRxThread();
            _session.IsHost = true;
            _session.State = LanState.Listen;
            _session.PeerEndPoint = "";
            _session.LastRttMs = -1;
            _session.FailReason = "";
            _session.MaxPlayers = LanRoom.ClampMax(maxPlayers);
            _session.LocalSlot = 0;
            ClearClients();
            _awaitingEcho = false;
            ResetRoomKeepMax();
            _session.PlayerCount = 1;
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] Host listen :" + port
                + " max=" + _session.MaxPlayers
                + " local=" + FormatEp(_udp.Client.LocalEndPoint as IPEndPoint));
            SendSelfProbe(port);
        }
        catch (SocketException ex)
        {
            _session.State = LanState.Fail;
            _session.FailReason = "port in use / bind failed: " + ex.SocketErrorCode;
            Plugin.LogSrc.LogWarning("[SatmLanIp] StartHost failed: " + _session.FailReason);
            DisposeUdp();
        }
        catch (Exception ex)
        {
            _session.State = LanState.Fail;
            _session.FailReason = ex.GetType().Name + ": " + ex.Message;
            Plugin.LogSrc.LogWarning("[SatmLanIp] StartHost failed: " + _session.FailReason);
            DisposeUdp();
        }
    }

    public void StartHost(int port)
    {
        StartHost(port, _session.MaxPlayers > 0 ? _session.MaxPlayers : 2);
    }

    public void StartClient(string ip, int port, int timeoutSec)
    {
        DisconnectSocketsOnly();
        _session.IsHost = false;

        if (!LanHostParse.TryParseHostPort(ip, port, out string host, out int usePort, out string parseErr))
        {
            _session.State = LanState.Fail;
            _session.FailReason = parseErr;
            _session.PeerEndPoint = "";
            _session.LastRttMs = -1;
            Plugin.LogSrc.LogWarning("[SatmLanIp] StartClient aborted: " + parseErr + " raw='" + (ip ?? "") + "'");
            return;
        }

        if (!IPAddress.TryParse(host, out IPAddress addr))
        {
            _session.State = LanState.Fail;
            _session.FailReason = "invalid JoinAddress";
            _session.PeerEndPoint = "";
            _session.LastRttMs = -1;
            Plugin.LogSrc.LogWarning("[SatmLanIp] StartClient aborted: invalid IP '" + host + "'");
            return;
        }

        try
        {
            _udp = OpenUdp4(0);
            StartRxThread();
            _clientHost = host;
            _clientPort = usePort;
            // Keep Fusion UniqueId / Connect port in lockstep with lobby UDP (Join IP:port).
            Plugin.SetLanPort(LanFusionStart.SessionPortAfterJoinParse(usePort));
            _clientTarget = new IPEndPoint(addr, usePort);
            _hostEp = _clientTarget;
            _session.IsHost = false;
            _session.State = LanState.Connecting;
            _session.PeerEndPoint = host + ":" + usePort.ToString();
            _session.LastRttMs = -1;
            _session.FailReason = "";
            _session.LocalSlot = 0;
            _awaitingEcho = false;
            _loggedFirstHello = false;
            _connectDeadline = UnityEngine.Time.unscaledTime + Math.Max(1, timeoutSec);
            _nextHelloOrHb = 0f;
            ClearClients();
            ResetRoomKeepMax();
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] Client connecting " + host + ":" + usePort.ToString()
                + " sessionPort=" + Plugin.JoinPort);
        }
        catch (Exception ex)
        {
            _session.State = LanState.Fail;
            _session.FailReason = ex.GetType().Name + ": " + ex.Message;
            Plugin.LogSrc.LogWarning("[SatmLanIp] StartClient failed: " + _session.FailReason);
            DisposeUdp();
        }
    }

    public void Disconnect()
    {
        if (_udp != null &&
            (_session.State == LanState.Connected || _session.State == LanState.Listen ||
             _session.State == LanState.Connecting))
        {
            try
            {
                byte[] bye = LanProtocol.Encode(LanPacketType.Goodbye, NextSeq(), NowMs());
                if (_session.IsHost)
                    BroadcastRaw(bye);
                else if (_hostEp != null)
                    _udp.Send(bye, bye.Length, _hostEp);
            }
            catch
            {
            }
        }

        DisconnectSocketsOnly();
        _session.State = LanState.Idle;
        _session.PeerEndPoint = "";
        _session.LastRttMs = -1;
        _session.FailReason = "";
        _session.IsHost = false;
        _session.LocalSlot = 0;
        ResetRoomKeepMax();
        Plugin.LogSrc.LogInfo("[SatmLanIp] Disconnect -> Idle");
    }

    /// <summary>
    /// Host is leaving the Fusion match: tell peers on the lobby UDP now.
    /// Do not DisposeUdp here — stock LeaveGame still owns Fusion teardown.
    /// </summary>
    public void NotifyMatchLeaving()
    {
        if (_udp == null)
            return;
        if (_session.State != LanState.Connected && _session.State != LanState.Listen)
            return;
        try
        {
            LanProtocol.WriteHeader(_pkt16, 0, LanPacketType.Goodbye, NextSeq(), NowMs());
            if (_session.IsHost)
                BroadcastRaw(_pkt16, LanProtocol.PacketSize);
            else if (_hostEp != null)
                _udp.Send(_pkt16, LanProtocol.PacketSize, _hostEp);
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_leave goodbye host=" + _session.IsHost);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] match_leave goodbye " + ex.GetType().Name);
        }
    }

    public void Poll()
    {
        if (_udp == null)
            return;

        float now = UnityEngine.Time.unscaledTime;

        if (_awaitingEcho && now >= _echoDeadline)
            _awaitingEcho = false;

        DrainRecv();

        if (_udp == null)
            return;

        if (_session.State == LanState.Connecting && now >= _connectDeadline)
        {
            _session.State = LanState.Fail;
            _session.FailReason = "connect timeout";
            Plugin.LogSrc.LogWarning("[SatmLanIp] Client connect timeout");
            DisposeUdp();
            return;
        }

        if (_session.State == LanState.Connecting)
        {
            if (now >= _nextHelloOrHb)
            {
                _nextHelloOrHb = now + HelloIntervalSec;
                SendTo(_clientHost, _clientPort, LanPacketType.Hello, NextSeq(), NowMs());
                if (!_loggedFirstHello)
                {
                    _loggedFirstHello = true;
                    Plugin.LogSrc.LogInfo(
                        "[SatmLanIp] Client Hello -> " + _clientHost + ":" + _clientPort.ToString());
                }
            }
            return;
        }

        if (_session.State == LanState.Connected && _hostEp != null && !_session.IsHost)
        {
            if (!_awaitingEcho && now >= _nextProbe)
            {
                _nextProbe = now + ProbeIntervalSec;
                _pendingProbeSeq = NextSeq();
                _awaitingEcho = true;
                _echoDeadline = now + EchoWaitTimeoutSec;
                // High byte = LocalSlot so host can rebind same-IP multi-instance peers.
                ushort wire = (ushort)((_session.LocalSlot << 8) | (_pendingProbeSeq & 0xFF));
                SendTo(_hostEp, LanPacketType.Heartbeat, wire, NowMs());
            }
        }

        if (_session.State == LanState.Connected && _hostEp != null && !_session.IsHost && now >= _nextReadySend)
        {
            _nextReadySend = now + SnapIntervalSec;
            SendTo(_hostEp, LanPacketType.Ready,
                LanRoom.PackReady(_session.LocalReady, _session.LocalSlot), NowMs());
        }

        if (_session.IsHost && _session.InRoom && ClientCount() > 0 && now >= _nextSnap)
        {
            _nextSnap = now + SnapIntervalSec;
            SendSnap();
        }

        if (_session.IsHost && _session.InRoom)
            EvictIdleClients(now);
    }

    private void HandlePacket(byte[] data, IPEndPoint remote)
    {
        if (!LanProtocol.TryParse(data, data.Length, out LanPacketType type, out ushort seq, out long unixMs))
        {
            Plugin.LogSrc.LogWarning(
                "[SatmLanIp] drop pkt len=" + data.Length + " from=" + FormatEp(remote));
            return;
        }
        if (_rxLogLeft > 0)
        {
            _rxLogLeft--;
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] rx type=" + type + " from=" + FormatEp(remote) + " len=" + data.Length);
        }

        switch (type)
        {
            case LanPacketType.Hello:
                HandleHello(remote, seq, unixMs);
                break;

            case LanPacketType.HelloAck:
                if (_session.State == LanState.Connecting && !_session.IsHost)
                {
                    _hostEp = remote;
                    _session.PeerEndPoint = remote.ToString();
                    _session.State = LanState.Connected;
                    _session.LocalSlot = seq > 0 && seq < LanRoom.SlotCap ? seq : 1;
                    _nextProbe = UnityEngine.Time.unscaledTime + ProbeIntervalSec;
                    // Do not invent room size from LocalSlot — wait for RoomSnap from host.
                    _session.PlayerCount = 1;
                    _session.OccupiedMask = 1 << _session.LocalSlot;
                    if (_session.LocalSlot == 0)
                        _session.OccupiedMask = 1;
                    _session.LocalReady = false;
                    _session.ReadyMask = 0;
                    _session.MatchActive = false;
                    Plugin.LogSrc.LogInfo(
                        "[SatmLanIp] Client CONNECTED peer=" + _session.PeerEndPoint
                        + " slot=" + _session.LocalSlot);
                    NoPhotonProbe.OnConnected();
                }
                break;

            case LanPacketType.RoomFull:
                if (_session.State == LanState.Connecting && !_session.IsHost)
                {
                    _session.State = LanState.Fail;
                    _session.FailReason = "room full";
                    Plugin.LogSrc.LogWarning("[SatmLanIp] Client room full");
                    DisposeUdp();
                }
                break;

            case LanPacketType.MatchBusy:
                if (_session.State == LanState.Connecting && !_session.IsHost)
                {
                    _session.State = LanState.Fail;
                    _session.FailReason = "match already started";
                    Plugin.LogSrc.LogWarning("[SatmLanIp] Client match already started");
                    DisposeUdp();
                }
                break;

            case LanPacketType.Heartbeat:
                HandleHeartbeat(remote, seq, unixMs);
                break;

            case LanPacketType.Ready:
                if (!_session.IsHost || !_session.InRoom)
                    break;
                {
                    int readySlot = LanRoom.UnpackReadySlot(seq);
                    int slot = readySlot >= 1 && readySlot < LanRoom.SlotCap
                        ? readySlot
                        : FindClientSlot(remote);
                    if (slot < 0)
                        break;
                    BindClientSlot(slot, remote, UnityEngine.Time.unscaledTime);
                    bool ready = LanRoom.UnpackReady(seq);
                    int next = LanRoom.SetSlotReady(_session.ReadyMask, slot, ready);
                    if (next == _session.ReadyMask)
                        break;
                    _session.ReadyMask = next;
                    Plugin.LogSrc.LogInfo("[SatmLanIp] Host saw slot " + slot + " Ready=" + ready);
                    SendSnap();
                }
                break;

            case LanPacketType.RoomSnap:
                if (_session.IsHost || _session.State != LanState.Connected)
                    break;
                if (!EndPointEquals(_hostEp, remote))
                    break;
                if (!LanRoom.TryReadSnap(data, data.Length, LanProtocol.PacketSize,
                    out int max, out int pc, out int mask, out int occ))
                    break;
                _session.MaxPlayers = max;
                _session.PlayerCount = pc;
                _session.ReadyMask = mask;
                _session.OccupiedMask = occ;
                break;

            case LanPacketType.StartMatch:
                if (_session.State != LanState.Connected)
                    break;
                if (_session.IsHost)
                    break;
                if (!EndPointEquals(_hostEp, remote))
                    break;
                LanMatch.TryBegin("peer");
                break;

            case LanPacketType.Pose:
                HandlePose(data, remote);
                break;

            case LanPacketType.Goodbye:
                HandleGoodbye(remote);
                break;
        }
    }

    private void HandleHello(IPEndPoint remote, ushort seq, long unixMs)
    {
        if (!_session.IsHost || !_session.InRoom)
        {
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] Host ignore Hello from " + FormatEp(remote)
                + " isHost=" + _session.IsHost
                + " state=" + _session.State);
            return;
        }

        if (_session.MatchActive)
        {
            SendTo(remote, LanPacketType.MatchBusy, 0, 0);
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] Host reject MatchBusy from " + FormatEp(remote));
            return;
        }

        int existing = FindClientSlot(remote);
        if (existing > 0)
        {
            BindClientSlot(existing, remote, UnityEngine.Time.unscaledTime);
            SendTo(remote, LanPacketType.HelloAck, (ushort)existing, unixMs);
            SendSnap();
            return;
        }

        int free = FirstFreeSlot();
        if (free < 0)
        {
            SendTo(remote, LanPacketType.RoomFull, 0, 0);
            Plugin.LogSrc.LogInfo("[SatmLanIp] Host reject full from " + FormatEp(remote));
            return;
        }

        BindClientSlot(free, remote, UnityEngine.Time.unscaledTime);
        RecountPlayers();
        _session.State = LanState.Connected;
        RefreshPeerSummary();
        SendTo(remote, LanPacketType.HelloAck, (ushort)free, unixMs);
        SendSnap();
        Plugin.LogSrc.LogInfo(
            "[SatmLanIp] Host accepted slot=" + free + " peer=" + FormatEp(remote)
            + " count=" + _session.PlayerCount + "/" + _session.MaxPlayers);
        NoPhotonProbe.OnConnected();
    }

    private void HandleHeartbeat(IPEndPoint remote, ushort seq, long unixMs)
    {
        if (_session.IsHost && _session.State == LanState.Listen)
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] self-probe rx from=" + FormatEp(remote));
            return;
        }
        if (_session.State != LanState.Connected)
            return;

        if (!_session.IsHost)
        {
            if (!EndPointEquals(_hostEp, remote))
                return;
            // Wire seq may carry LocalSlot in the high byte; match probe on low byte.
            if (_awaitingEcho && (seq & 0xFF) == (_pendingProbeSeq & 0xFF))
            {
                int rtt = (int)Math.Max(0, NowMs() - unixMs);
                _session.LastRttMs = rtt;
                _awaitingEcho = false;
            }
            return;
        }

        int hbSlot = LanRoom.UnpackReadySlot(seq);
        if (hbSlot < 1 || hbSlot >= LanRoom.SlotCap)
            hbSlot = FindClientSlot(remote);
        if (hbSlot < 0)
            return;
        BindClientSlot(hbSlot, remote, UnityEngine.Time.unscaledTime);
        SendTo(remote, LanPacketType.Heartbeat, seq, unixMs);
    }

    private void HandlePose(byte[] data, IPEndPoint remote)
    {
        if (_session.State != LanState.Connected)
            return;
        if (!LanProtocol.TryParse(data, data.Length, out _, out ushort seq, out _))
            return;
        if (!LanPose.TryRead(data, data.Length, LanProtocol.PacketSize,
            out float px, out float py, out float pz, out float pyaw))
            return;

        if (_session.IsHost)
        {
            int poseSlot = FindClientSlot(remote);
            if (poseSlot < 0)
                return;
            BindClientSlot(poseSlot, remote, UnityEngine.Time.unscaledTime);
            ApplyPose(poseSlot, px, py, pz, pyaw);
            RelayPoseExcept(data, poseSlot, remote);
            return;
        }

        if (!EndPointEquals(_hostEp, remote))
            return;
        int fromSlot = seq < LanRoom.SlotCap ? seq : 0;
        ApplyPose(fromSlot, px, py, pz, pyaw);
    }

    private void HandleGoodbye(IPEndPoint remote)
    {
        if (_session.IsHost)
        {
            int slot = FindClientSlot(remote);
            if (slot < 0)
                return;
            DropHostClientSlot(slot, "goodbye");
            return;
        }

        if (_session.State == LanState.Connected || _session.State == LanState.Connecting)
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] host Goodbye -> Drop");
            bool inMatch = _session.MatchActive;
            if (inMatch)
                LanMatch.RequestStockLeave();
            // Clear MatchActive before Drop so LanCloneUi can ReturnClientToCreate / Escape.
            _session.MarkHostDrop();
            DisposeUdp();
        }
    }

    private void EvictIdleClients(float now)
    {
        // Mid-match: Fusion owns peers; clearing UDP slots desyncs mappings.
        if (!LanRoom.AllowIdleEviction(_session.MatchActive))
            return;
        for (int i = 1; i < LanRoom.SlotCap; i++)
        {
            if (_clients[i] == null)
                continue;
            if (!LanRoom.ShouldEvictIdleClient(_clientLastRx[i], now, LanRoom.ClientIdleTimeoutSec))
                continue;
            DropHostClientSlot(i, "idle-timeout");
        }
    }

    private void DropHostClientSlot(int slot, string why)
    {
        if (slot < 0 || slot >= LanRoom.SlotCap || _clients[slot] == null)
            return;
        IPEndPoint peer = _clients[slot];
        try
        {
            LanProtocol.WriteHeader(_pkt16, 0, LanPacketType.Goodbye, NextSeq(), NowMs());
            _udp?.Send(_pkt16, LanProtocol.PacketSize, peer);
        }
        catch { /* best-effort notify */ }
        _clients[slot] = null;
        _clientLastRx[slot] = 0f;
        _session.ReadyMask = LanRoom.SetSlotReady(_session.ReadyMask, slot, false);
        RecountPlayers();
        RefreshPeerSummary();
        Plugin.LogSrc.LogInfo(
            "[SatmLanIp] Host slot " + slot + " left via=" + why + " count=" + _session.PlayerCount);
        if (_session.MatchActive)
            return;
        if (ClientCount() == 0)
        {
            _session.State = LanState.Listen;
            _awaitingEcho = false;
        }
        else
            SendSnap();
    }

    private void BindClientSlot(int slot, IPEndPoint remote, float now)
    {
        if (slot < 0 || slot >= LanRoom.SlotCap || remote == null)
            return;
        _clients[slot] = new IPEndPoint(remote.Address, remote.Port);
        TouchClientRx(slot, now);
    }

    private void TouchClientRx(int slot, float now)
    {
        if (slot < 0 || slot >= LanRoom.SlotCap)
            return;
        _clientLastRx[slot] = now;
    }

    public void ToggleLocalReady()
    {
        if (_udp == null || !_session.InRoom)
            return;
        if (_session.IsHost && _session.State == LanState.Listen)
        {
            // host lobby with 0 clients still allowed
        }
        else if (_session.State != LanState.Connected && !(_session.IsHost && _session.State == LanState.Listen))
            return;

        _session.LocalReady = !_session.LocalReady;
        if (_session.IsHost)
        {
            _session.ReadyMask = LanRoom.SetSlotReady(_session.ReadyMask, 0, _session.LocalReady);
            SendSnap();
            Plugin.LogSrc.LogInfo("[SatmLanIp] Host Ready=" + _session.LocalReady);
        }
        else if (_hostEp != null)
        {
            SendTo(_hostEp, LanPacketType.Ready,
                LanRoom.PackReady(_session.LocalReady, _session.LocalSlot), NowMs());
            Plugin.LogSrc.LogInfo("[SatmLanIp] Client Ready=" + _session.LocalReady);
        }
    }

    public void SendStartMatch()
    {
        if (!_session.IsHost || _udp == null || !_session.AllReady)
            return;
        Broadcast(LanPacketType.StartMatch, NextSeq(), 0);
        Plugin.LogSrc.LogInfo("[SatmLanIp] StartMatch broadcast clients=" + ClientCount());
    }

    public void SendPose(float x, float y, float z, float yaw)
    {
        if (_session.State != LanState.Connected || _udp == null)
            return;
        try
        {
            int len = LanProtocol.WritePosePacket(_pktPose, (ushort)_session.LocalSlot, x, y, z, yaw);
            if (_session.IsHost)
                BroadcastRaw(_pktPose, len);
            else if (_hostEp != null)
                _udp.Send(_pktPose, len, _hostEp);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] pose send: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void ApplyPose(int slot, float px, float py, float pz, float pyaw)
    {
        bool first = !_session.HasRemotePose;
        _session.SetPeerPose(slot, px, py, pz, pyaw);
        if (first)
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_pose=ok slot=" + slot);
    }

    private void RelayPoseExcept(byte[] pkt, int slot, IPEndPoint except)
    {
        pkt[6] = (byte)(slot & 0xFF);
        pkt[7] = (byte)((slot >> 8) & 0xFF);
        for (int i = 1; i < LanRoom.SlotCap; i++)
        {
            IPEndPoint ep = _clients[i];
            if (ep == null || EndPointEquals(ep, except))
                continue;
            try { _udp.Send(pkt, pkt.Length, ep); }
            catch { }
        }
    }

    private void ResetRoomKeepMax()
    {
        int max = LanRoom.ClampMax(_session.MaxPlayers);
        _session.PlayerCount = 1;
        _session.ReadyMask = 0;
        _session.OccupiedMask = 1;
        _session.LocalReady = false;
        _session.MatchActive = false;
        _session.ClearPeerPoses();
        _session.MaxPlayers = max;
        _nextSnap = 0f;
        _nextReadySend = 0f;
    }

    private void SendSnap()
    {
        if (!_session.IsHost || _udp == null)
            return;
        try
        {
            int len = LanProtocol.WriteRoomSnapPacket(
                _pktSnap,
                _session.MaxPlayers, _session.PlayerCount, _session.ReadyMask, _session.OccupiedMask);
            BroadcastRaw(_pktSnap, len);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] snap send: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void Broadcast(LanPacketType type, ushort seq, long unixMs)
    {
        LanProtocol.WriteHeader(_pkt16, 0, type, seq, unixMs);
        BroadcastRaw(_pkt16, LanProtocol.PacketSize);
    }

    private void BroadcastRaw(byte[] pkt)
    {
        BroadcastRaw(pkt, pkt != null ? pkt.Length : 0);
    }

    private void BroadcastRaw(byte[] pkt, int len)
    {
        if (_udp == null || pkt == null || len <= 0)
            return;
        for (int i = 1; i < LanRoom.SlotCap; i++)
        {
            IPEndPoint ep = _clients[i];
            if (ep == null)
                continue;
            try { _udp.Send(pkt, len, ep); }
            catch (Exception ex)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] send failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    private void SendTo(IPEndPoint ep, LanPacketType type, ushort seq, long unixMs)
    {
        if (ep == null)
            return;
        string host = ep.Address != null ? ep.Address.ToString() : "";
        SendTo(host, ep.Port, type, seq, unixMs);
    }

    private void SendTo(string host, int port, LanPacketType type, ushort seq, long unixMs)
    {
        if (_udp == null || host == null || host.Length == 0 || port < 1)
            return;
        try
        {
            LanProtocol.WriteHeader(_pkt16, 0, type, seq, unixMs);
            int n = _udp.Send(_pkt16, LanProtocol.PacketSize, host, port);
            if (!_loggedFirstHello && type == LanPacketType.Hello)
                Plugin.LogSrc.LogInfo("[SatmLanIp] send Hello n=" + n + " -> " + host + ":" + port.ToString());
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] send failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void SendSelfProbe(int port)
    {
        if (_udp == null)
            return;
        try
        {
            LanProtocol.WriteHeader(_pkt16, 0, LanPacketType.Heartbeat, 0, 0);
            int n = _udp.Send(_pkt16, LanProtocol.PacketSize, "127.0.0.1", port);
            Plugin.LogSrc.LogInfo("[SatmLanIp] self-probe send n=" + n + " -> 127.0.0.1:" + port.ToString());
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] self-probe send " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private int FindClientSlot(IPEndPoint remote)
    {
        for (int i = 1; i < LanRoom.SlotCap; i++)
        {
            if (EndPointEquals(_clients[i], remote))
                return i;
        }
        return -1;
    }

    private int FirstFreeSlot()
    {
        int max = _session.MaxPlayers;
        if (max < 2)
            max = 2;
        if (max > LanRoom.SlotCap)
            max = LanRoom.SlotCap;
        for (int i = 1; i < max; i++)
        {
            if (_clients[i] == null)
                return i;
        }
        return -1;
    }

    private int ClientCount()
    {
        int n = 0;
        for (int i = 1; i < LanRoom.SlotCap; i++)
        {
            if (_clients[i] != null)
                n++;
        }
        return n;
    }

    private void RecountPlayers()
    {
        int max = _session.MaxPlayers;
        if (max < 2)
            max = 2;
        if (max > LanRoom.SlotCap)
            max = LanRoom.SlotCap;
        int occ = 1;
        int n = 0;
        for (int i = 1; i < max; i++)
        {
            if (_clients[i] == null)
                continue;
            occ |= 1 << i;
            n++;
        }
        for (int i = max; i < LanRoom.SlotCap; i++)
            _clients[i] = null;
        _session.OccupiedMask = occ;
        _session.PlayerCount = 1 + n;
    }

    private void RefreshPeerSummary()
    {
        if (ClientCount() == 0)
        {
            _session.PeerEndPoint = "";
            return;
        }
        for (int i = 1; i < LanRoom.SlotCap; i++)
        {
            if (_clients[i] != null)
            {
                _session.PeerEndPoint = _clients[i].ToString();
                return;
            }
        }
    }

    private void ClearClients()
    {
        for (int i = 0; i < _clients.Length; i++)
        {
            _clients[i] = null;
            _clientLastRx[i] = 0f;
        }
        _hostEp = null;
        _clientTarget = null;
    }

    private ushort NextSeq()
    {
        _seq++;
        return _seq;
    }

    private static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static string FormatEp(IPEndPoint ep)
    {
        if (ep == null)
            return "null";
        IPAddress a = ep.Address;
        string ip = a != null ? a.ToString() : "";
        return ip + ":" + ep.Port.ToString();
    }

    private static bool EndPointEquals(IPEndPoint a, IPEndPoint b)
    {
        if (a == null || b == null)
            return false;
        return a.Port == b.Port && a.Address.Equals(b.Address);
    }

    private sealed class RxPkt
    {
        public byte[] Data;
        public IPEndPoint Remote;
    }

    private static UdpClient OpenUdp4(int port)
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.ReceiveTimeout = 200;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        return udp;
    }

    private void StartRxThread()
    {
        _rxLogLeft = 8;
        while (_inbox.TryDequeue(out _))
        {
        }
        _rxRun = true;
        _rxThread = new Thread(RxLoop);
        _rxThread.IsBackground = true;
        _rxThread.Name = "SatmLanIpRx";
        _rxThread.Start();
        _kaThread = new Thread(KeepaliveLoop);
        _kaThread.IsBackground = true;
        _kaThread.Name = "SatmLanIpKa";
        _kaThread.Start();
        Plugin.LogSrc.LogInfo("[SatmLanIp] rx thread start");
    }

    private void RxLoop()
    {
        while (_rxRun)
        {
            UdpClient udp = _udp;
            if (udp == null)
                break;
            try
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udp.Receive(ref remote);
                if (data == null || data.Length == 0)
                    continue;
                IPEndPoint copy = remote == null
                    ? null
                    : new IPEndPoint(remote.Address, remote.Port);
                _inbox.Enqueue(new RxPkt { Data = data, Remote = copy });
            }
            catch (SocketException)
            {
                if (!_rxRun)
                    break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Plugin.LogSrc.LogWarning("[SatmLanIp] rx thread " + ex.GetType().Name + ": " + ex.Message);
                if (!_rxRun)
                    break;
            }
        }
    }

    /// <summary>
    /// Lobby Ready keepalive independent of Unity Update so alt-tab / focus loss
    /// on any joiner does not look like a dead peer to the host.
    /// Uses Encode() (own buffer) — never shares _pkt16 with the main thread.
    /// </summary>
    private void KeepaliveLoop()
    {
        while (_rxRun)
        {
            try
            {
                ClientLobbyKeepaliveTick();
            }
            catch
            {
                /* best-effort */
            }
            Thread.Sleep(500);
        }
    }

    private void ClientLobbyKeepaliveTick()
    {
        if (!LanRoom.ShouldClientLobbyKeepalive(_session.IsHost, _session.State, _session.LocalSlot))
            return;
        IPEndPoint host = _hostEp;
        UdpClient udp = _udp;
        if (host == null || udp == null)
            return;
        ushort wire = LanRoom.PackReady(_session.LocalReady, _session.LocalSlot);
        byte[] pkt = LanProtocol.Encode(LanPacketType.Ready, wire, NowMs());
        udp.Send(pkt, pkt.Length, host);
    }

    private void DrainRecv()
    {
        int n = 0;
        while (n < DrainCap && _inbox.TryDequeue(out RxPkt pkt))
            _drainBuf[n++] = pkt;

        for (int pass = 0; pass <= 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                RxPkt pkt = _drainBuf[i];
                if (pkt == null || pkt.Data == null)
                    continue;
                int pri = 2;
                if (pkt.Data.Length >= LanProtocol.PacketSize)
                {
                    byte t = pkt.Data[5];
                    if (t >= (byte)LanPacketType.Hello && t <= (byte)LanPacketType.MatchBusy)
                        pri = LanProtocol.DrainPriority((LanPacketType)t);
                }
                if (pri != pass)
                    continue;
                HandlePacket(pkt.Data, pkt.Remote);
                _drainBuf[i] = null;
            }
        }

        for (int i = 0; i < n; i++)
            _drainBuf[i] = null;
    }

    private void DisconnectSocketsOnly()
    {
        DisposeUdp();
        ClearClients();
        _awaitingEcho = false;
    }

    private void DisposeUdp()
    {
        _rxRun = false;
        UdpClient udp = _udp;
        _udp = null;
        try { udp?.Close(); }
        catch { }
        Thread rx = _rxThread;
        _rxThread = null;
        if (rx != null && rx.IsAlive)
            rx.Join(500);
        Thread ka = _kaThread;
        _kaThread = null;
        if (ka != null && ka.IsAlive)
            ka.Join(500);
        while (_inbox.TryDequeue(out _))
        {
        }
    }
}
