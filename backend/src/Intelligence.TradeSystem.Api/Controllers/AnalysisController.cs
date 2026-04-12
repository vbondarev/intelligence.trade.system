using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Ai;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

/// <summary>
/// HTTP surface для analysis API.
/// Предоставляет endpoint'ы snapshot- и AI-analysis с базовой валидацией HTTP-входа
/// и mapping прикладных ошибок в стабильные HTTP-ответы.
/// </summary>
[ApiController]
[Route("api/analysis")]
public sealed class AnalysisController : ControllerBase
{
    private readonly ILlmAnalyticsService _llmAnalyticsService;
    private readonly IMarketAnalysisService _marketAnalysisService;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AnalysisController"/>.
    /// </summary>
    /// <param name="marketAnalysisService">Сервис построения финального <see cref="MarketAnalysisSnapshot"/>.</param>
    /// <param name="llmAnalyticsService">Сервис AI-анализа поверх готового <see cref="MarketAnalysisSnapshot"/>.</param>
    /// <exception cref="ArgumentNullException">Если любая из зависимостей равна <c>null</c>.</exception>
    public AnalysisController(
        IMarketAnalysisService marketAnalysisService,
        ILlmAnalyticsService llmAnalyticsService)
    {
        _marketAnalysisService = marketAnalysisService ?? throw new ArgumentNullException(nameof(marketAnalysisService));
        _llmAnalyticsService = llmAnalyticsService ?? throw new ArgumentNullException(nameof(llmAnalyticsService));
    }

    /// <summary>
    /// Выполняет snapshot-analysis и возвращает готовый <see cref="MarketAnalysisSnapshot"/>.
    /// </summary>
    [HttpPost("snapshot")]
    [ProducesResponseType(typeof(MarketAnalysisSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MarketAnalysisSnapshot>> Snapshot(
        [FromBody] SnapshotAnalysisRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateSnapshotRequest(request, out var exchangeId, out var symbol, out var category, out var validationProblem))
        {
            return validationProblem!;
        }

        try
        {
            var snapshot = await _marketAnalysisService.BuildSnapshotAsync(
                exchangeId,
                symbol,
                category,
                cancellationToken).ConfigureAwait(false);

            return Ok(snapshot);
        }
        catch (ArgumentException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Snapshot analysis is temporarily unavailable.",
                detail: exception.Message);
        }
    }

    /// <summary>
    /// Выполняет AI-analysis поверх готового <see cref="MarketAnalysisSnapshot"/>.
    /// </summary>
    [HttpPost("ai")]
    [ProducesResponseType(typeof(AiAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AiAnalysisResponse>> Ai(
        [FromBody] AiAnalysisRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateAiRequest(request, out var exchangeId, out var symbol, out var category, out var userQuery, out var validationProblem))
        {
            return validationProblem!;
        }

        try
        {
            var snapshot = await _marketAnalysisService.BuildSnapshotAsync(
                exchangeId,
                symbol,
                category,
                cancellationToken).ConfigureAwait(false);

            var analysis = await _llmAnalyticsService.AnalyzeAsync(
                snapshot,
                userQuery,
                cancellationToken).ConfigureAwait(false);

            return Ok(new AiAnalysisResponse
            {
                Exchange = snapshot.Exchange,
                Symbol = snapshot.Symbol,
                Category = snapshot.Category,
                Analysis = analysis,
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "AI provider request failed.",
                detail: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI analysis is temporarily unavailable.",
                detail: exception.Message);
        }
    }

    private BadRequestObjectResult BadRequestProblem(string detail) =>
        BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request validation failed.",
            Detail = detail,
        });

    private bool TryValidateSnapshotRequest(
        SnapshotAnalysisRequest? request,
        out ExchangeId exchangeId,
        out string symbol,
        out MarketCategory category,
        out BadRequestObjectResult? validationProblem)
    {
        if (request is null)
        {
            exchangeId = default;
            symbol = string.Empty;
            category = default;
            validationProblem = BadRequestProblem("Snapshot request body is required.");
            return false;
        }

        if (!TryParseExchange(request.Exchange, out exchangeId, out var exchangeError))
        {
            symbol = string.Empty;
            category = default;
            validationProblem = BadRequestProblem(exchangeError!);
            return false;
        }

        if (!TryNormalizeRequiredString(request.Symbol, "symbol", out symbol, out var symbolError))
        {
            category = default;
            validationProblem = BadRequestProblem(symbolError!);
            return false;
        }

        if (!TryParseCategory(request.Category, out category, out var categoryError))
        {
            validationProblem = BadRequestProblem(categoryError!);
            return false;
        }

        validationProblem = null;
        return true;
    }

    private bool TryValidateAiRequest(
        AiAnalysisRequest? request,
        out ExchangeId exchangeId,
        out string symbol,
        out MarketCategory category,
        out string userQuery,
        out BadRequestObjectResult? validationProblem)
    {
        if (request is null)
        {
            exchangeId = default;
            symbol = string.Empty;
            category = default;
            userQuery = string.Empty;
            validationProblem = BadRequestProblem("AI analysis request body is required.");
            return false;
        }

        if (!TryValidateSnapshotRequest(request is null
                ? null
                : new SnapshotAnalysisRequest
                {
                    Exchange = request.Exchange,
                    Symbol = request.Symbol,
                    Category = request.Category,
                },
            out exchangeId,
            out symbol,
            out category,
            out validationProblem))
        {
            userQuery = string.Empty;
            return false;
        }

        if (!TryNormalizeRequiredString(request!.UserQuery, "userQuery", out userQuery, out var userQueryError))
        {
            validationProblem = BadRequestProblem(userQueryError!);
            return false;
        }

        validationProblem = null;
        return true;
    }

    private static bool TryNormalizeRequiredString(string? value, string fieldName, out string normalized, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = string.Empty;
            error = $"Field '{fieldName}' is required.";
            return false;
        }

        normalized = value.Trim();
        error = null;
        return true;
    }

    private static bool TryParseExchange(string? value, out ExchangeId exchangeId, out string? error)
    {
        if (!TryNormalizeRequiredString(value, "exchange", out var normalized, out error))
        {
            exchangeId = default;
            return false;
        }

        if (!Enum.TryParse(normalized, ignoreCase: true, out exchangeId) || !Enum.IsDefined(exchangeId))
        {
            error = $"Field 'exchange' value '{normalized}' is not supported.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseCategory(string? value, out MarketCategory category, out string? error)
    {
        if (!TryNormalizeRequiredString(value, "category", out var normalized, out error))
        {
            category = default;
            return false;
        }

        if (!Enum.TryParse(normalized, ignoreCase: true, out category) || !Enum.IsDefined(category))
        {
            error = $"Field 'category' value '{normalized}' is not supported.";
            return false;
        }

        error = null;
        return true;
    }
}



