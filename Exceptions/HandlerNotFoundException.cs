namespace SocketServerCore.Exceptions;

public class HandlerNotFoundException(ushort eventId)
    : SocketServerException($"No handler registered for event ID 0x{eventId:X4}")
{
    public ushort EventId { get; } = eventId;
}