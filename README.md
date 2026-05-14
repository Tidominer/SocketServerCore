# SocketServerCore

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/nuget/v/SocketServerCore)](https://www.nuget.org/packages/SocketServerCore)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

A high-performance, event-driven TCP socket server framework for .NET 10.0. SocketServerCore provides a clean, robust abstraction for building real-time bidirectional messaging servers with support for custom protocols, authentication, and flexible handler registration.

## Features

- **Event-Driven Architecture**: Build responsive servers with attribute-based handler registration
- **High Performance**: Thread-safe operations with optimized handler invocation using delegates
- **Type-Safe Authentication**: Handler parameter types automatically determine authentication requirements
- **Flexible Protocol Support**: Binary protocol with pluggable serialization (JSON, Protocol Buffers, MessagePack, etc.)
- **Async/Await Support**: Full async/await support for scalable I/O operations
- **Connection Management**: Built-in connection lifecycle management and automatic cleanup
- **Error Reporting**: Configurable error reporting modes for secure client communication
- **Cross-Platform**: Runs on Windows, Linux, and macOS with .NET 10.0

## Installation

### NuGet Package Manager

```bash
Install-Package SocketServerCore
```

### .NET CLI

```bash
dotnet add package SocketServerCore
```

### Package Reference

```xml
<PackageReference Include="SocketServerCore" Version="1.0.0" />
```

## Quick Start

### 1. Create Request/Response Types

```csharp
using SocketServerCore;

// Request types
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ChatMessage
{
    public string Sender { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

// Response types
public class LoginResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

// Custom user connection for authenticated users
public class AuthenticatedUser
{
    public string Username { get; set; } = string.Empty;
    public string[] Roles { get; set; } = Array.Empty<string>();
}
```

### 2. Define Your Handlers

```csharp
using SocketServerCore;
using SocketServerCore.Attributes;

public class MyServerHandlers
{
    // Public handler - anyone can call
    [EventHandler(0x0001)]
    public LoginResponse Login(SocketConnection connection, LoginRequest request)
    {
        // Your authentication logic here
        bool isValid = ValidateUser(request.Username, request.Password);
        
        if (isValid)
        {
            connection.User = new AuthenticatedUser 
            { 
                Username = request.Username,
                Roles = new[] { "User" }
            };
        }
        
        return new LoginResponse 
        { 
            Success = isValid, 
            Message = isValid ? "Welcome!" : "Invalid credentials" 
        };
    }

    // Authenticated handler - requires connection.User to be AuthenticatedUser
    [EventHandler(0x0002)]
    public void SendMessage(AuthenticatedUser connection, ChatMessage message)
    {
        Console.WriteLine($"{connection.Username}: {message.Content}");
        
        // Process message (fire-and-forget, no response)
    }
}
```

### 3. Start the Server

```csharp
using SocketServerCore;
using SocketServerCore.Serialization;

// Create server with JSON serialization
var server = new SocketServer("127.0.0.1", 9000, new JsonSerializer());

// Register handler classes
server.RegisterHandlers<MyServerHandlers>();

// Optional: Set error reporting mode
server.ErrorReportingMode = ErrorReportingMode.Limited;

// Set up event handlers
server.OnClientConnected = (connection) =>
{
    Console.WriteLine($"Client connected: {connection.RemoteEndPoint}");
};

server.OnClientDisconnected = (connection) =>
{
    Console.WriteLine($"Client disconnected: {connection.RemoteEndPoint}");
};

// Start the server
await server.StartAsync();
Console.WriteLine("Server started on 127.0.0.1:9000");

// Keep server running
Console.WriteLine("Press any key to stop...");
Console.ReadKey();

// Stop the server
await server.StopAsync();
```

## Message Protocol

The server uses a binary protocol with the following format:
- **Event ID**: 2 bytes (ushort) - identifies the message type
- **Payload Length**: 4 bytes (int) - length of the serialized payload  
- **Payload**: N bytes - serialized data (JSON by default)

**Reserved Event IDs:**
- `0xFFFF` - Error responses (framework-reserved)

## Usage

### Handler Registration

Handlers are registered using the `[EventHandler(ushort eventId)]` attribute:

```csharp
public class GameHandlers
{
    // Public handler - anyone can call (SocketConnection parameter)
    [EventHandler(0x0100)]
    public ConnectionInfo Connect(SocketConnection connection, ConnectRequest request)
    {
        var user = new AuthenticatedUser { Username = request.Username };
        connection.User = user;
        return new ConnectionInfo(user.Username, DateTime.UtcNow);
    }

    // Authenticated handler - requires connection.User to be AuthenticatedUser
    [EventHandler(0x0200)]
    public PlayerMove MovePlayer(AuthenticatedUser connection, MoveRequest request)
    {
        // This handler will only execute if connection.User is AuthenticatedUser
        return ProcessMove(connection, request);
    }

    // Fire-and-forget handler (no response)
    [EventHandler(0x0300)]
    public void HandleHeartbeat(SocketConnection connection, HeartbeatRequest request)
    {
        Console.WriteLine($"Heartbeat from {connection.RemoteEndPoint}");
        // No response sent
    }

    // Async handler with response
    [EventHandler(0x0400)]
    public async Task<PlayerStats> GetStats(AuthenticatedUser connection, GetStatsRequest request)
    {
        // Simulate async database operation
        await Task.Delay(100);
        return await database.GetPlayerStats(connection.Username);
    }
}
```

### Server Configuration

```csharp
// Create server with custom settings
var server = new SocketServer("127.0.0.1", 9000, new JsonSerializer())
{
    // Configure error reporting
    ErrorReportingMode = ErrorReportingMode.Limited
};

// Register multiple handler classes
server.RegisterHandlers<GameHandlers>();
server.RegisterHandlers<ChatHandlers>();
server.RegisterHandlers<AdminHandlers>();

// Set up event handlers
server.OnClientConnected = (connection) =>
{
    Console.WriteLine($"Connected: {connection.RemoteEndPoint}");
};

server.OnClientDisconnected = (connection) =>
{
    Console.WriteLine($"Disconnected: {connection.RemoteEndPoint}");
};
```

### Connection Management

```csharp
// Send to specific client by ID
await server.SendAsync(connectionId, 0x0100, data);

// Get all connections
var allConnections = server.Connections;

// Send to specific connection object
foreach (var connection in server.Connections)
{
    await connection.SendAsync(0x0200, broadcastData);
}
```

### Custom Serialization

Implement the `ISerializer` interface for custom serialization:

```csharp
using SocketServerCore.Serialization;

public class MessagePackSerializer : ISerializer
{
    public string ContentType => "application/msgpack";
    
    public byte[] Serialize<T>(T data)
    {
        return MessagePackSerializer.Serialize(data);
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        return MessagePackSerializer.Deserialize<T>(data);
    }

    public object? Deserialize(Type type, ReadOnlySpan<byte> data)
    {
        return MessagePackSerializer.Deserialize(type, data);
    }
}

// Use in server constructor
var server = new SocketServer("127.0.0.1", 9000, new MessagePackSerializer());
```

## Error Handling

### Error Reporting Modes

Configure how much error information is sent to clients:

```csharp
public enum ErrorReportingMode
{
    None,       // No error responses sent
    Limited,    // Generic "Internal Server Error"
    Full        // Exception type, message, and stack trace
}
```

### Usage Examples

```csharp
// Development - detailed errors
var devServer = new SocketServer("127.0.0.1", 9000)
{
    ErrorReportingMode = ErrorReportingMode.Full
};

// Production - limited error exposure
var prodServer = new SocketServer("0.0.0.0", 9000)
{
    ErrorReportingMode = ErrorReportingMode.Limited
};

// High-security - no error details
var secureServer = new SocketServer("0.0.0.0", 9000)
{
    ErrorReportingMode = ErrorReportingMode.None
};
```

### Throwing Exceptions in Handlers

```csharp
[EventHandler(0x0001)]
public UserProfileResponse GetUserProfile(AuthenticatedUser connection, GetUserProfileRequest request)
{
    var user = database.GetUser(request.Username);
    if (user == null)
    {
        throw new NotFoundException($"User {request.Username} not found");
    }
    return new UserProfileResponse { User = user };
}

[EventHandler(0x0002)]
public async Task<LoginResponse> Login(AuthenticatedUser connection, LoginRequest request)
{
    if (string.IsNullOrEmpty(request.Password))
    {
        throw new ValidationException("Password cannot be empty");
    }
    
    var user = await authService.ValidateUser(request.Username, request.Password);
    if (user == null)
    {
        throw new UnauthorizedException("Invalid credentials");
    }
    
    return new LoginResponse { Token = user.Token };
}
```

## Event ID Conventions

Event IDs are `ushort` values (0x0000-0xFFFF) organized by functionality:

- **0x0000-0x00FF**: System/Core operations
- **0x0100-0x01FF**: Authentication/Authorization  
- **0x0200-0x02FF**: Game-specific operations
- **0x0300-0x03FF**: Chat/Messaging
- **0x0400-0xFFFE**: Application-specific
- **0xFFFF**: Framework reserved (Error responses)

## API Reference

### Core Classes

#### SocketServer
Main server class that manages TCP listener, client connections, and message routing.

**Constructor:**
- `SocketServer(string host, int port, ISerializer? serializer = null)`

**Methods:**
- `RegisterHandlers<T>()` - Register handler class
- `StartAsync(CancellationToken)` - Start the server
- `StopAsync()` - Stop the server  
- `SendAsync<T>(Guid connectionId, ushort eventId, T data)` - Send to specific client
- `Dispose()` - Clean up resources

**Properties:**
- `IsRunning` - Server status
- `ActiveConnections` - Number of connected clients
- `Connections` - Read-only collection of all connections
- `ErrorReportingMode` - Error reporting configuration

**Events:**
- `OnClientConnected` - Raised when client connects
- `OnClientDisconnected` - Raised when client disconnects

#### SocketConnection
Represents individual client connections with send/receive capabilities.

**Properties:**
- `Id` - Unique connection identifier (Guid)
- `User` - Authenticated user object
- `IsConnected` - Connection status
- `RemoteEndPoint` - Client address and port
- `ConnectedAt` - Connection timestamp

**Methods:**
- `SendAsync<T>(ushort eventId, T data)` - Send message to client
- `Dispose()` - Close the connection

#### ErrorReportingMode
Controls error information sent to clients when handlers throw exceptions.

**Values:**
- `None` - No error responses
- `Limited` - Generic error messages only
- `Full` - Complete error details

#### SystemEventIds
Reserved system event IDs used by the framework.

**Constants:**
- `Error = 0xFFFF` - Error response event ID

## Advanced Examples

### Authentication System

```csharp
public class AuthUser 
{
    public string Username { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}

public class AuthHandlers
{
    [EventHandler(0x0100)]
    public AuthUser Login(SocketConnection connection, LoginRequest request)
    {
        // Validate credentials
        if (ValidateCredentials(request.Username, request.Password))
        {
            var user = new AuthUser 
            { 
                Username = request.Username,
                Roles = GetUserRoles(request.Username)
            };
            connection.User = user;
            return user;
        }
        
        throw new UnauthorizedAccessException("Invalid credentials");
    }

    [EventHandler(0x0101)]
    public async Task AdminCommand(AuthUser connection, AdminRequest request)
    {
        // Only accessible if connection.User is AuthUser
        if (!connection.Roles.Contains("Admin"))
        {
            throw new UnauthorizedAccessException("Admin access required");
        }
        
        // Process admin command
        await ProcessAdminCommand(request);
    }
}
```

### Real-time Chat Server

```csharp
public class ChatHandlers
{
    private readonly SocketServer _server;

    public ChatHandlers(SocketServer server)
    {
        _server = server;
    }

    [EventHandler(0x0300)]
    public void SendMessage(AuthUser connection, ChatMessage message)
    {
        Console.WriteLine($"{connection.Username}: {message.Content}");
        
        // Broadcast to all connected clients
        foreach (var conn in _server.Connections)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await conn.SendAsync(0x0301, new ChatMessage
                    {
                        Sender = connection.Username,
                        Content = message.Content,
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error broadcasting: {ex.Message}");
                }
            });
        }
    }
}
```

## Performance Considerations

- **Delegate Caching**: Handler methods are compiled to delegates for faster invocation
- **Thread Safety**: Uses `SemaphoreSlim` for send locking and `ConcurrentDictionary` for connection management
- **Binary Protocol**: System byte order protocol for optimal performance on Intel/AMD systems
- **Async/Await**: Full async operations for scalable I/O handling
- **Connection Pooling**: Efficient connection lookup with O(1) complexity

## Security Considerations

- **Error Reporting**: Use `ErrorReportingMode.Limited` or `ErrorReportingMode.None` in production
- **Input Validation**: Always validate and sanitize user input in handlers
- **Authentication**: Implement proper authentication before setting `connection.User`
- **Rate Limiting**: Consider implementing rate limiting for resource-intensive operations
- **TLS/SSL**: Consider wrapping connections with SSL/TLS for encrypted communication

## Requirements

- .NET 10.0 or higher
- Operating System: Windows, Linux, or macOS

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Note**: This is an active project. APIs may change between versions. Please check the [CHANGELOG](CHANGELOG.md) for version history and changes.