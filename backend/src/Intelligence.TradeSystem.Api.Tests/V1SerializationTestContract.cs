namespace Intelligence.TradeSystem.Api.Contracts.V1.Testing;

public enum ContractState
{
    WaitingForReview,
}

public sealed record V1SerializationItem(
    Guid Id,
    ContractState State,
    string? OptionalValue,
    DateTimeOffset CapturedAt);
