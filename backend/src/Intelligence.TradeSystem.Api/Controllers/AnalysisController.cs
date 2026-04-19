using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Ai;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

/// <summary>
/// Обрабатывает HTTP-запросы на построение рыночного снимка и AI-анализа.
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
    /// <param name="marketAnalysisService">Сервис построения агрегированного рыночного снимка.</param>
    /// <param name="llmAnalyticsService">Сервис построения текстового AI-анализа по готовому рыночному снимку.</param>
    /// <exception cref="ArgumentNullException">Если любая из зависимостей равна <c>null</c>.</exception>
    public AnalysisController(
        IMarketAnalysisService marketAnalysisService,
        ILlmAnalyticsService llmAnalyticsService)
    {
        _marketAnalysisService = marketAnalysisService ?? throw new ArgumentNullException(nameof(marketAnalysisService));
        _llmAnalyticsService = llmAnalyticsService ?? throw new ArgumentNullException(nameof(llmAnalyticsService));
    }

    /// <summary>
    /// Строит рыночный снимок по указанному инструменту.
    /// </summary>
    /// <param name="request">Параметры инструмента и рынка, для которых нужно построить снимок.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>
    /// HTTP 200 с <see cref="MarketAnalysisResponse"/>, если снимок успешно построен;
    /// иначе один из стандартных problem-details ответов. Если <c>exchange</c> содержит невалидное enum-значение,
    /// ASP.NET Core возвращает стандартный <c>400 ValidationProblemDetails</c> до выполнения метода.
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
    /// Выполняет AI-анализ по указанному инструменту и пользовательскому запросу.
    /// </summary>
    /// <param name="request">Параметры инструмента и текст запроса к AI-анализу.</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса.</param>
    /// <returns>
    /// HTTP 200 с <see cref="AiAnalysisResponse"/>, если AI-анализ успешно построен;
    /// иначе один из стандартных problem-details ответов. Если <c>exchange</c> содержит невалидное enum-значение,
    /// ASP.NET Core возвращает стандартный <c>400 ValidationProblemDetails</c> до выполнения метода.
    /// </returns>
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

        if (!TryValidateSnapshotRequest(new SnapshotAnalysisRequest
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



