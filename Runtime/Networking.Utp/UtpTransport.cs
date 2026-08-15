using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using Error = Unity.Networking.Transport.Error;

namespace Vapor.Networking.Utp
{
    /// <summary>Knobs for <see cref="UtpTransport"/>. Defaults suit a game server on a LAN or the internet.</summary>
    public sealed class UtpTransportSettings
    {
        /// <summary>Packets in flight per reliable pipeline before sends report <see cref="SendResult.QueueFull"/>. UTP caps this at 2040; 32–64 is typical.</summary>
        public int ReliableWindowSize = 64;

        /// <summary>Largest reliable-fragmented message. Full spawn state and big rpcs must fit.</summary>
        public int FragmentationPayloadCapacity = 32 * 1024;

        public int ConnectTimeoutMs = 1000;
        public int MaxConnectAttempts = 10;
        public int DisconnectTimeoutMs = 30000;
        public int HeartbeatTimeoutMs = 500;
        public int SendQueueCapacity = 1024;
        public int ReceiveQueueCapacity = 1024;

        /// <summary>UDP payload per datagram. 1400 fits every common path MTU.</summary>
        public int MaxMessageSize = 1400;

        /// <summary>Development: insert UTP's simulator stage on every pipeline.</summary>
        public bool EnableSimulator;
        public int SimulatorDelayMs;
        public int SimulatorJitterMs;
        public int SimulatorDropPercentage;
        public uint SimulatorSeed = 1;
    }

    /// <summary>
    /// <see cref="IVaporTransport"/> over Unity Transport (UTP): one <see cref="NetworkDriver"/>, four
    /// pipelines for the four delivery classes, synchronous pumping from <see cref="Poll"/>.
    /// </summary>
    /// <remarks>
    /// The transport keeps its own connection ids (small ints, never reused within a run) so callers
    /// hold no UTP types. Backpressure — <c>NetworkSendQueueFull</c> from the reliable pipelines when
    /// the window is full — surfaces as <see cref="SendResult.QueueFull"/> and is expected; the
    /// replicator keeps its records and tries again next tick.
    /// </remarks>
    public sealed class UtpTransport : IVaporTransport
    {
        private NetworkDriver _driver;
        private NetworkPipeline _unreliable;
        private NetworkPipeline _unreliableSequenced;
        private NetworkPipeline _reliable;
        private NetworkPipeline _fragmented;
        private readonly int[] _maxPayload = new int[DeliveryExtensions.Count];

        private readonly Dictionary<int, NetworkConnection> _connectionsById = new();
        private readonly Dictionary<NetworkConnection, ConnectionId> _idsByConnection = new();
        private readonly List<(ConnectionId, DisconnectReason)> _pendingDisconnects = new();
        private readonly List<(ConnectionId, DisconnectReason)> _disconnectScratch = new();
        private byte[] _receiveScratch = new byte[2048];
        private byte[] _sendScratch = new byte[2048];
        private TransportStats _stats;
        private int _nextConnectionId = 1;

        public UtpTransport(UtpTransportSettings settings = null)
        {
            Settings = settings ?? new UtpTransportSettings();
        }

        public UtpTransportSettings Settings { get; }

        public bool IsRunning { get; private set; }

        public bool IsServer { get; private set; }

        public ConnectionId ServerConnection { get; private set; } = ConnectionId.Invalid;

        public TransportStats Stats => _stats;

        public int ConnectionCount => _connectionsById.Count;

        #region - Lifecycle -

        public bool StartServer(TransportEndpoint bind)
        {
            if (IsRunning) return false;
            if (!TryCreateDriver()) return false;

            var endpoint = ToUtp(bind, anyForServer: true);
            if (_driver.Bind(endpoint) != 0)
            {
                Debug.LogError($"UtpTransport: failed to bind {bind}.");
                DisposeDriver();
                return false;
            }

            if (_driver.Listen() != 0)
            {
                Debug.LogError($"UtpTransport: failed to listen on {bind}.");
                DisposeDriver();
                return false;
            }

            IsServer = true;
            IsRunning = true;
            return true;
        }

        public bool StartClient(TransportEndpoint remote)
        {
            if (IsRunning) return false;
            if (!TryCreateDriver()) return false;

            var endpoint = ToUtp(remote, anyForServer: false);
            var connection = _driver.Connect(endpoint);
            if (!connection.IsCreated)
            {
                Debug.LogError($"UtpTransport: failed to start connecting to {remote}.");
                DisposeDriver();
                return false;
            }

            IsServer = false;
            IsRunning = true;
            ServerConnection = Register(connection);
            return true;
        }

        public void Shutdown()
        {
            if (!IsRunning) return;

            foreach (var pair in new List<KeyValuePair<int, NetworkConnection>>(_connectionsById))
            {
                Disconnect(new ConnectionId(pair.Key));
            }

            if (_driver.IsCreated)
            {
                _driver.ScheduleFlushSend(default).Complete();
            }

            DisposeDriver();
            IsRunning = false;
            ServerConnection = ConnectionId.Invalid;
        }

