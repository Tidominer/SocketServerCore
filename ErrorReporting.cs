namespace SocketServerCore;

/// <summary>
/// Defines how much error information is sent to clients when handlers throw exceptions
/// </summary>
public enum ErrorReportingMode
{
    /// <summary>
    /// No error responses sent to clients
    /// </summary>
    None,

    /// <summary>
    /// Only generic "Internal Server Error" message sent
    /// </summary>
    Limited,

    /// <summary>
    /// Full error details including exception type, message, and stack trace
    /// </summary>
    Full
}

/// <summary>
/// Standard error response format for server-side exceptions
/// </summary>
public class ErrorResponse
{
    public string ErrorType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}