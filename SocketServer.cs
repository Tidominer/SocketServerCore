using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SocketServerCore.Handlers;
using SocketServerCore.Serialization;

namespace SocketServerCore;

public class SocketServer : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly ISerializer _serializer;
    private readonly HandlerRegistry _handlerRegistry;
    private readonly ConcurrentDictionary<Guid, SocketConnection> _connections = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private TcpListener? _listener;
    private Task? _acceptTask;
    private bool _disposed;

    public Action<SocketConnection>? OnClientConnected;
    public Action<SocketConnection>? OnClientDisconnected;
    public Action<SocketErrorEventArgs>? OnError;

    public bool IsRunning { get; private set; }
    public int ActiveConnections => _connections.Count;
    public IReadOnlyCollection<SocketConnection> Connections => (IReadOnlyCollection<SocketConnection>)_connections.Values;

    private ErrorReportingMode _errorReportingMode = ErrorReportingMode.Limited;
    public ErrorReportingMode ErrorReportingMode
    {
        get => _errorReportingMode;
        set
        {
            _errorReportingMode = value;
            _handlerRegistry.ErrorReportingMode = value;
        }
    }
    
    public const ushort ErrorEventId = 0xFFFF;

    public SocketServer(string host, int port, ISerializer? serializer = null)
    {
        _host = host;
        _port = port;
        _serializer = serializer ?? new JsonSerializer();
        _handlerRegistry = new HandlerRegistry(_serializer, ErrorReportingMode, OnError);

        ValidateHostAndPort();
    }

    private void ValidateHostAndPort()
    {
        if (string.IsNullOrWhiteSpace(_host))
            throw new ArgumentException("Host cannot be empty", nameof(_host));

        if (_port < 1 || _port > 65535)
            throw new ArgumentOutOfRangeException(nameof(_port), "Port must be between 1 and 65535");
    }

    public void RegisterHandlers<T>() where T : class, new()
    {
        _handlerRegistry.RegisterHandlers<T>();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("Server is already running");

        try
        {
            IPEndPoint localEndPoint;

            // Handle wildcard addresses for listening on all interfaces
            if (_host == "0.0.0.0" || _host == "*" || _host == "")
            {
                localEndPoint = new IPEndPoint(IPAddress.Any, _port);
            }
            else if (_host == "::")
            {
                localEndPoint = new IPEndPoint(IPAddress.IPv6Any, _port);
            }
            else if (IPAddress.TryParse(_host, out var parsedIp))
            {
                localEndPoint = new IPEndPoint(parsedIp, _port);
            }
            else
            {
                // Resolve host to IP address
                var hostEntry = await Dns.GetHostEntryAsync(_host, cancellationToken);
                localEndPoint = new IPEndPoint(hostEntry.AddressList[0], _port);
            }

            _listener = new TcpListener(localEndPoint);
            _listener.Start();

            IsRunning = true;
            Console.WriteLine($"[SocketServer] Server started on {_host}:{_port}");

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);

            _acceptTask = AcceptClientsAsync(linkedCts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SocketServer] Failed to start server: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        Console.WriteLine("[SocketServer] Stopping server...");

        await _cancellationTokenSource.CancelAsync();
        _listener?.Stop();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        // Disconnect all clients
        foreach (var connection in _connections.Values.ToList())
        {
            connection.Dispose();
        }

        _connections.Clear();

        IsRunning = false;
        Console.WriteLine("[SocketServer] Server stopped");
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                var connection = new SocketConnection(tcpClient, _serializer, CleanupClient, OnError);

                _connections[connection.Id] = connection;
                Console.WriteLine($"[SocketServer] Client connected: {connection.RemoteEndPoint} (ID: {connection.Id})");

                OnClientConnected?.Invoke(connection);

                // Start receive loop for this client
                _ = Task.Run(() => connection.ReceiveLoopAsync(_handlerRegistry, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    var errorArgs = new SocketErrorEventArgs(ex, null, null, "Accepting client");
                    OnError?.Invoke(errorArgs);
                    Console.WriteLine($"[SocketServer] Error accepting client: {ex.Message}");
                }
            }
        }
    }

    public Task SendAsync<T>(Guid connectionId, ushort eventId, T data, CancellationToken ct = default)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            return connection.SendAsync(eventId, data, ct);
        }
        throw new KeyNotFoundException($"Connection {connectionId} not found");
    }

    private void CleanupClient(Guid clientId)
    {
        if (_connections.TryRemove(clientId, out var connection))
        {
            Console.WriteLine($"[SocketServer] Client disconnected: {connection.RemoteEndPoint} (ID: {connection.Id})");
            OnClientDisconnected?.Invoke(connection);
            connection.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().Wait();
        _cancellationTokenSource.Dispose();
        _disposed = true;
    }
}