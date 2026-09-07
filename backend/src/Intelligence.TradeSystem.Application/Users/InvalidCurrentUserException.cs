namespace Intelligence.TradeSystem.Application.Users;

/// <summary>
/// Indicates that the current principal cannot be used for a user-delegated operation.
/// </summary>
public sealed class InvalidCurrentUserException(string message) : Exception(message);
