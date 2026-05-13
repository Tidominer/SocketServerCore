using System.Reflection;

namespace SocketServerCore.Handlers;

public class HandlerMethod
{
    public ushort EventId { get; set; }
    public MethodInfo MethodInfo { get; set; } = null!;
    public Type? RequestType { get; set; }
    public Type? ResponseType { get; set; }
    public Type ConnectionType { get; set; } = null!;
    public bool RequiresAuthentication { get; set; }
    public object? HandlerInstance { get; set; }
    public Delegate? HandlerDelegate { get; set; }
}