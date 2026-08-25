using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>Receives application payloads. The reader is positioned after the session header and is valid only for the call.</summary>
    public delegate void SessionDataHandler(ulong clientId, Delivery delivery, NetworkReader reader);

    /// <summary>
    /// Connections, client ids, the handshake, the fixed tick and time sync — everything Vapor used
    /// <c>NetworkManager</c> for, over an <see cref="IVaporTransport"/>. A session is either a server
    /// or a client; a host is a server session plus a client session in the same process, joined by
    /// loopback, so there is exactly one code path for each role.
    /// </summary>
    /// <remarks>
    /// The session owns one byte of every packet: a message-type prefix that separates its control
    /// traffic (hello, welcome, reject, disconnect, ping, pong) from application data. Everything
    /// after that byte belongs to whoever subscribed to <see cref="Data"/> — in practice the replicator.
    /// <para>
    /// It is driven, not threaded: call <see cref="Update"/> once per frame (or per test step). That
    /// polls the transport, raises connection and data events, runs timeouts, and fires as many fixed
    /// <see cref="Tick"/>s as the injected clock says are due.
    /// </para>
    /// </remarks>
    public sealed class NetworkSession : ITransportEvents, IDisposable
    {
        public const ulong ServerClientId = 0;
        public const ulong InvalidClientId = ulong.MaxValue;

        private enum MessageType : byte { Hello = 1, Welcome = 2, Reject = 3, Disconnect = 4, Ping = 5, Pong = 6, Data = 0x10 }

        private enum ConnectionState : byte { Handshaking, Connected, Closing }

        private sealed class Connection
        {
            public ConnectionId Id;
            public ulong ClientId = InvalidClientId;
            public ConnectionState State;
            public double ConnectedAt;
            public double LastReceivedAt;
        }

        private sealed class PendingTransportClose
        {
            public ConnectionId Id;
            public byte[] ControlPacket;
            public bool Sent;
            public ulong SentOnUpdate;
            public double Deadline;
        }

        private enum ClientState : byte { Disconnected, Connecting, Handshaking, Connected }

        private readonly IVaporTransport _transport;
        private readonly INetworkClock _clock;
        private readonly SessionConfig _config;
        private readonly NetworkWriter _writer = new(512);
        private readonly NetworkReader _reader = new();
        private byte[] _receiveBuffer = new byte[2048];
        private byte[] _sendBuffer = new byte[2048];

        // Server
        private readonly Dictionary<int, Connection> _connections = new();
        private readonly Dictionary<ulong, Connection> _clients = new();
        private readonly List<ulong> _clientIds = new();
        private readonly List<Connection> _connectionScratch = new();
        private readonly List<PendingTransportClose> _pendingTransportCloses = new();
        private ulong _nextClientId = 1;
        private ulong _updateSerial;

        // Client
        private ClientState _clientState;
        private ConnectionId _serverConnection = ConnectionId.Invalid;
        private byte[] _connectionPayload = Array.Empty<byte>();
        private double _connectStartedAt;
        private double _lastReceivedFromServer;
        private double _lastPingSentAt;
        private double _rtt;
        private double _clockOffset;
        private double _serverTimeAtSync;
        private uint _serverTickAtSync;
        private bool _hasTimeSync;

        // Ticks
        private double _lastUpdateTime;
        private double _tickAccumulator;

        /// <summary>Reliable data that arrived before the welcome, held rather than thrown away.</summary>
        private readonly List<(Delivery delivery, byte[] payload)> _preWelcome = new();
        private readonly List<(Delivery delivery, byte[] payload)> _heldScratch = new();

        private const int MaxHeldBeforeWelcome = 256;
        private uint _tick;

        public NetworkSession(IVaporTransport transport, INetworkClock clock, SessionConfig config = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = (config ?? new SessionConfig()).Clone();
            if (_config.TickRate <= 0) throw new ArgumentOutOfRangeException(nameof(config), "TickRate must be positive.");
        }

        #region - State -

        public IVaporTransport Transport => _transport;
        public INetworkClock Clock => _clock;
        public SessionConfig Config => _config;

        private SessionRole _role;

        /// <summary>What this session is. Published process-wide through <see cref="NetworkRoles"/> while it runs.</summary>
        public SessionRole Role
        {
            get => _role;
            private set
            {
                if (_role == value) return;
                NetworkRoles.Retract(_role);
                _role = value;
                NetworkRoles.Publish(_role);
            }
        }

        public bool IsRunning => Role != SessionRole.None;

        /// <summary>This session runs the authority: a dedicated server, or a host.</summary>
        public bool IsServer => Role is SessionRole.Server or SessionRole.Host;

        /// <summary>This session carries a local player: a client, or a host.</summary>
        public bool IsClient => Role is SessionRole.Client or SessionRole.Host;

        /// <summary>Client: the handshake completed and the server assigned an id. Server: always true while running.</summary>
        public bool IsConnected => IsServer || _clientState == ClientState.Connected;

        /// <summary>
        /// Who this session is, for ownership. <see cref="ServerClientId"/> on a dedicated server, the
        /// id the server assigned on a client, and the local player's own id on a host — a host's
        /// objects are owned by its player, not by the server it also happens to be.
        /// </summary>
        public ulong LocalClientId { get; private set; } = InvalidClientId;

        /// <summary>
        /// Server side. The client id of the player sharing this process; invalid on a dedicated
        /// server. A host reserves one at <see cref="StartHost"/> and never gives it a connection: the
        /// replicator has nothing to send to someone who already holds the objects.
        /// </summary>
        public ulong LocalPlayerClientId { get; private set; } = InvalidClientId;

        public bool IsHost => Role == SessionRole.Host;

        /// <summary>Server: every approved client. Client: empty.</summary>
        public IReadOnlyList<ulong> ConnectedClientIds => _clientIds;

        public int ClientCount => _clientIds.Count;

        /// <summary>Whether a client is connected. A host's local player counts, and has no connection to ask.</summary>
        public bool IsClientConnected(ulong clientId) =>
            (IsHost && clientId == LocalPlayerClientId && _clientIds.Contains(clientId)) ||
            (_clients.TryGetValue(clientId, out var c) && c.State == ConnectionState.Connected);

        public double TickRate => _config.TickRate;
        public double TickInterval => 1.0 / _config.TickRate;

        /// <summary>Server: the authoritative tick. Client: this session's own tick counter, seeded from the server's at welcome.</summary>
        public uint LocalTick => _tick;

        /// <summary>Client: the server's tick right now, from the last time sync. Server: <see cref="LocalTick"/>.</summary>
        public uint EstimatedServerTick
        {
            get
            {
                if (IsServer) return _tick;
                if (!_hasTimeSync) return _tick;
                double elapsed = EstimatedServerTime - _serverTimeAtSync;
                return _serverTickAtSync + (uint)Math.Max(0, Math.Floor(elapsed * _config.TickRate));
            }
        }

        /// <summary>
        /// Client: the server's tick as a continuous value (whole ticks plus the fraction elapsed since
        /// the last one), for interpolation timelines. Server: the local tick plus its accumulator.
        /// </summary>
        public double EstimatedServerTickExact
        {
            get
            {
                if (IsServer) return _tick + _tickAccumulator * _config.TickRate;
                if (!_hasTimeSync) return _tick + _tickAccumulator * _config.TickRate;
                double elapsed = EstimatedServerTime - _serverTimeAtSync;
                return _serverTickAtSync + Math.Max(0, elapsed * _config.TickRate);
            }
        }

        /// <summary>Client: the server's clock right now. Server: the local clock.</summary>
        public double EstimatedServerTime => IsServer ? _clock.Now : _clock.Now + _clockOffset;

        public double RoundTripTime => _rtt;
        public bool HasTimeSync => IsServer || _hasTimeSync;

        /// <summary>Server-side approval hook; null approves everyone.</summary>
        public ConnectionApprovalHandler Approval { get; set; }

        #endregion

        #region - Events -

        /// <summary>Server: a client completed the handshake.</summary>
        public event Action<ulong> ClientConnected;

        /// <summary>Server: a connected client is gone. Not raised for connections that never finished the handshake.</summary>
        public event Action<ulong, SessionDisconnectReason> ClientDisconnected;

        /// <summary>Client: welcomed by the server.</summary>
        public event Action Connected;

        /// <summary>Client: the connection ended (including a rejected handshake).</summary>
        public event Action<SessionDisconnectReason> Disconnected;

        /// <summary>Fixed-rate tick: (tick number, tick interval).</summary>
        public event Action<uint, double> Tick;

        /// <summary>Application payloads. On the server the client id is the sender; on the client it is <see cref="ServerClientId"/>.</summary>
        public event SessionDataHandler Data;

        #endregion

        #region - Lifecycle -

        public bool StartServer(TransportEndpoint bind)
        {
            if (IsRunning) return false;
            if (!_transport.StartServer(bind)) return false;

            Role = SessionRole.Server;
            LocalClientId = ServerClientId;
            _lastUpdateTime = _clock.Now;
            _tickAccumulator = 0;
            _tick = 0;
            return true;
        }

        /// <summary>
        /// A server that is also its own client. One session, one world: the local player is given a
        /// client id and holds the server's objects directly, so nothing is serialised to it and it
        /// sees everything the server does.
        /// </summary>
        /// <remarks>
        /// Approval runs here, against <paramref name="connectionPayload"/>, exactly as it would for a
        /// remote join — a host that its own rules reject does not start. The player is not announced
        /// yet: <see cref="AdmitLocalPlayer"/> does that once whoever started the session has wired up
        /// its world and its listeners, so the join is heard the same way a remote one is.
        /// </remarks>
        public bool StartHost(TransportEndpoint bind, ReadOnlySpan<byte> connectionPayload = default)
        {
            if (IsRunning) return false;

            ulong clientId = _nextClientId;
            var approval = Approval?.Invoke(clientId, connectionPayload.ToArray()) ?? ConnectionApproval.Approve();
            if (!approval.Approved)
            {
                Debug.LogError($"The host was refused by its own approval: {approval.Reason}");
                return false;
            }

            if (!_transport.StartServer(bind)) return false;

            _nextClientId++;
            Role = SessionRole.Host;
            LocalPlayerClientId = clientId;
            LocalClientId = clientId;
            _lastUpdateTime = _clock.Now;
            _tickAccumulator = 0;
            _tick = 0;
            return true;
        }

        /// <summary>
        /// Host: puts the local player among the connected clients and raises <see cref="ClientConnected"/>
        /// for it, so a game mode spawns its controller through the one path it uses for everybody.
        /// Call once, after the world and the listeners are up.
        /// </summary>
        public void AdmitLocalPlayer()
        {
            if (!IsHost || _clientIds.Contains(LocalPlayerClientId)) return;

            _clientIds.Add(LocalPlayerClientId);
            ClientConnected?.Invoke(LocalPlayerClientId);
        }

        /// <summary>Host: the mirror of <see cref="AdmitLocalPlayer"/>. The local player never disconnects on its own, and something has to be the last chance to save it.</summary>
        public void ReleaseLocalPlayer()
        {
            if (!IsHost || !_clientIds.Remove(LocalPlayerClientId)) return;

            ClientDisconnected?.Invoke(LocalPlayerClientId, SessionDisconnectReason.ServerShutdown);
        }

        public bool StartClient(TransportEndpoint remote, ReadOnlySpan<byte> connectionPayload = default)
        {
            if (IsRunning) return false;
            if (!_transport.StartClient(remote)) return false;

            Role = SessionRole.Client;
            _connectionPayload = connectionPayload.ToArray();
            _clientState = ClientState.Connecting;
            _connectStartedAt = _clock.Now;
            _lastUpdateTime = _clock.Now;
            _tickAccumulator = 0;
            _tick = 0;
            _hasTimeSync = false;
            _rtt = 0;
            _clockOffset = 0;
            return true;
        }

        public void Shutdown()
        {
            if (!IsRunning) return;

            if (IsServer)
            {
                // A fresh list: Shutdown may be called from a handler while a scratch-list loop is running.
                foreach (var connection in new List<Connection>(_connections.Values))
                {
                    CloseConnection(connection, SessionDisconnectReason.ServerShutdown, notifyPeer: true);
                }
            }
            else if (_clientState != ClientState.Disconnected)
            {
                if (_serverConnection.IsValid)
                {
                    SendControl(_serverConnection, MessageType.Disconnect, w => w.WriteByte((byte)SessionDisconnectReason.Local));
                    _transport.Disconnect(_serverConnection);
                }

                EndClientConnection(SessionDisconnectReason.Local);
            }

            _transport.Shutdown();
            _pendingTransportCloses.Clear();
            Role = SessionRole.None;
            LocalClientId = InvalidClientId;
            LocalPlayerClientId = InvalidClientId;
            _serverConnection = ConnectionId.Invalid;
        }

        public void Dispose() => Shutdown();

        /// <summary>Server: ends a client's connection, telling it why.</summary>
        public void DisconnectClient(ulong clientId, SessionDisconnectReason reason = SessionDisconnectReason.Local)
        {
            if (!IsServer || !_clients.TryGetValue(clientId, out var connection)) return;
            CloseConnection(connection, reason, notifyPeer: true);
        }

        /// <summary>Client: leaves the server.</summary>
        public void Disconnect()
        {
            if (!IsClient) return;
            Shutdown();
        }

        #endregion

        #region - Update -

        /// <summary>Polls the transport, runs timeouts and pings, and fires due ticks. Call once per frame.</summary>
        public void Update()
        {
            if (!IsRunning) return;

            _updateSerial++;
            _transport.Poll(this);
            if (!IsRunning) return;   // the poll may have ended the session

            ProcessPendingTransportCloses();
            if (!IsRunning) return;

            double now = _clock.Now;
            if (IsServer)
            {
                RunServerTimeouts(now);
            }
            else
            {
                RunClientTimeouts(now);
                if (_clientState == ClientState.Connected && now - _lastPingSentAt >= _config.PingIntervalSeconds)
                {
                    SendPing(now);
                }
            }

            RunTicks(now);
        }

        private void RunTicks(double now)
        {
            double interval = TickInterval;
            _tickAccumulator += Math.Max(0, now - _lastUpdateTime);
            _lastUpdateTime = now;

            int fired = 0;
            while (_tickAccumulator >= interval && fired < _config.MaxTicksPerUpdate)
            {
                _tickAccumulator -= interval;
                fired++;
                _tick++;
                Tick?.Invoke(_tick, interval);
                if (!IsRunning) return;
            }

            if (fired >= _config.MaxTicksPerUpdate && _tickAccumulator >= interval)
            {
                // A long stall: drop the backlog rather than replaying it in one frame.
                _tickAccumulator = 0;
            }
        }

        private void RunServerTimeouts(double now)
        {
            _connectionScratch.Clear();
            _connectionScratch.AddRange(_connections.Values);
            foreach (var connection in _connectionScratch)
            {
                if (connection.State == ConnectionState.Handshaking && now - connection.ConnectedAt > _config.HandshakeTimeoutSeconds)
                {
                    CloseConnection(connection, SessionDisconnectReason.HandshakeTimeout, notifyPeer: true);
                }
                else if (connection.State == ConnectionState.Connected && now - connection.LastReceivedAt > _config.TimeoutSeconds)
                {
                    CloseConnection(connection, SessionDisconnectReason.Timeout, notifyPeer: true);
                }
            }
        }

        private void RunClientTimeouts(double now)
        {
            switch (_clientState)
            {
                case ClientState.Connecting:
                case ClientState.Handshaking:
                    if (now - _connectStartedAt > _config.HandshakeTimeoutSeconds)
                    {
                        FailClient(SessionDisconnectReason.HandshakeTimeout);
                    }
                    break;

                case ClientState.Connected:
                    if (now - _lastReceivedFromServer > _config.TimeoutSeconds)
                    {
                        FailClient(SessionDisconnectReason.Timeout);
                    }
                    break;
            }
        }

        private void FailClient(SessionDisconnectReason reason)
        {
            if (_serverConnection.IsValid)
            {
                _transport.Disconnect(_serverConnection);
            }

            EndClientConnection(reason);
            _transport.Shutdown();
            Role = SessionRole.None;
        }

        #endregion

        #region - Send -

        /// <summary>The largest application payload a single send accepts for the delivery.</summary>
        public int MaxPayload(Delivery delivery) => _transport.MaxPayload(delivery) - 1;

        /// <summary>Server → one client.</summary>
        public SendResult Send(ulong clientId, Delivery delivery, ReadOnlySpan<byte> payload)
        {
            if (!IsServer) return SendResult.NotStarted;
            if (!_clients.TryGetValue(clientId, out var connection) || connection.State != ConnectionState.Connected) return SendResult.Disconnected;
            return SendData(connection.Id, delivery, payload);
        }

        /// <summary>Client → server.</summary>
        public SendResult SendToServer(Delivery delivery, ReadOnlySpan<byte> payload)
        {
            if (!IsClient || _clientState != ClientState.Connected) return SendResult.NotStarted;
            return SendData(_serverConnection, delivery, payload);
        }

        /// <summary>Server → every connected client. Returns the worst result seen.</summary>
        public SendResult Broadcast(Delivery delivery, ReadOnlySpan<byte> payload)
        {
            if (!IsServer) return SendResult.NotStarted;
            var worst = SendResult.Ok;
            foreach (var clientId in _clientIds)
            {
                var result = Send(clientId, delivery, payload);
                if (result != SendResult.Ok) worst = result;
            }

            return worst;
        }

        private SendResult SendData(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > MaxPayload(delivery)) return SendResult.TooLarge;
            EnsureSendBuffer(payload.Length + 1);
            _sendBuffer[0] = (byte)MessageType.Data;
            payload.CopyTo(new Span<byte>(_sendBuffer, 1, payload.Length));
            return _transport.Send(connection, delivery, new ReadOnlySpan<byte>(_sendBuffer, 0, payload.Length + 1));
        }

        private void EnsureSendBuffer(int size)
        {
            if (_sendBuffer.Length < size)
            {
                _sendBuffer = new byte[Math.Max(size, _sendBuffer.Length * 2)];
            }
        }

        private void SendControl(ConnectionId connection, MessageType type, Action<NetworkWriter> body)
        {
            _writer.Reset();
            _writer.WriteByte((byte)type);
            body?.Invoke(_writer);
            _transport.Send(connection, Delivery.ReliableSequenced, _writer.WrittenSpan);
        }

        private void QueueTransportClose(ConnectionId connection, MessageType type, Action<NetworkWriter> body)
        {
            _writer.Reset();
            _writer.WriteByte((byte)type);
            body?.Invoke(_writer);
            var pending = new PendingTransportClose
            {
                Id = connection,
                ControlPacket = _writer.ToArray(),
                Deadline = _clock.Now + Math.Max(0.25, Math.Min(2.0, _config.HandshakeTimeoutSeconds)),
            };

            var result = _transport.Send(connection, Delivery.ReliableSequenced, pending.ControlPacket);
            if (result == SendResult.Ok)
            {
                pending.Sent = true;
                pending.SentOnUpdate = _updateSerial;
            }
            else if (result != SendResult.QueueFull)
            {
                _transport.Disconnect(connection);
                return;
            }

            _pendingTransportCloses.Add(pending);
        }

        private void ProcessPendingTransportCloses()
        {
            double now = _clock.Now;
            for (int i = _pendingTransportCloses.Count - 1; i >= 0; i--)
            {
                var pending = _pendingTransportCloses[i];
                if (pending.Sent && pending.SentOnUpdate < _updateSerial)
                {
                    _transport.Disconnect(pending.Id);
                    _pendingTransportCloses.RemoveAt(i);
                    continue;
                }

                if (now >= pending.Deadline)
                {
                    _transport.Disconnect(pending.Id);
                    _pendingTransportCloses.RemoveAt(i);
                    continue;
                }

                if (!pending.Sent)
                {
                    var result = _transport.Send(pending.Id, Delivery.ReliableSequenced, pending.ControlPacket);
                    if (result == SendResult.Ok)
                    {
                        pending.Sent = true;
                        pending.SentOnUpdate = _updateSerial;
                    }
                    else if (result != SendResult.QueueFull)
                    {
                        _transport.Disconnect(pending.Id);
                        _pendingTransportCloses.RemoveAt(i);
                    }
                }
            }
        }

        private void ForgetPendingTransportClose(ConnectionId connection)
        {
            for (int i = _pendingTransportCloses.Count - 1; i >= 0; i--)
            {
                if (_pendingTransportCloses[i].Id == connection)
                {
                    _pendingTransportCloses.RemoveAt(i);
                }
            }
        }

        private void SendPing(double now)
        {
            _lastPingSentAt = now;
            _writer.Reset();
            _writer.WriteByte((byte)MessageType.Ping);
            _writer.WriteDouble(now);
            _transport.Send(_serverConnection, Delivery.Unreliable, _writer.WrittenSpan);
        }

        #endregion

        #region - Transport events -

        void ITransportEvents.OnConnected(ConnectionId connection)
        {
            double now = _clock.Now;
            if (IsServer)
            {
                _connections[connection.Value] = new Connection
                {
                    Id = connection,
                    State = ConnectionState.Handshaking,
                    ConnectedAt = now,
                    LastReceivedAt = now,
                };
                return;
            }

            if (_clientState != ClientState.Connecting) return;
            _serverConnection = connection;
            _clientState = ClientState.Handshaking;
            _lastReceivedFromServer = now;

            var payload = _connectionPayload;
            SendControl(connection, MessageType.Hello, w =>
            {
                w.WriteUInt32(_config.ProtocolVersion);
                w.WriteUInt32(_config.ContentHash);
                w.WriteBytesWithLength(payload);
            });
        }

        void ITransportEvents.OnDisconnected(ConnectionId connection, DisconnectReason reason)
        {
            ForgetPendingTransportClose(connection);
            if (IsServer)
            {
                if (_connections.TryGetValue(connection.Value, out var existing))
                {
                    CloseConnection(existing, Map(reason), notifyPeer: false);
                }

                return;
            }

            if (_clientState == ClientState.Disconnected) return;
            // Before the handshake the session has not adopted the connection yet; a connect failure
            // arrives as a disconnect of the transport's server connection.
            bool isServerLink = connection == _serverConnection ||
                                (_clientState == ClientState.Connecting && connection == _transport.ServerConnection);
            if (!isServerLink) return;
            EndClientConnection(Map(reason));
            _transport.Shutdown();
            Role = SessionRole.None;
        }

        void ITransportEvents.OnData(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload)
        {
            if (payload.Length == 0) return;

            if (_receiveBuffer.Length < payload.Length)
            {
                _receiveBuffer = new byte[Math.Max(payload.Length, _receiveBuffer.Length * 2)];
            }

            payload.CopyTo(_receiveBuffer);
            _reader.SetSource(_receiveBuffer, 0, payload.Length);
            var type = (MessageType)_reader.ReadByte();

            try
            {
                if (IsServer)
                {
                    HandleServerMessage(connection, delivery, type);
                }
                else
                {
                    HandleClientMessage(connection, delivery, type, payload);
                }
            }
            catch (NetworkSerializationException)
            {
                // A malformed packet from a peer is that peer's problem, not the session's.
                if (IsServer && _connections.TryGetValue(connection.Value, out var bad))
                {
                    CloseConnection(bad, SessionDisconnectReason.TransportError, notifyPeer: true);
                }
                else if (IsClient)
                {
                    FailClient(SessionDisconnectReason.TransportError);
                }
            }
        }

        private static SessionDisconnectReason Map(DisconnectReason reason) => reason switch
        {
            DisconnectReason.Local => SessionDisconnectReason.Local,
            DisconnectReason.Remote => SessionDisconnectReason.Remote,
            DisconnectReason.Timeout => SessionDisconnectReason.Timeout,
            _ => SessionDisconnectReason.TransportError,
        };

        #endregion

        #region - Server handshake and messages -

        private void HandleServerMessage(ConnectionId connectionId, Delivery delivery, MessageType type)
        {
            if (!_connections.TryGetValue(connectionId.Value, out var connection) || connection.State == ConnectionState.Closing) return;
            connection.LastReceivedAt = _clock.Now;

            switch (type)
            {
                case MessageType.Hello:
                    if (connection.State == ConnectionState.Handshaking)
                    {
                        HandleHello(connection);
                    }
                    break;

                case MessageType.Ping:
                    if (connection.State == ConnectionState.Connected)
                    {
                        double clientTime = _reader.ReadDouble();
                        _writer.Reset();
                        _writer.WriteByte((byte)MessageType.Pong);
                        _writer.WriteDouble(clientTime);
                        _writer.WriteDouble(_clock.Now);
                        _writer.WriteUInt32(_tick);
                        _transport.Send(connectionId, Delivery.Unreliable, _writer.WrittenSpan);
                    }
                    break;

                case MessageType.Disconnect:
                {
                    var reason = (SessionDisconnectReason)_reader.ReadByte();
                    CloseConnection(connection, reason == SessionDisconnectReason.Local ? SessionDisconnectReason.Remote : reason, notifyPeer: false);
                    _transport.Disconnect(connectionId);
                    break;
                }

                case MessageType.Data:
                    if (connection.State == ConnectionState.Connected)
                    {
                        Data?.Invoke(connection.ClientId, delivery, _reader);
                    }
                    break;
            }
        }

        private void HandleHello(Connection connection)
        {
            uint protocol = _reader.ReadUInt32();
            uint contentHash = _reader.ReadUInt32();
            var payload = _reader.ReadBytesWithLength();

            if (protocol != _config.ProtocolVersion || (_config.ContentHash != 0 && contentHash != _config.ContentHash))
            {
                Reject(connection, SessionDisconnectReason.ProtocolMismatch, $"protocol {protocol}/{contentHash:X8} vs {_config.ProtocolVersion}/{_config.ContentHash:X8}");
                return;
            }

            if (_clientIds.Count >= _config.MaxClients)
            {
                Reject(connection, SessionDisconnectReason.ServerFull, "server full");
                return;
            }

            ulong clientId = _nextClientId;
            var approval = Approval?.Invoke(clientId, payload) ?? ConnectionApproval.Approve();
            if (!approval.Approved)
            {
                Reject(connection, SessionDisconnectReason.Rejected, approval.Reason);
                return;
            }

            _nextClientId++;
            connection.ClientId = clientId;
            connection.State = ConnectionState.Connected;
            _clients.Add(clientId, connection);
            _clientIds.Add(clientId);

            double tickRate = _config.TickRate;
            uint tick = _tick;
            double serverTime = _clock.Now;
            SendControl(connection.Id, MessageType.Welcome, w =>
            {
                w.WriteUInt64(clientId);
                w.WriteDouble(tickRate);
                w.WriteUInt32(tick);
                w.WriteDouble(serverTime);
            });

            ClientConnected?.Invoke(clientId);
        }

        private void Reject(Connection connection, SessionDisconnectReason reason, string detail)
        {
            connection.State = ConnectionState.Closing;
            QueueTransportClose(connection.Id, MessageType.Reject, w =>
            {
                w.WriteByte((byte)reason);
                w.WriteString(detail);
            });
            _connections.Remove(connection.Id.Value);
        }

        private void CloseConnection(Connection connection, SessionDisconnectReason reason, bool notifyPeer)
        {
            if (connection.State == ConnectionState.Closing) return;
            bool wasConnected = connection.State == ConnectionState.Connected;
            connection.State = ConnectionState.Closing;

            if (notifyPeer)
            {
                QueueTransportClose(connection.Id, MessageType.Disconnect, w => w.WriteByte((byte)reason));
            }

            _connections.Remove(connection.Id.Value);
            if (connection.ClientId != InvalidClientId)
            {
                _clients.Remove(connection.ClientId);
                _clientIds.Remove(connection.ClientId);
                if (LocalPlayerClientId == connection.ClientId)
                {
                    LocalPlayerClientId = InvalidClientId;
                }
            }

            if (wasConnected)
            {
                ClientDisconnected?.Invoke(connection.ClientId, reason);
            }
        }

        #endregion

        #region - Client handshake and messages -

        private void HandleClientMessage(ConnectionId connectionId, Delivery delivery, MessageType type,
            ReadOnlySpan<byte> payload)
        {
            if (connectionId != _serverConnection || _clientState == ClientState.Disconnected) return;
            double now = _clock.Now;
            _lastReceivedFromServer = now;

            switch (type)
            {
                case MessageType.Welcome:
                    if (_clientState == ClientState.Handshaking)
                    {
                        LocalClientId = _reader.ReadUInt64();
                        double tickRate = _reader.ReadDouble();
                        uint serverTick = _reader.ReadUInt32();
                        double serverTime = _reader.ReadDouble();
                        _config.TickRate = tickRate;
                        _tick = serverTick;
                        _tickAccumulator = 0;
                        _lastUpdateTime = now;
                        // A first, one-way estimate; pings refine it.
                        _clockOffset = serverTime - now;
                        _serverTimeAtSync = serverTime;
                        _serverTickAtSync = serverTick;
                        _clientState = ClientState.Connected;
                        Connected?.Invoke();
                        if (_clientState == ClientState.Connected)
                        {
                            SendPing(now);
                        }

                        // Anything that overtook this welcome on the other reliable pipeline, in the
                        // order it arrived — after Connected, so whatever binds on connect is ready to
                        // receive a world.
                        DeliverHeldData();
                    }
                    break;

                case MessageType.Reject:
                {
                    var reason = (SessionDisconnectReason)_reader.ReadByte();
                    _ = _reader.ReadString();
                    FailClient(reason);
                    break;
                }

                case MessageType.Pong:
                    if (_clientState == ClientState.Connected)
                    {
                        double sentAt = _reader.ReadDouble();
                        double serverTime = _reader.ReadDouble();
                        uint serverTick = _reader.ReadUInt32();
                        ApplyTimeSample(now, sentAt, serverTime, serverTick);
                    }
                    break;

                case MessageType.Disconnect:
                {
                    var reason = (SessionDisconnectReason)_reader.ReadByte();
                    _transport.Disconnect(_serverConnection);
                    EndClientConnection(reason == SessionDisconnectReason.Local ? SessionDisconnectReason.Remote : reason);
                    _transport.Shutdown();
                    Role = SessionRole.None;
                    break;
                }

                case MessageType.Data:
                    if (_clientState == ClientState.Connected)
                    {
                        Data?.Invoke(ServerClientId, delivery, _reader);
                    }
                    else if (delivery.IsReliable())
                    {
                        HoldUntilWelcome(delivery, payload);
                    }

                    break;
            }
        }

        /// <summary>
        /// Reliable data that arrived before the welcome it should have followed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The welcome and the world arrive on two different pipelines, and nothing orders one against
        /// the other.</b> Control messages go out reliable-sequenced; the replicator's spawns go out
        /// reliable-<i>fragmented</i>-sequenced, because a full spawn can exceed a datagram. Those are two
        /// sequence spaces — in this transport, in UTP, and in Steam's — so ordering holds within each and
        /// not between them. On a link with no jitter every packet takes exactly the same time and send
        /// order survives by accident, which is why this was invisible for as long as it was.
        /// </para>
        /// <para>
        /// Add a millisecond of jitter and the first spawn overtakes the welcome. A client that discarded
        /// it would be discarding <i>reliable</i> data, which by definition is never sent again: the
        /// joining player gets an empty world and stays in it. Holding the data for the few milliseconds
        /// until the welcome lands costs a list and fixes it exactly.
        /// </para>
        /// <para>
        /// Only reliable classes are held. An unreliable snapshot from before you were welcomed describes
        /// a world you had no way to interpret and will be superseded within a tick, which is what
        /// "unreliable" was chosen to mean.
        /// </para>
        /// </remarks>
        private void HoldUntilWelcome(Delivery delivery, ReadOnlySpan<byte> payload)
        {
            // A bound, because everything arriving before a welcome that never comes is otherwise held
            // forever. A real handshake takes a round trip; anything past this is a server that is not
            // going to welcome us.
            if (_preWelcome.Count >= MaxHeldBeforeWelcome)
            {
                FailClient(SessionDisconnectReason.TransportError);
                return;
            }

            _preWelcome.Add((delivery, payload.ToArray()));
        }

        /// <summary>Replays what was held, in arrival order, once the welcome has been processed.</summary>
        private void DeliverHeldData()
        {
            if (_preWelcome.Count == 0)
            {
                return;
            }

            // A copy, because a handler may disconnect us — which clears the list underneath the loop.
            _heldScratch.Clear();
            _heldScratch.AddRange(_preWelcome);
            _preWelcome.Clear();

            foreach (var (delivery, payload) in _heldScratch)
            {
                if (_clientState != ClientState.Connected)
                {
                    return;
                }

                _reader.SetSource(payload, 0, payload.Length);
                _reader.ReadByte();   // the message type, already known to be Data
                Data?.Invoke(ServerClientId, delivery, _reader);
            }

            _heldScratch.Clear();
        }

        private void ApplyTimeSample(double now, double sentAt, double serverTime, uint serverTick)
        {
            double rtt = Math.Max(0, now - sentAt);
            double offset = serverTime + rtt * 0.5 - now;
            if (!_hasTimeSync)
            {
                _rtt = rtt;
                _clockOffset = offset;
                _hasTimeSync = true;
            }
            else
            {
                float a = _config.TimeSyncSmoothing;
                _rtt += (rtt - _rtt) * a;
                _clockOffset += (offset - _clockOffset) * a;
            }

            _serverTimeAtSync = serverTime;
            _serverTickAtSync = serverTick;
        }

        private void EndClientConnection(SessionDisconnectReason reason)
        {
            bool wasKnown = _clientState != ClientState.Disconnected;
            _clientState = ClientState.Disconnected;
            _serverConnection = ConnectionId.Invalid;
            LocalClientId = InvalidClientId;
            _hasTimeSync = false;

            // A world we were never welcomed into is not one to replay on the next connection.
            _preWelcome.Clear();
            _heldScratch.Clear();
            if (wasKnown)
            {
                Disconnected?.Invoke(reason);
            }
        }

        #endregion
    }
}