        public void Dispose() => Shutdown();

        private bool TryCreateDriver()
        {
            try
            {
                var s = Settings;
                var settings = new NetworkSettings(Allocator.Temp);
                settings.WithNetworkConfigParameters(
                    connectTimeoutMS: s.ConnectTimeoutMs,
                    maxConnectAttempts: s.MaxConnectAttempts,
                    disconnectTimeoutMS: s.DisconnectTimeoutMs,
                    heartbeatTimeoutMS: s.HeartbeatTimeoutMs,
                    receiveQueueCapacity: s.ReceiveQueueCapacity,
                    sendQueueCapacity: s.SendQueueCapacity,
                    maxMessageSize: s.MaxMessageSize);
                settings.WithReliableStageParameters(windowSize: s.ReliableWindowSize);
                settings.WithFragmentationStageParameters(payloadCapacity: s.FragmentationPayloadCapacity);
                if (s.EnableSimulator)
                {
                    settings.WithSimulatorStageParameters(
                        maxPacketCount: 4096,
                        packetDelayMs: s.SimulatorDelayMs,
                        packetJitterMs: s.SimulatorJitterMs,
                        packetDropPercentage: s.SimulatorDropPercentage,
                        randomSeed: s.SimulatorSeed);
                }

                _driver = NetworkDriver.Create(settings);

                if (s.EnableSimulator)
                {
                    _unreliable = _driver.CreatePipeline(typeof(SimulatorPipelineStage));
                    _unreliableSequenced = _driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
                    _reliable = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
                    _fragmented = _driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
                }
                else
                {
                    _unreliable = NetworkPipeline.Null;
                    _unreliableSequenced = _driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
                    _reliable = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                    _fragmented = _driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
                }

                _maxPayload[(int)Delivery.Unreliable] = s.MaxMessageSize - _driver.MaxHeaderSize(_unreliable);
                _maxPayload[(int)Delivery.UnreliableSequenced] = s.MaxMessageSize - _driver.MaxHeaderSize(_unreliableSequenced);
                _maxPayload[(int)Delivery.ReliableSequenced] = s.MaxMessageSize - _driver.MaxHeaderSize(_reliable);
                _maxPayload[(int)Delivery.ReliableFragmentedSequenced] = s.FragmentationPayloadCapacity - _driver.MaxHeaderSize(_fragmented);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"UtpTransport: failed to create the network driver: {e.Message}");
                DisposeDriver();
                return false;
            }
        }

        private void DisposeDriver()
        {
            if (_driver.IsCreated)
            {
                _driver.Dispose();
            }

            _driver = default;
            _connectionsById.Clear();
            _idsByConnection.Clear();
        }

        private static NetworkEndpoint ToUtp(TransportEndpoint endpoint, bool anyForServer)
        {
            if (string.IsNullOrEmpty(endpoint.Address) || endpoint.Address == "0.0.0.0" || endpoint.Address == "any")
            {
                return anyForServer ? NetworkEndpoint.AnyIpv4.WithPort(endpoint.Port) : NetworkEndpoint.LoopbackIpv4.WithPort(endpoint.Port);
            }

            if (endpoint.Address == "localhost" || endpoint.Address == "loopback")
            {
                return NetworkEndpoint.LoopbackIpv4.WithPort(endpoint.Port);
            }

            if (NetworkEndpoint.TryParse(endpoint.Address, endpoint.Port, out var parsed))
            {
                return parsed;
            }

            if (NetworkEndpoint.TryParse(endpoint.Address, endpoint.Port, out parsed, NetworkFamily.Ipv6))
            {
                return parsed;
            }

            throw new ArgumentException($"'{endpoint.Address}' is not an IP address. Resolve host names before starting the transport.");
        }

        #endregion

        #region - Connections -

        private ConnectionId Register(NetworkConnection connection)
        {
            var id = new ConnectionId(_nextConnectionId++);
            _connectionsById.Add(id.Value, connection);
            _idsByConnection[connection] = id;
            return id;
        }

        private void Forget(ConnectionId id, NetworkConnection connection)
        {
            _connectionsById.Remove(id.Value);
            _idsByConnection.Remove(connection);
            if (ServerConnection == id) ServerConnection = ConnectionId.Invalid;
        }

        public void Disconnect(ConnectionId connection)
        {
            if (!_connectionsById.TryGetValue(connection.Value, out var utp)) return;
            if (_driver.IsCreated)
            {
                _driver.Disconnect(utp);
            }

            Forget(connection, utp);
            _pendingDisconnects.Add((connection, DisconnectReason.Local));
        }

        #endregion

        #region - Send -

        public int MaxPayload(Delivery delivery) => _maxPayload[(int)delivery];

