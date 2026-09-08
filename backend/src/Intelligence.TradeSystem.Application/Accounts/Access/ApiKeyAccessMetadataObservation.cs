using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Application.Accounts.Access;

public sealed record ApiKeyAccessMetadataObservation(
    ApiKeyAccessMetadataObservationStatus Status,
    ApiKeyAccessMetadata? Metadata,
    ExchangeFailure? Failure)
{
    public static ApiKeyAccessMetadataObservation Complete(ApiKeyAccessMetadata metadata) =>
        new(ApiKeyAccessMetadataObservationStatus.Complete, metadata, null);

    public static ApiKeyAccessMetadataObservation Failed(ExchangeFailure failure) =>
        new(ApiKeyAccessMetadataObservationStatus.Failed, null, failure);
}
