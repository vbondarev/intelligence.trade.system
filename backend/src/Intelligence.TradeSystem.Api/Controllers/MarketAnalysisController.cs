using FluentValidation;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.MarketFacts;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Application;
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
    private readonly IValidator<SnapshotAnalysisRequest> _snapshotRequestValidator;
    private readonly IValidator<LlmPayloadRequest> _llmPayloadRequestValidator;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="MarketAnalysisController"/>.
    /// </summary>
    /// <param name="marketAnalysisService">Сервис построения агрегированного рыночного снимка.</param>
    /// <param name="snapshotHealthEvaluator">Сервис оценки свежести снапшота.</param>
    /// <param name="snapshotRequestValidator">Валидатор запроса снапшота.</param>
    /// <param name="llmPayloadRequestValidator">Валидатор запроса LLM-payload.</param>
    /// <exception cref="ArgumentNullException">Если любая из зависимостей равна <c>null</c>.</exception>
    public MarketAnalysisController(
        IMarketAnalysisService marketAnalysisService,
        ISnapshotHealthEvaluator snapshotHealthEvaluator,
        IValidator<SnapshotAnalysisRequest> snapshotRequestValidator,
        IValidator<LlmPayloadRequest> llmPayloadRequestValidator)
    {
        _marketAnalysisService = marketAnalysisService;
        _snapshotHealthEvaluator = snapshotHealthEvaluator;
        _snapshotRequestValidator = snapshotRequestValidator;
        _llmPayloadRequestValidator = llmPayloadRequestValidator;
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
        if (request is null)
        {
            return BadRequestProblem("Snapshot request body is required.");
        }

        var validationResult = await _snapshotRequestValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BadRequestProblem(validationResult.Errors[0].ErrorMessage);
        }

        try
        {
            var snapshot = await _marketAnalysisService.BuildSnapshotAsync(
                request.Exchange!.Value,
                request.Symbol!.Trim(),
                request.Category!.Value,
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
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequestProblem("Field 'symbol' is required.");
        }

        var validationResult = await _llmPayloadRequestValidator
            .ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            return BadRequestProblem(validationResult.Errors[0].ErrorMessage);
        }

        var mode = request.Mode ?? AnalysisMode.Intraday;
        var normalizedSymbol = symbol.Trim();

        try
        {
            var snapshot = await _marketAnalysisService.BuildSnapshotAsync(
                request.Exchange!.Value,
                normalizedSymbol,
                request.Category!.Value,
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

    /// <summary>
    /// Возвращает нормализованный слой рыночных фактов по указанному инструменту.
    /// Payload предназначен для downstream-агентов и deterministic validation.
    /// Не является текстовым LLM-анализом и не содержит готового торгового решения.
    /// <para>
    /// <c>market-facts</c> — canonical facts layer со схемой <c>market-facts/v1</c>.
    /// Содержит детерминированные поля: <c>dataQuality.status</c>, <c>tradeFlow.direction</c>,
    /// <c>tradeFlow.label</c>, таймфреймы и агрегированные уровни поддержки/сопротивления.
    /// </para>
    /// </summary>
    /// <param name="symbol">Тикер торгового инструмента, например <c>BTCUSDT</c>.</param>
    /// <param name="request">Query-параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>
    /// HTTP 200 с <see cref="MarketFactsPayload"/>, если market facts успешно построены;
    /// иначе один из стандартных problem-details ответов.
    /// </returns>
    [HttpGet("{symbol}/market-facts")]
    [ProducesResponseType(typeof(MarketFactsPayload), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MarketFactsPayload>> MarketFacts(
        [FromRoute] string? symbol,
        [FromQuery] LlmPayloadRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return BadRequestProblem("Field 'symbol' is required.");
        }

        var validationResult = await _llmPayloadRequestValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BadRequestProblem(validationResult.Errors[0].ErrorMessage);
        }

        var mode = request.Mode ?? AnalysisMode.Intraday;
        var normalizedSymbol = symbol.Trim();

        try
        {
            var snapshot = await _marketAnalysisService.BuildSnapshotAsync(
                request.Exchange!.Value,
                normalizedSymbol,
                request.Category!.Value,
                cancellationToken);

            var health = _snapshotHealthEvaluator.Evaluate(snapshot, mode);
            var payload = snapshot.ToMarketFacts(mode, health);

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
                title: "Market facts analysis is temporarily unavailable.",
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
}
