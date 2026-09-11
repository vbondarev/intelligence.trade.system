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
        BasePolicyConfigurationVersion = assessment.InputVersions.BasePolicyConfigurationIdentity.Version,
        BasePolicyConfigurationHash = assessment.InputVersions.BasePolicyConfigurationIdentity.Hash,
        PolicyConfigurationVersion = assessment.PolicyConfigurationIdentity.Version,
        PolicyConfigurationHash = assessment.PolicyConfigurationIdentity.Hash,
        ResultJson = assessment.Result.IsLegacy
            ? null
            : JsonSerializer.Serialize(
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

        var id = PositionAssessmentId.FromGuid(entity.Id);
        var inputVersions = new PositionAssessmentInputVersions(
            PositionId.FromGuid(entity.PositionId),
            ExchangeAccountId.FromGuid(entity.ExchangeAccountId),
            InstrumentId.From(entity.InstrumentId),
            PersistenceDateTime.ToUtc(entity.PositionObservedAt),
            PersistenceDateTime.ToUtc(entity.PortfolioCalculatedAt),
            PersistenceDateTime.ToUtc(entity.MarketCapturedAt),
            PolicyConfigurationIdentity.From(
                entity.BasePolicyConfigurationVersion,
                entity.BasePolicyConfigurationHash),
            PolicyConfigurationIdentity.From(
                entity.PolicyConfigurationVersion,
                entity.PolicyConfigurationHash));
        var ruleVersion = RuleVersion.From(entity.RuleVersion);
        var createdAt = PersistenceDateTime.ToUtc(entity.CreatedAt);
        var validUntil = PersistenceDateTime.ToUtc(entity.ValidUntil);
        var orderedReasons = reasons.OrderBy(reason => reason.Sequence)
            .Select(reason => reason.ReasonCode);

        if (string.IsNullOrWhiteSpace(entity.ResultJson))
            return PositionAssessment.Restore(
                id,
                inputVersions,
                ruleVersion,
                createdAt,
                validUntil,
                entity.PortfolioRiskDecision,
                orderedReasons);

        using var document = JsonDocument.Parse(entity.ResultJson);
        if (IsLegacyPayload(document.RootElement))
            return PositionAssessment.Restore(
                id,
                inputVersions,
                ruleVersion,
                createdAt,
                validUntil,
                entity.PortfolioRiskDecision,
                orderedReasons);

        var result = DeserializeStructuredResult(entity.ResultJson, entity.Id);
        return PositionAssessment.Restore(
            id,
            inputVersions,
            ruleVersion,
            createdAt,
            validUntil,
            entity.PortfolioRiskDecision,
            result,
            orderedReasons);
    }

    private static PositionAssessmentResult DeserializeStructuredResult(
        string json,
        Guid assessmentId)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("schemaVersion", out _))
        {
            var persisted = JsonSerializer.Deserialize<PositionAssessmentResultDocument>(
                json,
                PositionAssessmentJson.Options)
                ?? throw new InvalidOperationException(
                    $"Position assessment {assessmentId} contains an empty result payload.");
            if (persisted.SchemaVersion is not
                (PositionAssessmentResultDocument.LegacySchemaVersion or
                 PositionAssessmentResultDocument.CurrentSchemaVersion))
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

    private static bool IsLegacyPayload(JsonElement root)
    {
        if (root.TryGetProperty("isLegacy", out var isLegacy) &&
            isLegacy.ValueKind == JsonValueKind.True)
            return true;

        if (!root.TryGetProperty("dataQuality", out var dataQuality) ||
            !dataQuality.TryGetProperty("safetyState", out var safetyState))
            return false;

        return safetyState.ValueKind switch
        {
            JsonValueKind.String =>
                string.Equals(safetyState.GetString(), "notEvaluated", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => safetyState.TryGetInt32(out var value) && value == 0,
            _ => false,
        };
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
    public const int LegacySchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;
}
