using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/exchange-accounts")]
[Authorize(Policy = "TradeUser")]
public sealed class ExchangeAccountsController(
    IExchangeAccountService accountService,
    IExchangeAccountSyncService syncService,
    ICurrentUserContext currentUserContext)
    : ControllerBase
{
    [HttpPost("bybit")]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExchangeAccountResponse>> ConnectBybit(
        [FromBody] ConnectExchangeAccountRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequestProblem("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequestProblem("Field 'apiKey' is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiSecret))
        {
            return BadRequestProblem("Field 'apiSecret' is required.");
        }

        var credentials = new ExchangeAccountCredentialSecret(
            request.ApiKey.Trim(),
            request.ApiSecret.Trim());
        var result = await accountService
            .ConnectAsync(ExchangeId.Bybit, credentials, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            ExchangeAccountConnectionOutcome.Connected when result.Account is not null =>
                Ok(ToResponse(result.Account)),
            ExchangeAccountConnectionOutcome.InvalidCredentials =>
                Error(ApiErrorDescriptors.ExchangeCredentialsInvalid),
            ExchangeAccountConnectionOutcome.PermissionsRejected =>
                Error(ApiErrorDescriptors.ExchangePermissionsRejected),
            ExchangeAccountConnectionOutcome.UnsupportedExchange =>
                Error(ApiErrorDescriptors.ValidationFailed, "The exchange is not supported."),
            _ => Error(ApiErrorDescriptors.ExchangeUnavailable),
        };
    }

    [HttpDelete("{accountId:guid}")]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExchangeAccountResponse>> Disconnect(
        [FromRoute] Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await accountService
            .DisconnectAsync(
                ExchangeAccountId.FromGuid(accountId),
                cancellationToken)
            .ConfigureAwait(false);

        return account is null
            ? NotFound()
            : Ok(ToResponse(account));
    }

    [HttpPost("{accountId:guid}/sync")]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExchangeAccountResponse>> Synchronize(
        [FromRoute] Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = await syncService
            .SynchronizeAsync(
                currentUserContext.UserId,
                ExchangeAccountId.FromGuid(accountId),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            ExchangeAccountSyncOutcome.Synchronized when result.Account is not null =>
                Ok(ToResponse(result.Account)),
            ExchangeAccountSyncOutcome.AlreadyApplied or
                ExchangeAccountSyncOutcome.Superseded when result.Account is not null =>
                Ok(ToResponse(result.Account)),
            ExchangeAccountSyncOutcome.NotFound => NotFound(),
            ExchangeAccountSyncOutcome.AccountDisabled =>
                Error(ApiErrorDescriptors.ExchangeAccountDisabled),
            ExchangeAccountSyncOutcome.CredentialsUnavailable or
                ExchangeAccountSyncOutcome.ExchangeUnavailable =>
                Error(ApiErrorDescriptors.ExchangeUnavailable),
            _ => Error(ApiErrorDescriptors.InternalError),
        };
    }

    private ObjectResult Error(ApiErrorDescriptor descriptor, string? detail = null) =>
        StatusCode(descriptor.StatusCode, ApiProblemDetails.Create(HttpContext, descriptor, detail));

    private BadRequestObjectResult BadRequestProblem(string detail) =>
        BadRequest(ApiProblemDetails.CreateValidation(HttpContext, detail));

    private static ExchangeAccountResponse ToResponse(ExchangeAccount account) =>
        new(
            account.Id.Value,
            account.ExchangeId,
            account.ConnectionStatus,
            Enum.GetValues<ExchangeAccountCapabilities>()
                .Where(capability =>
                    capability != ExchangeAccountCapabilities.None &&
                    account.Capabilities.HasFlag(capability))
            .ToArray())
        {
            LastSyncedAt = account.LastSyncedAt,
        };
}
