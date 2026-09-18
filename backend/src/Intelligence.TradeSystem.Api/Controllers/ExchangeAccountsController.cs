using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
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
[Route("api/v1/exchange-accounts")]
[Authorize(Policy = "TradeUser")]
public sealed class ExchangeAccountsController(
    IExchangeAccountService accountService,
    IExchangeAccountSyncService syncService,
    ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ExchangeAccountListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExchangeAccountListResponse>> List(CancellationToken cancellationToken) =>
        Ok(new ExchangeAccountListResponse((await accountService.ListActiveAsync(
            currentUserContext.UserId, cancellationToken).ConfigureAwait(false)).Select(ToResponse).ToArray()));

    [HttpPost]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExchangeAccountResponse>> Connect(
        [FromBody] CreateExchangeAccountRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ApiKey) || string.IsNullOrWhiteSpace(request.ApiSecret))
            return BadRequestProblem("Both apiKey and apiSecret are required.");
        if (request.Exchange != ExchangeProvider.Bybit)
            return BadRequestProblem("The exchange is not supported.");
        var result = await accountService.ConnectAsync(currentUserContext.UserId, ExchangeId.Bybit,
            new ExchangeAccountCredentialSecret(request.ApiKey.Trim(), request.ApiSecret.Trim()), cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            ExchangeAccountConnectionOutcome.Connected when result.Account is not null => StatusCode(StatusCodes.Status201Created, ToResponse(result.Account)),
            ExchangeAccountConnectionOutcome.InvalidCredentials => Error(ApiErrorDescriptors.ExchangeCredentialsInvalid),
            ExchangeAccountConnectionOutcome.PermissionsRejected => Error(ApiErrorDescriptors.ExchangePermissionsRejected),
            ExchangeAccountConnectionOutcome.UnsupportedExchange => BadRequestProblem("The exchange is not supported."),
            _ => Error(ApiErrorDescriptors.ExchangeUnavailable),
        };
    }

    [HttpPost("{id}/verify")]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<ExchangeAccountResponse>> Verify([FromRoute] Guid id, CancellationToken cancellationToken) =>
        ExecuteVerification(id, cancellationToken);

    [HttpPut("{id}/credentials")]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExchangeAccountResponse>> Rotate([FromRoute] Guid id,
        [FromBody] RotateExchangeAccountCredentialsRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ApiKey) || string.IsNullOrWhiteSpace(request.ApiSecret))
            return BadRequestProblem("Both apiKey and apiSecret are required.");
        var result = await accountService.RotateCredentialsAsync(currentUserContext.UserId, ExchangeAccountId.FromGuid(id),
            new ExchangeAccountCredentialSecret(request.ApiKey.Trim(), request.ApiSecret.Trim()), cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            ExchangeAccountCredentialRotationOutcome.Succeeded when result.Account is not null => Ok(ToResponse(result.Account)),
            ExchangeAccountCredentialRotationOutcome.NotFound => NotFoundProblem(),
            ExchangeAccountCredentialRotationOutcome.AccountDisabled => Error(ApiErrorDescriptors.ExchangeAccountDisabled),
            ExchangeAccountCredentialRotationOutcome.InvalidCredentials => Error(ApiErrorDescriptors.ExchangeCredentialsInvalid),
            ExchangeAccountCredentialRotationOutcome.PermissionsRejected => Error(ApiErrorDescriptors.ExchangePermissionsRejected),
            ExchangeAccountCredentialRotationOutcome.UnsupportedExchange => BadRequestProblem("The exchange is not supported."),
            _ => Error(ApiErrorDescriptors.ExchangeUnavailable),
        };
    }

    [HttpPost("{id}/sync")]
    [ProducesResponseType(typeof(ExchangeAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExchangeAccountResponse>> Synchronize([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await syncService.SynchronizeAsync(currentUserContext.UserId, ExchangeAccountId.FromGuid(id), cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            ExchangeAccountSyncOutcome.Synchronized or ExchangeAccountSyncOutcome.AlreadyApplied or ExchangeAccountSyncOutcome.Superseded when result.Account is not null => Ok(ToResponse(result.Account)),
            ExchangeAccountSyncOutcome.NotFound => NotFoundProblem(),
            ExchangeAccountSyncOutcome.AccountDisabled => Error(ApiErrorDescriptors.ExchangeAccountDisabled),
            _ => Error(ApiErrorDescriptors.ExchangeUnavailable),
        };
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Disconnect([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var account = await accountService.DisconnectAsync(currentUserContext.UserId, ExchangeAccountId.FromGuid(id), cancellationToken).ConfigureAwait(false);
        return account is null ? NotFoundProblem() : NoContent();
    }

    private async Task<ActionResult<ExchangeAccountResponse>> ExecuteVerification(Guid id, CancellationToken cancellationToken)
    {
        var result = await accountService.VerifyAsync(currentUserContext.UserId, ExchangeAccountId.FromGuid(id), cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            ExchangeAccountVerificationOutcome.Succeeded when result.Account is not null => Ok(ToResponse(result.Account)),
            ExchangeAccountVerificationOutcome.NotFound => NotFoundProblem(),
            ExchangeAccountVerificationOutcome.AccountDisabled => Error(ApiErrorDescriptors.ExchangeAccountDisabled),
            ExchangeAccountVerificationOutcome.InvalidCredentials => Error(ApiErrorDescriptors.ExchangeCredentialsInvalid),
            ExchangeAccountVerificationOutcome.PermissionsRejected => Error(ApiErrorDescriptors.ExchangePermissionsRejected),
            ExchangeAccountVerificationOutcome.UnsupportedExchange => BadRequestProblem("The exchange is not supported."),
            _ => Error(ApiErrorDescriptors.ExchangeUnavailable),
        };
    }

    private ObjectResult Error(ApiErrorDescriptor descriptor, string? detail = null) =>
        StatusCode(descriptor.StatusCode, ApiProblemDetails.Create(HttpContext, descriptor, detail));
    private ObjectResult NotFoundProblem() => Error(ApiErrorDescriptors.ResourceNotFound, "The requested resource was not found.");
    private BadRequestObjectResult BadRequestProblem(string detail) => BadRequest(ApiProblemDetails.CreateValidation(HttpContext, detail));

    private static ExchangeAccountResponse ToResponse(ExchangeAccount account) => new(
        account.Id.Value, ExchangeProvider.Bybit, (ExchangeAccountStatus)account.ConnectionStatus,
        Enum.GetValues<ExchangeAccountCapabilities>().Where(x => x != ExchangeAccountCapabilities.None && account.Capabilities.HasFlag(x))
            .Select(x => (ExchangeAccountCapability)x).ToArray(), account.LastSyncedAt);
}
