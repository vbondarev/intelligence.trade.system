using FluentValidation;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Application.Market;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

/// <summary>
/// Обрабатывает HTTP-запросы на построение рыночного снимка, LLM-payload и AI-анализа.
/// </summary>
[ApiController]
[Route("api/market-analysis")]
public sealed class MarketAnalysisController : ControllerBase
{
    private readonly IMarketSnapshotService _marketSnapshotService;
    private readonly ISnapshotHealthEvaluator _snapshotHealthEvaluator;
    private readonly IValidator<SnapshotAnalysisRequest> _snapshotRequestValidator;
    private readonly IValidator<LlmPayloadRequest> _llmPayloadRequestValidator;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="MarketAnalysisController"/>.
    /// </summary>
    /// <param name="marketSnapshotService">Сервис построения агрегированного рыночного снимка.</param>
    /// <param name="snapshotHealthEvaluator">Сервис оценки свежести снапшота.</param>
    /// <param name="snapshotRequestValidator">Валидатор запроса снапшота.</param>
    /// <param name="llmPayloadRequestValidator">Валидатор запроса LLM-payload.</param>
    /// <exception cref="ArgumentNullException">Если любая из зависимостей равна <c>null</c>.</exception>
    public MarketAnalysisController(
        IMarketSnapshotService marketSnapshotService,
        ISnapshotHealthEvaluator snapshotHealthEvaluator,
        IValidator<SnapshotAnalysisRequest> snapshotRequestValidator,
        IValidator<LlmPayloadRequest> llmPayloadRequestValidator)
    {
        _marketSnapshotService = marketSnapshotService;
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
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

        var snapshot = await _marketSnapshotService.BuildSnapshotAsync(
            request.Exchange!.Value,
            request.Symbol!.Trim(),
            request.Category!.Value,
            cancellationToken).ConfigureAwait(false);

        return Ok(snapshot.ToResponse(PortfolioSnapshot.Unavailable));
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
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

        var snapshot = await _marketSnapshotService.BuildSnapshotAsync(
            request.Exchange!.Value,
            normalizedSymbol,
            request.Category!.Value,
            cancellationToken).ConfigureAwait(false);

        var health = _snapshotHealthEvaluator.Evaluate(snapshot, mode);
        var payload = snapshot.ToLlmPayload(mode, health);

        return Ok(payload);
    }

    private BadRequestObjectResult BadRequestProblem(string detail) =>
        BadRequest(ApiProblemDetails.CreateValidation(HttpContext, detail));
}
