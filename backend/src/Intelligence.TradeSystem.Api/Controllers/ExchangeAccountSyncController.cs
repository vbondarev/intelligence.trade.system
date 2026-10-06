using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

/// <summary>
/// Ручная синхронизация биржевого аккаунта. Операция не входит в management API
/// <c>/api/v1/me/exchange-accounts</c> и сохраняет прежний route до отдельного решения.
/// </summary>
[ApiController]
[Route("api/v1/exchange-accounts")]
[Authorize(Policy = "TradeUser")]
public sealed class ExchangeAccountSyncController(
    IExchangeAccountSyncService syncService,
    ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpPost("{id}/sync")]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExchangeAccountResponse>> Synchronize([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return BadRequestProblem("The exchange account id must be a non-empty GUID.");
        var result = await syncService.SynchronizeAsync(currentUserContext.UserId, ExchangeAccountId.FromGuid(id), cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            ExchangeAccountSyncOutcome.Synchronized or ExchangeAccountSyncOutcome.AlreadyApplied or
                ExchangeAccountSyncOutcome.Superseded when result.Account is not null =>
                Ok(ExchangeAccountMapper.ToResponse(result.Account)),
            ExchangeAccountSyncOutcome.NotFound => NotFoundProblem(),
            ExchangeAccountSyncOutcome.AccountDisabled => Error(ApiErrorDescriptors.ExchangeAccountDisabled),
            _ => Error(ApiErrorDescriptors.ExchangeUnavailable),
        };
    }

    private ObjectResult Error(ApiErrorDescriptor descriptor, string? detail = null) =>
        StatusCode(descriptor.StatusCode, ApiProblemDetails.Create(HttpContext, descriptor, detail));
    private ObjectResult NotFoundProblem() => Error(ApiErrorDescriptors.ResourceNotFound, "The requested resource was not found.");
    private BadRequestObjectResult BadRequestProblem(string detail) => BadRequest(ApiProblemDetails.CreateValidation(HttpContext, detail));
}
