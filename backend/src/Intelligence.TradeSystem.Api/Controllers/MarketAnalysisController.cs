using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

/// <summary>
/// Обрабатывает HTTP-запросы на построение рыночного снимка, LLM-payload и AI-анализа.
/// </summary>
[ApiController]
[Route("api/market-analysis")]
public sealed class MarketAnalysisController : ControllerBase
{
    private readonly IMarketAnalysisService _marketAnalysisService;
    private readonly ISnapshotHealthEvaluator _snapshotHealthEvaluator;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="MarketAnalysisController"/>.
    /// </summary>
    /// <param name="marketAnalysisService">Сервис построения агрегированного рыночного снимка.</param>
    /// <param name="snapshotHealthEvaluator">Сервис оценки свежести снапшота.</param>
    /// <exception cref="ArgumentNullException">Если любая из зависимостей равна <c>null</c>.</exception>
    public MarketAnalysisController(
        IMarketAnalysisService marketAnalysisService,
        ISnapshotHealthEvaluator snapshotHealthEvaluator)
    {
        _marketAnalysisService = marketAnalysisService;
        _snapshotHealthEvaluator = snapshotHealthEvaluator;
    }

    /// <summary>
    /// Строит рыночный снимок по указанному инструменту.
    /// </summary>
    /// <param name="request">Параметры инструмента и рынка, для которых нужно построить снимок.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>
    /// HTTP 200 с <see cref="MarketAnalysisResponse"/>, если снимок успешно построен;
    /// иначе один из стандартных problem-details ответов.
    /// </returns>
    [HttpPost("snapshot")]
    [ProducesResponseType(typeof(MarketAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MarketAnalysisResponse>> Snapshot(
        [FromBody] SnapshotAnalysisRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateSnapshotRequest(request, out var exchangeId, out var symbol, out var category,
                out var validationProblem))
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

            return Ok(snapshot.ToResponse());
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
    /// Возвращает LLM-оптимизированный payload по указанному инструменту.
    /// Содержит только сигнальные данные, пригодные как прямой вход для GPT / Qwen / DeepSeek.
    /// </summary>
    /// <param name="symbol">Тикер торгового инструмента, например <c>BTCUSDT</c>.</param>
    /// <param name="request">Query-параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>
    /// HTTP 200 с <see cref="LlmMarketAnalysisPayload"/>, если payload успешно построен;
    /// иначе один из стандартных problem-details ответов.
    /// </returns>
    [HttpGet("{symbol}/llm-payload")]
    [ProducesResponseType(typeof(LlmMarketAnalysisPayload), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LlmMarketAnalysisPayload>> LlmPayload(
        [FromRoute] string? symbol,
        [FromQuery] LlmPayloadRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidateLlmPayloadRequest(symbol, request, out var exchangeId, out var normalizedSymbol,
                out var category, out var mode, out var validationProblem))
        {
            return validationProblem!;
        }

        try
        {
            var snapshot = await _marketAnalysisService.BuildSnapshotAsync(
                exchangeId,
                normalizedSymbol,
                category,
                cancellationToken).ConfigureAwait(false);

            var health = _snapshotHealthEvaluator.Evaluate(snapshot, mode);
            var payload = snapshot.ToLlmPayload(mode, health);

            return Ok(payload);
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
                title: "LLM payload analysis is temporarily unavailable.",
                detail: exception.Message);
        }
    }

    // ─── Validation ─────────────────────────────────────────────────────────

    private BadRequestObjectResult BadRequestProblem(string detail) =>
        BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request validation failed.",
            Detail = detail,
        });

    private bool TryValidateLlmPayloadRequest(
        string? symbol,
        LlmPayloadRequest request,
        out ExchangeId exchangeId,
        out string normalizedSymbol,
        out MarketCategory category,
        out AnalysisMode mode,
        out BadRequestObjectResult? validationProblem)
    {
        mode = request.Mode ?? AnalysisMode.Intraday;

        if (!TryNormalizeRequiredString(symbol, "symbol", out normalizedSymbol, out var symbolError))
        {
            exchangeId = default;
            category = default;
            validationProblem = BadRequestProblem(symbolError!);
            return false;
        }

        if (request.Exchange is null)
        {
            exchangeId = default;
            category = default;
            validationProblem = BadRequestProblem("Field 'exchange' is required.");
            return false;
        }

        exchangeId = request.Exchange.Value;

        if (request.Category is null)
        {
            category = default;
            validationProblem = BadRequestProblem("Field 'category' is required.");
            return false;
        }

        category = request.Category.Value;
        validationProblem = null;

        return true;
    }

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

        if (request.Exchange is null)
        {
            exchangeId = default;
            symbol = string.Empty;
            category = default;
            validationProblem = BadRequestProblem("Field 'exchange' is required.");
            return false;
        }

        exchangeId = request.Exchange.Value;

        if (!TryNormalizeRequiredString(request.Symbol, "symbol", out symbol, out var symbolError))
        {
            category = default;
            validationProblem = BadRequestProblem(symbolError!);
            return false;
        }

        if (request.Category is null)
        {
            category = default;
            validationProblem = BadRequestProblem("Field 'category' is required.");
            return false;
        }

        category = request.Category.Value;

        validationProblem = null;
        return true;
    }


    private static bool TryNormalizeRequiredString(string? value, string fieldName, out string normalized,
        out string? error)
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
}
