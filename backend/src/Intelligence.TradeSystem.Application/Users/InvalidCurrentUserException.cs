namespace Intelligence.TradeSystem.Application.Users;

/// <summary>
/// Указывает, что текущий субъект не может использоваться для операции от имени пользователя.
/// </summary>
public sealed class InvalidCurrentUserException(string message) : Exception(message);
