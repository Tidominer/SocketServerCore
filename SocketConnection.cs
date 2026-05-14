using System.Net.Sockets;
using SocketServerCore.Handlers;
using SocketServerCore.Serialization;

namespace SocketServerCore;

public class SocketConnection : IDisposable
{
    private readonly NetworkStream _stream;
    private readonly TcpClient _tcpClient;
    private readonly ISerializer _serializer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Action<Guid>? _onDisconnect;
    private bool _disposed;

    public Guid Id { get; }
    public bool IsConnected { get; private set; }
    public string RemoteEndPoint { get; }
    public object? User { get; set; }
    public DateTime ConnectedAt { get; }

    internal SocketConnection(TcpClient tcpClient, ISerializer serializer, Action<Guid>? onDisconnect = null)
    {
        _tcpClient = tcpClient;
        _serializer = serializer;
        _onDisconnect = onDisconnect;
        _stream = tcpClient.GetStream();
        Id = Guid.NewGuid();
        IsConnected = true;
        RemoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        ConnectedAt = DateTime.UtcNow;
    }

    public async Task SendAsync<T>(ushort eventId, T data, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SocketConnection));

        if (!IsConnected)
            throw new InvalidOperationException("Connection is not active");

        await _sendLock.WaitAsync(ct);

        try
        {
            var payload = _serializer.Serialize(data);
            var eventIdBytes = BitConverter.GetBytes(eventId);
            var lengthBytes = BitConverter.GetBytes(payload.Length);

            var fullMessage = new byte[6 + payload.Length];
            Buffer.BlockCopy(eventIdBytes, 0, fullMessage, 0, 2);
            Buffer.BlockCopy(lengthBytes, 0, fullMessage, 2, 4);
            Buffer.BlockCopy(payload, 0, fullMessage, 6, payload.Length);

            await _stream.WriteAsync(fullMessage, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    internal async Task ReceiveLoopAsync(HandlerRegistry registry, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                // Read eventId (2 bytes)
                var eventIdBytes = await ReadExactAsync(2, ct);
                if (eventIdBytes == null) break;

                ushort eventId = BitConverter.ToUInt16(eventIdBytes, 0);

                // Read payload length (4 bytes)
                var lengthBytes = await ReadExactAsync(4, ct);
                if (lengthBytes == null) break;

                int payloadLength = BitConverter.ToInt32(lengthBytes, 0);

                // Read payload
                var payload = await ReadExactAsync(payloadLength, ct);
                if (payload == null) break;

                // Process message
                var handler = registry.GetHandler(eventId);
                if (handler != null)
                {
                    try
                    {
                        await registry.InvokeHandlerAsync(this, handler, payload);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SocketServer] Error processing event 0x{eventId:X4}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[SocketServer] No handler found for event ID 0x{eventId:X4}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SocketServer] Connection {Id} error: {ex.Message}");
        }
        finally
        {
            Cleanup();
        }
    }

    private async Task<byte[]?> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        int totalRead = 0;

        while (totalRead < count)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0) return null; // Connection closed
            totalRead += read;
        }

        return buffer;
    }

    private void Cleanup()
    {
        if (_disposed) return;

        IsConnected = false;

        try
        {
            _stream.Close();
            _tcpClient.Close();
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            _onDisconnect?.Invoke(Id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Cleanup();
        _sendLock.Dispose();
        _stream.Dispose();
        _tcpClient.Dispose();

        _disposed = true;
    }
}