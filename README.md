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
- **Broadcast Messaging**: Easy-to-use APIs for broadcasting to all or specific clients
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

### 1. Create a Simple Server

```csharp
using SocketServerCore;
using SocketServerCore.Attributes;
using SocketServerCore.Serialization;

// Define your request/response types
public record LoginRequest(string Username, string Password);
public record LoginResponse(bool Success, string Message);
public record ChatMessage(string Sender, string Content);

// Define your handlers
public class MyServerHandlers
{
    [EventHandler(0x0001)]
    public LoginResponse Login(SocketConnection connection, LoginRequest request)
    {
        // Your authentication logic here
        bool isValid = ValidateUser(request.Username, request.Password);
        
        if (isValid)
        {
            connection.User = new AuthenticatedUser 
            { 
                Username = request.Username 
            };
        }
        
        return new LoginResponse(isValid, isValid ? "Welcome!" : "Invalid credentials");
    }

    [EventHandler(0x0002)]
    public async Task ChatMessage(AuthenticatedUser connection, ChatMessage message)
    {
        // Broadcast to all connected clients
        await _server.BroadcastAsync(0x0003, message);
    }
}
```

### 2. Start the Server

```csharp
var server = new SocketServerBuilder()
    .WithPort(8080)
    .WithSerializer<JsonSerializer>() // or use custom serializer
    .WithHandler<MyServerHandlers>()
    .Build();

await server.StartAsync();
Console.WriteLine("Server started on port 8080");
```

### 3. Connect and Send Messages

The server uses a binary protocol with the following format:
- **Event ID**: 2 bytes (ushort) - identifies the message type
- **Payload Length**: 4 bytes (int) - length of the serialized payload  
- **Payload**: N bytes - serialized data

## Usage

### Handler Registration

Handlers are registered using the `[EventHandler(ushort eventId)]` attribute:

```csharp
public class GameHandlers
{
    // Public handler - anyone can call
    [EventHandler(0x0100)]
    public ConnectionInfo Connect(SocketConnection connection, ConnectRequest request)
    {
        var user = new AuthenticatedUser { Username = request.Username };
        connection.User = user;
        return new ConnectionInfo(user.Username, GetServerTime());
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
        connection.LastActivity = DateTime.UtcNow;
    }
}
```

### Server Configuration

```csharp
var server = new SocketServerBuilder()
    .WithPort(8080)                    // Set server port
    .WithSerializer<JsonSerializer>()   // Set serialization format
    .WithHandler<GameHandlers>()        // Add handler class
    .WithHandler<ChatHandlers>()        // Add multiple handler classes
    .WithMaxConnections(1000)           // Set max concurrent connections
    .WithBufferSize(8192)               // Set buffer size
    .Build();
```

### Connection Management

```csharp
// Get all connections
var allConnections = server.Connections;

// Send to specific client
await server.SendAsync(connectionId, eventId, data);

// Broadcast to all clients
await server.BroadcastAsync(eventId, data);

// Get specific connection
if (server.TryGetConnection(connectionId, out var connection))
{
    await connection.SendAsync(eventId, data);
}

// Disconnect client
await server.DisconnectAsync(connectionId);
```

### Custom Serialization

Implement the `ISerializer` interface for custom serialization:

```csharp
public class MessagePackSerializer : ISerializer
{
    public T Deserialize<T>(byte[] data)
    {
        return MessagePackSerializer.Deserialize<T>(data);
    }

    public byte[] Serialize<T>(T obj)
    {
        return MessagePackSerializer.Serialize(obj);
    }
}

// Use in server builder
var server = new SocketServerBuilder()
    .WithSerializer<MessagePackSerializer>()
    .Build();
```

## Event ID Conventions

Event IDs are `ushort` values (0x0000-0xFFFF) organized by functionality:

- **0x0000-0x00FF**: System/Core operations
- **0x0100-0x01FF**: Authentication/Authorization
- **0x0200-0x02FF**: Game-specific operations
- **0x0300-0x03FF**: Chat/Messaging
- **0x0400-0xFFFF**: Application-specific

## API Reference

### Core Classes

#### SocketServer
Main server class that manages TCP listener, client connections, and message routing.

**Methods:**
- `StartAsync()` - Start the server
- `StopAsync()` - Stop the server
- `SendAsync(Guid connectionId, ushort eventId, object data)` - Send to specific client
- `BroadcastAsync(ushort eventId, object data)` - Broadcast to all clients
- `DisconnectAsync(Guid connectionId)` - Disconnect a client

#### SocketConnection
Represents individual client connections with send/receive capabilities.

**Properties:**
- `Id` - Unique connection identifier
- `User` - Authenticated user object
- `Connected` - Connection status
- `LastActivity` - Timestamp of last activity

**Methods:**
- `SendAsync<T>(ushort eventId, T data)` - Send message to client
- `DisconnectAsync()` - Close the connection

#### HandlerRegistry
Central handler discovery and invocation system using reflection and delegates.

## Advanced Examples

### Authentication System

```csharp
public class AuthUser 
{
    public string Username { get; set; }
    public string[] Roles { get; set; }
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
    public void AdminCommand(AuthUser connection, AdminRequest request)
    {
        // Only accessible if connection.User is AuthUser
        if (!connection.Roles.Contains("Admin"))
        {
            throw new UnauthorizedAccessException("Admin access required");
        }
        
        // Process admin command
    }
}
```

### Real-time Game Server

```csharp
public class GameHandlers
{
    private readonly GameState _gameState = new();

    [EventHandler(0x0200)]
    public PlayerState JoinGame(AuthUser connection, JoinRequest request)
    {
        var player = _gameState.AddPlayer(connection.Username, request.Position);
        return player;
    }

    [EventHandler(0x0201)]
    public async Task<GameUpdate> MovePlayer(AuthUser connection, MoveRequest request)
    {
        var player = _gameState.MovePlayer(connection.Username, request.Destination);
        
        // Broadcast movement to all players
        await _server.BroadcastAsync(0x0202, new PlayerMovedEvent
        {
            PlayerId = connection.Username,
            Position = request.Destination
        });
        
        return player;
    }

    [EventHandler(0x0203)]
    public void PlayerAction(AuthUser connection, ActionRequest request)
    {
        _gameState.ProcessAction(connection.Username, request.Action);
    }
}
```

## Performance Considerations

- **Delegate Caching**: Handler methods are compiled to delegates for faster invocation
- **Thread Safety**: Uses `SemaphoreSlim` for send locking and `ConcurrentDictionary` for connection management
- **Buffer Management**: Configurable buffer sizes for optimal memory usage
- **Connection Pooling**: Efficient connection lookup with O(1) complexity
- **Binary Protocol**: System byte order protocol for optimal performance on Intel/AMD systems

## Requirements

- .NET 10.0 or higher
- Operating System: Windows, Linux, or macOS

## Documentation

For detailed documentation and API reference, please visit our [Wiki](https://github.com/yourusername/SocketServerCore/wiki).

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Note**: This is an active project. APIs may change between versions. Please check the [CHANGELOG](CHANGELOG.md) for version history and changes.