        public SendResult Send(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload)
        {
            if (!IsRunning || !_driver.IsCreated) return SendResult.NotStarted;
            if (!_connectionsById.TryGetValue(connection.Value, out var utp)) return SendResult.Disconnected;
            if (payload.Length > MaxPayload(delivery))
            {
                _stats.SendsRejected++;
                return SendResult.TooLarge;
            }

            var pipeline = PipelineFor(delivery);
            int status = _driver.BeginSend(pipeline, utp, out var writer, payload.Length);
            if (status < 0)
            {
                return Classify(status);
            }

            if (_sendScratch.Length < payload.Length)
            {
                _sendScratch = new byte[Math.Max(payload.Length, _sendScratch.Length * 2)];
            }

            payload.CopyTo(_sendScratch);
            if (!writer.WriteBytes(new Span<byte>(_sendScratch, 0, payload.Length)))
            {
                _driver.AbortSend(writer);
                _stats.SendsRejected++;
                return SendResult.TooLarge;
            }

            status = _driver.EndSend(writer);
            if (status < 0)
            {
                return Classify(status);
            }

            _stats.PacketsSent++;
            _stats.BytesSent += payload.Length;
            return SendResult.Ok;
        }

        private SendResult Classify(int status)
        {
            switch ((Error.StatusCode)status)
            {
                case Error.StatusCode.NetworkSendQueueFull:
                    _stats.SendsRejected++;
                    return SendResult.QueueFull;
                case Error.StatusCode.NetworkPacketOverflow:
                    _stats.SendsRejected++;
                    return SendResult.TooLarge;
                case Error.StatusCode.NetworkIdMismatch:
                case Error.StatusCode.NetworkVersionMismatch:
                case Error.StatusCode.NetworkStateMismatch:
                    return SendResult.Disconnected;
                default:
                    _stats.SendsRejected++;
                    return SendResult.QueueFull;
            }
        }

        private NetworkPipeline PipelineFor(Delivery delivery) => delivery switch
        {
            Delivery.Unreliable => _unreliable,
            Delivery.UnreliableSequenced => _unreliableSequenced,
            Delivery.ReliableSequenced => _reliable,
            _ => _fragmented,
        };

        private Delivery DeliveryFor(NetworkPipeline pipeline)
        {
            if (pipeline == _unreliableSequenced) return Delivery.UnreliableSequenced;
            if (pipeline == _reliable) return Delivery.ReliableSequenced;
            if (pipeline == _fragmented) return Delivery.ReliableFragmentedSequenced;
            return Delivery.Unreliable;
        }

        #endregion

        #region - Poll -

        public void Poll(ITransportEvents sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            if (_pendingDisconnects.Count > 0)
            {
                _disconnectScratch.Clear();
                _disconnectScratch.AddRange(_pendingDisconnects);
                _pendingDisconnects.Clear();
                foreach (var (connection, reason) in _disconnectScratch)
                {
                    sink.OnDisconnected(connection, reason);
                }
            }

            if (!IsRunning || !_driver.IsCreated) return;

            _driver.ScheduleUpdate().Complete();

            if (IsServer)
            {
                NetworkConnection incoming;
                while ((incoming = _driver.Accept()) != default)
                {
                    var id = Register(incoming);
                    sink.OnConnected(id);
                    if (!IsRunning) return;
                }
            }

            NetworkEvent.Type type;
            while (_driver.IsCreated && (type = _driver.PopEvent(out var connection, out var reader, out var pipeline)) != NetworkEvent.Type.Empty)
            {
                if (!_idsByConnection.TryGetValue(connection, out var id))
                {
                    // Already forgotten locally (we disconnected it); drain silently.
                    continue;
                }

                switch (type)
                {
                    case NetworkEvent.Type.Connect:
                        sink.OnConnected(id);
                        break;

                    case NetworkEvent.Type.Data:
                    {
                        int length = reader.Length;
                        if (_receiveScratch.Length < length)
                        {
                            _receiveScratch = new byte[Math.Max(length, _receiveScratch.Length * 2)];
                        }

                        reader.ReadBytes(new Span<byte>(_receiveScratch, 0, length));
                        _stats.PacketsReceived++;
                        _stats.BytesReceived += length;
                        sink.OnData(id, DeliveryFor(pipeline), new ReadOnlySpan<byte>(_receiveScratch, 0, length));
                        break;
                    }

                    case NetworkEvent.Type.Disconnect:
                    {
                        var reason = DisconnectReason.Remote;
                        if (reader.Length > 0)
                        {
                            reason = Map((Error.DisconnectReason)reader.ReadByte());
                        }

                        Forget(id, connection);
                        sink.OnDisconnected(id, reason);
                        break;
                    }
                }

                if (!IsRunning) return;
            }
        }

        private static DisconnectReason Map(Error.DisconnectReason reason) => reason switch
        {
            Error.DisconnectReason.Timeout => DisconnectReason.Timeout,
            Error.DisconnectReason.MaxConnectionAttempts => DisconnectReason.Timeout,
            Error.DisconnectReason.ClosedByRemote => DisconnectReason.Remote,
            Error.DisconnectReason.Default => DisconnectReason.Remote,
            _ => DisconnectReason.TransportError,
        };

        #endregion
    }
}
