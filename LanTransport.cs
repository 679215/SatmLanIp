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

    private readonly LanSession _session = new LanSession();
    private readonly IPEndPoint[] _clients = new IPEndPoint[LanRoom.SlotCap];
    private UdpClient _udp;
    private IPEndPoint _hostEp;
    private IPEndPoint _clientTarget;
    private string _clientHost = "";
    private int _clientPort;
    private readonly ConcurrentQueue<RxPkt> _inbox = new ConcurrentQueue<RxPkt>();
    private Thread _rxThread;
    private volatile bool _rxRun;
    private int _rxLogLeft;
    private bool _loggedFirstHello;
    private float _nextHelloOrHb;
    private float _connectDeadline;
    private ushort _seq;
    private ushort _pendingProbeSeq;
    private ushort _lastFinishedProbeSeq;
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
                "[SatmLanIp] Client connecting " + host + ":" + usePort.ToString());
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
            byte[] bye = LanProtocol.Encode(LanPacketType.Goodbye, NextSeq(), NowMs());
            if (_session.IsHost)
                BroadcastRaw(bye);
            else if (_hostEp != null)
                _udp.Send(bye, bye.Length, _hostEp);
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_leave goodbye host=" + _session.IsHost);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] match_leave goodbye " + ex.GetType().Name);
        }
    }

    public void ReleaseUdpKeepSession(string why)
    {
        Plugin.LogSrc.LogWarning(
            "[SatmLanIp] udp_release ignored (Fusion uses LAN+1) via=" + why);
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
                SendTo(_hostEp, LanPacketType.Heartbeat, _pendingProbeSeq, NowMs());
            }
        }

        if (_session.State == LanState.Connected && _hostEp != null && !_session.IsHost && now >= _nextReadySend)
        {
            _nextReadySend = now + SnapIntervalSec;
            SendTo(_hostEp, LanPacketType.Ready, LanRoom.PackReady(_session.LocalReady), NowMs());
        }

        if (_session.IsHost && _session.InRoom && ClientCount() > 0 && now >= _nextSnap)
        {
            _nextSnap = now + SnapIntervalSec;
            SendSnap();
        }
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
                    _session.PlayerCount = Math.Max(2, _session.LocalSlot + 1);
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

            case LanPacketType.Heartbeat:
                HandleHeartbeat(remote, seq, unixMs);
                break;

            case LanPacketType.Ready:
                if (!_session.IsHost || !_session.InRoom)
                    break;
                int slot = FindClientSlot(remote);
                if (slot < 0)
                    break;
                bool ready = LanRoom.UnpackReady(seq);
                int next = LanRoom.SetSlotReady(_session.ReadyMask, slot, ready);
                if (next == _session.ReadyMask)
                    break;
                _session.ReadyMask = next;
                Plugin.LogSrc.LogInfo("[SatmLanIp] Host saw slot " + slot + " Ready=" + ready);
                SendSnap();
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
        if (!_session.IsHost || !_session.InRoom || _session.MatchActive)
        {
            Plugin.LogSrc.LogInfo(
                "[SatmLanIp] Host ignore Hello from " + FormatEp(remote)
                + " isHost=" + _session.IsHost
                + " state=" + _session.State
                + " match=" + _session.MatchActive);
            return;
        }

        int existing = FindClientSlot(remote);
        if (existing > 0)
        {
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

        _clients[free] = remote;
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
            if (_awaitingEcho && seq == _pendingProbeSeq)
            {
                int rtt = (int)Math.Max(0, NowMs() - unixMs);
                _session.LastRttMs = rtt;
                _awaitingEcho = false;
                _lastFinishedProbeSeq = seq;
            }
            return;
        }

        if (FindClientSlot(remote) < 0)
            return;
        SendTo(remote, LanPacketType.Heartbeat, seq, unixMs);
    }

    private void HandlePose(byte[] data, IPEndPoint remote)
    {
        if (_session.State != LanState.Connected)
            return;
        if (!LanPose.TryRead(data, data.Length, LanProtocol.PacketSize,
            out float px, out float py, out float pz, out float pyaw))
            return;

        if (_session.IsHost)
        {
            if (FindClientSlot(remote) < 0)
                return;
            ApplyPose(px, py, pz, pyaw);
            RelayPoseExcept(data, remote);
            return;
        }

        if (!EndPointEquals(_hostEp, remote))
            return;
        ApplyPose(px, py, pz, pyaw);
    }

    private void HandleGoodbye(IPEndPoint remote)
    {
        if (_session.IsHost)
        {
            int slot = FindClientSlot(remote);
            if (slot < 0)
                return;
            _clients[slot] = null;
            _session.ReadyMask = LanRoom.SetSlotReady(_session.ReadyMask, slot, false);
            RecountPlayers();
            RefreshPeerSummary();
            Plugin.LogSrc.LogInfo("[SatmLanIp] Host slot " + slot + " left count=" + _session.PlayerCount);
            if (_session.MatchActive)
                return;
            if (ClientCount() == 0)
            {
                _session.State = LanState.Listen;
                _awaitingEcho = false;
            }
            else
                SendSnap();
            return;
        }

        if (_session.State == LanState.Connected || _session.State == LanState.Connecting)
        {
            Plugin.LogSrc.LogInfo("[SatmLanIp] host Goodbye -> Drop");
            bool inMatch = _session.MatchActive;
            if (inMatch)
                LanMatch.RequestStockLeave();
            _session.State = LanState.Drop;
            DisposeUdp();
        }
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
            SendTo(_hostEp, LanPacketType.Ready, LanRoom.PackReady(_session.LocalReady), NowMs());
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
            byte[] pkt = LanProtocol.EncodePose(NextSeq(), x, y, z, yaw);
            if (_session.IsHost)
                BroadcastRaw(pkt);
            else if (_hostEp != null)
                _udp.Send(pkt, pkt.Length, _hostEp);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] pose send: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void ApplyPose(float px, float py, float pz, float pyaw)
    {
        _session.RemoteX = px;
        _session.RemoteY = py;
        _session.RemoteZ = pz;
        _session.RemoteYaw = pyaw;
        if (!_session.HasRemotePose)
        {
            _session.HasRemotePose = true;
            Plugin.LogSrc.LogInfo("[SatmLanIp] match_pose=ok");
        }
    }

    private void RelayPoseExcept(byte[] pkt, IPEndPoint except)
    {
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
        _session.HasRemotePose = false;
        _session.RemoteX = 0f;
        _session.RemoteY = 0f;
        _session.RemoteZ = 0f;
        _session.RemoteYaw = 0f;
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
            byte[] pkt = LanProtocol.EncodeRoomSnap(
                _session.MaxPlayers, _session.PlayerCount, _session.ReadyMask, _session.OccupiedMask);
            BroadcastRaw(pkt);
        }
        catch (Exception ex)
        {
            Plugin.LogSrc.LogWarning("[SatmLanIp] snap send: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void Broadcast(LanPacketType type, ushort seq, long unixMs)
    {
        byte[] pkt = LanProtocol.Encode(type, seq, unixMs);
        BroadcastRaw(pkt);
    }

    private void BroadcastRaw(byte[] pkt)
    {
        if (_udp == null || pkt == null)
            return;
        for (int i = 1; i < LanRoom.SlotCap; i++)
        {
            IPEndPoint ep = _clients[i];
            if (ep == null)
                continue;
            try { _udp.Send(pkt, pkt.Length, ep); }
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
            byte[] pkt = LanProtocol.Encode(type, seq, unixMs);
            int n = _udp.Send(pkt, pkt.Length, host, port);
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
            byte[] pkt = LanProtocol.Encode(LanPacketType.Heartbeat, 0, 0);
            int n = _udp.Send(pkt, pkt.Length, "127.0.0.1", port);
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
            _clients[i] = null;
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

    private void DrainRecv()
    {
        for (int n = 0; n < 32; n++)
        {
            RxPkt pkt;
            if (!_inbox.TryDequeue(out pkt))
                return;
            if (pkt != null && pkt.Data != null)
                HandlePacket(pkt.Data, pkt.Remote);
        }
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
        Thread t = _rxThread;
        _rxThread = null;
        if (t != null && t.IsAlive)
            t.Join(500);
        while (_inbox.TryDequeue(out _))
        {
        }
    }
}
