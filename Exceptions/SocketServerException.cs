namespace SocketServerCore.Exceptions;

public class SocketServerException : Exception
{
    public SocketServerException()
    {
    }

    public SocketServerException(string message) : base(message)
    {
    }

    public SocketServerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}