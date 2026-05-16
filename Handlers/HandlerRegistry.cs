using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using SocketServerCore.Attributes;
using SocketServerCore.Serialization;

namespace SocketServerCore.Handlers;

public class HandlerRegistry
{
    private readonly ConcurrentDictionary<ushort, HandlerMethod> _handlers = new();
    private readonly ISerializer _serializer;
    internal ErrorReportingMode ErrorReportingMode;
    private Action<SocketErrorEventArgs>? _onError;

    public HandlerRegistry(ISerializer serializer, ErrorReportingMode errorReportingMode, Action<SocketErrorEventArgs>? onError = null)
    {
        _serializer = serializer;
        ErrorReportingMode = errorReportingMode;
        _onError = onError;
    }

    public void RegisterHandlers<T>() where T : class, new()
    {
        RegisterHandlers(typeof(T));
    }

    public void RegisterHandlers(Type handlerType)
    {
        var instance = Activator.CreateInstance(handlerType);
        if (instance == null)
            throw new InvalidOperationException($"Failed to create instance of {handlerType.Name}");

        DiscoverHandlers(handlerType, instance);
    }

    private void DiscoverHandlers(Type handlerType, object instance)
    {
        var methods = handlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<EventHandlerAttribute>();
            if (attr == null) continue;

            var handlerInfo = CreateHandlerInfo(method, attr, instance);
            _handlers.TryAdd(attr.EventId, handlerInfo);
        }
    }

    private HandlerMethod CreateHandlerInfo(MethodInfo method, EventHandlerAttribute attr, object instance)
    {
        var parameters = method.GetParameters();
        if (parameters.Length < 2)
        {
            throw new InvalidOperationException($"Handler {method.Name} must have at least 2 parameters (connection, request)");
        }

        var connectionType = parameters[0].ParameterType;
        var requestType = parameters[1].ParameterType;
        var returnType = method.ReturnType;

        Type? responseType = null;
        if (returnType != typeof(void))
        {
            if (returnType == typeof(Task))
            {
                // Async fire-and-forget
                responseType = null;
            }
            else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                // Async with return value
                responseType = returnType.GetGenericArguments()[0];
            }
            else
            {
                // Synchronous return value
                responseType = returnType;
            }
        }

        // Create delegate for faster invocation
        var handlerDelegate = CreateDelegate(method, instance);

        return new HandlerMethod
        {
            EventId = attr.EventId,
            MethodInfo = method,
            RequestType = requestType,
            ResponseType = responseType,
            ConnectionType = connectionType,
            RequiresAuthentication = connectionType != typeof(SocketConnection),
            HandlerInstance = instance,
            HandlerDelegate = handlerDelegate
        };
    }

    private Delegate? CreateDelegate(MethodInfo method, object instance)
    {
        try
        {
            // Create a closed delegate for faster invocation
            var delegateType = GetDelegateType(method);
            return Delegate.CreateDelegate(delegateType, instance, method);
        }
        catch
        {
            // Fall back to reflection if delegate creation fails
        }
        return null;
    }

    private Type GetDelegateType(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();
        var returnType = method.ReturnType;

        // Handle async methods
        if (returnType == typeof(Task))
        {
            return Expression.GetActionType(parameterTypes);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            return Expression.GetFuncType(parameterTypes.Concat(new[] { resultType }).ToArray());
        }
        else if (returnType == typeof(void))
        {
            return Expression.GetActionType(parameterTypes);
        }
        else
        {
            return Expression.GetFuncType(parameterTypes.Concat(new[] { returnType }).ToArray());
        }
    }

    public HandlerMethod? GetHandler(ushort eventId)
    {
        return _handlers.TryGetValue(eventId, out var handler) ? handler : null;
    }

    public async Task InvokeHandlerAsync(SocketConnection connection, HandlerMethod handler, byte[] data)
    {
        var parameters = handler.MethodInfo.GetParameters();
        if (parameters.Length == 0)
            throw new InvalidOperationException("Handler must have at least one parameter");

        var firstParameter = parameters[0];
        var firstParamType = firstParameter.ParameterType;

        // Check if handler can be invoked based on authentication requirements
        object? connectionParameter;
        if (firstParamType == typeof(SocketConnection))
        {
            // Public handler - always invoke
            connectionParameter = connection;
        }
        else
        {
            // Authenticated handler - check User type
            if (connection.User == null)
            {
                Console.WriteLine($"[SocketServer] Handler {handler.MethodInfo.Name} requires authenticated user, but User is null");
                return; // Skip handler
            }

            if (!firstParamType.IsAssignableFrom(connection.User.GetType()))
            {
                Console.WriteLine($"[SocketServer] Handler {handler.MethodInfo.Name} requires {firstParamType.Name}, but User is {connection.User.GetType().Name}");
                return; // Skip handler
            }

            // Type matches - use User object
            connectionParameter = connection.User;
        }

        // Deserialize request data
        var request = _serializer.Deserialize(handler.RequestType!, data);
        if (request == null)
        {
            throw new InvalidOperationException($"Failed to deserialize request to {handler.RequestType!.Name}");
        }

        try
        {
            // Invoke handler
            var result = await InvokeHandlerCore(handler, connectionParameter, request);

            // Send response if handler returns data
            if (result != null && handler.ResponseType != null)
            {
                await connection.SendAsync(handler.EventId, result);
            }
        }
        catch (Exception ex)
        {
            var errorArgs = new SocketErrorEventArgs(ex, connection, handler.EventId, $"Handler {handler.MethodInfo.Name} execution");
            _onError?.Invoke(errorArgs);
            Console.WriteLine($"[SocketServer] Error in handler {handler.MethodInfo.Name}: {ex.Message}\nPayload: {System.Text.Encoding.UTF8.GetString(data)}");
            await SendErrorResponseAsync(connection, ex);
        }
    }

    private async Task<object?> InvokeHandlerCore(HandlerMethod handler, object connectionParameter, object request)
    {
        try
        {
            // Use delegate if available for faster invocation
            if (handler.HandlerDelegate != null)
            {
                var result = handler.HandlerDelegate.DynamicInvoke(connectionParameter, request);
                if (result is Task task)
                {
                    await task;

                    // Check if Task has a Result property (Task<T>)
                    var resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty != null)
                    {
                        return resultProperty.GetValue(task);
                    }
                    return null;
                }
                return result;
            }
            else
            {
                // Fall back to reflection
                var result = handler.MethodInfo.Invoke(handler.HandlerInstance, new[] { connectionParameter, request });
                if (result is Task task)
                {
                    await task;

                    // Check if Task has a Result property (Task<T>)
                    var resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty != null)
                    {
                        return resultProperty.GetValue(task);
                    }
                    return null;
                }
                return result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SocketServer] Error invoking handler {handler.MethodInfo.Name}: {ex.Message}");
            throw;
        }
    }

    private async Task SendErrorResponseAsync(SocketConnection connection, Exception exception)
    {
        if (ErrorReportingMode == ErrorReportingMode.None)
            return;

        var errorResponse = new ErrorResponse();

        switch (ErrorReportingMode)
        {
            case ErrorReportingMode.Limited:
                errorResponse.ErrorType = "InternalServerError";
                errorResponse.Message = "An error occurred while processing your request";
                break;

            case ErrorReportingMode.Full:
                errorResponse.ErrorType = exception.GetType().Name;
                errorResponse.Message = exception.Message;
                break;
        }

        await connection.SendAsync(SocketServer.ErrorEventId, errorResponse);
    }
}