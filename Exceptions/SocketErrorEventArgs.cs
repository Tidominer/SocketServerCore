namespace SocketServerCore;

/// <summary>
/// Event arguments for socket server errors with full context and diagnostic information
/// </summary>
public class SocketErrorEventArgs : EventArgs
{
    /// <summary>
    /// The exception that occurred
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// The client connection that experienced the error (null if not connection-specific)
    /// </summary>
    public SocketConnection? Connection { get; }

    /// <summary>
    /// The event ID being processed when the error occurred (null if not during message processing)
    /// </summary>
    public ushort? EventId { get; }

    /// <summary>
    /// Human-readable context describing what operation was happening when the error occurred
    /// </summary>
    public string ErrorContext { get; }

    /// <summary>
    /// Timestamp when the error occurred
    /// </summary>
    public DateTime Timestamp { get; }

    public SocketErrorEventArgs(Exception exception, SocketConnection? connection = null,
        ushort? eventId = null, string errorContext = "")
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        Connection = connection;
        EventId = eventId;
        Timestamp = DateTime.UtcNow;
    }

    public override string ToString()
    {
        var parts = new List<string> { $"Error: {Exception.Message}" };

        if (!string.IsNullOrEmpty(ErrorContext))
            parts.Add($"Context: {ErrorContext}");

        if (Connection != null)
            parts.Add($"Connection: {Connection.Id} ({Connection.RemoteEndPoint})");

        if (EventId.HasValue)
            parts.Add($"EventId: 0x{EventId.Value:X4}");

        parts.Add($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC");

        return string.Join(" | ", parts);
    }
}