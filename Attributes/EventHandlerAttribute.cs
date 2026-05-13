namespace SocketServerCore.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class EventHandlerAttribute : Attribute
{
    public ushort EventId { get; }

    public EventHandlerAttribute(ushort eventId)
    {
        EventId = eventId;
    }
}