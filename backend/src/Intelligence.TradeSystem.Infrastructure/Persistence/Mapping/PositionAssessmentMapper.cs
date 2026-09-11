using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PositionAssessmentMapper
{
    public static PositionAssessmentEntity ToEntity(PositionAssessment assessment) => new()
    {
        Id = assessment.Id.Value,
        PositionId = assessment.InputVersions.PositionId.Value,
        ExchangeAccountId = assessment.InputVersions.ExchangeAccountId.Value,
        InstrumentId = assessment.InputVersions.InstrumentId.Value,
        PositionObservedAt = PersistenceDateTime.ToUtc(assessment.InputVersions.PositionObservedAt),
        PortfolioCalculatedAt = PersistenceDateTime.ToUtc(assessment.InputVersions.PortfolioCalculatedAt),
        MarketCapturedAt = PersistenceDateTime.ToUtc(assessment.InputVersions.MarketCapturedAt),
        RuleVersion = assessment.RuleVersion.Value,
        PolicyConfigurationVersion = assessment.PolicyConfigurationIdentity.Version,
        PolicyConfigurationHash = assessment.PolicyConfigurationIdentity.Hash,
        ResultJson = JsonSerializer.Serialize(
            new PositionAssessmentResultDocument(
                PositionAssessmentResultDocument.CurrentSchemaVersion,
                assessment.Result),
            PositionAssessmentJson.Options),
        CreatedAt = PersistenceDateTime.ToUtc(assessment.CreatedAt),
        ValidUntil = PersistenceDateTime.ToUtc(assessment.ValidUntil),
        PortfolioRiskDecision = assessment.PortfolioRiskDecision,
    };

    public static IReadOnlyList<PositionAssessmentReasonEntity> ToReasonEntities(
        PositionAssessment assessment) =>
        assessment.ReasonCodes
            .Select((reason, index) => new PositionAssessmentReasonEntity
            {
                PositionAssessmentId = assessment.Id.Value,
                Sequence = index + 1,
                ReasonCode = reason,
            })
            .ToArray();

    public static PositionAssessment ToDomain(
        PositionAssessmentEntity entity,
        IReadOnlyCollection<PositionAssessmentReasonEntity> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        var result = string.IsNullOrWhiteSpace(entity.ResultJson)
            ? PositionAssessmentResult.Legacy(entity.PortfolioRiskDecision)
            : DeserializeResult(entity.ResultJson, entity.Id);

        return PositionAssessment.Restore(
            PositionAssessmentId.FromGuid(entity.Id),
            new PositionAssessmentInputVersions(
                PositionId.FromGuid(entity.PositionId),
                ExchangeAccountId.FromGuid(entity.ExchangeAccountId),
                InstrumentId.From(entity.InstrumentId),
                PersistenceDateTime.ToUtc(entity.PositionObservedAt),
                PersistenceDateTime.ToUtc(entity.PortfolioCalculatedAt),
                PersistenceDateTime.ToUtc(entity.MarketCapturedAt),
                PolicyConfigurationIdentity.From(
                    entity.PolicyConfigurationVersion,
                    entity.PolicyConfigurationHash)),
            RuleVersion.From(entity.RuleVersion),
            PersistenceDateTime.ToUtc(entity.CreatedAt),
            PersistenceDateTime.ToUtc(entity.ValidUntil),
            entity.PortfolioRiskDecision,
            result,
            reasons.OrderBy(reason => reason.Sequence).Select(reason => reason.ReasonCode));
    }

    private static PositionAssessmentResult DeserializeResult(string json, Guid assessmentId)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("schemaVersion", out _))
        {
            var persisted = JsonSerializer.Deserialize<PositionAssessmentResultDocument>(
                json,
                PositionAssessmentJson.Options)
                ?? throw new InvalidOperationException(
                    $"Position assessment {assessmentId} contains an empty result payload.");
            if (persisted.SchemaVersion != PositionAssessmentResultDocument.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    $"Position assessment {assessmentId} uses unsupported result schema " +
                    $"{persisted.SchemaVersion}.");
            return persisted.Result;
        }

        return JsonSerializer.Deserialize<PositionAssessmentResult>(
                   json,
                   PositionAssessmentJson.Options)
               ?? throw new InvalidOperationException(
                   $"Position assessment {assessmentId} contains an empty result payload.");
    }
}

internal static class PositionAssessmentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

internal sealed record PositionAssessmentResultDocument(
    int SchemaVersion,
    PositionAssessmentResult Result)
{
    public const int CurrentSchemaVersion = 1;
}
