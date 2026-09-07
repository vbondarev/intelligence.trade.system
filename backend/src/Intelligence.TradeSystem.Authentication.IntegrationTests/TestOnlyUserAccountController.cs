using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

[ApiController]
[Route("test-only/accounts")]
[Authorize(Policy = "TradeUser")]
public sealed class TestOnlyUserAccountController(
    ICurrentUserContext currentUserContext,
    IExchangeAccountRepository repository) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var accountId = ExchangeAccountId.FromGuid(id);
        var account = await repository.GetByIdAsync(
            currentUserContext.UserId,
            accountId,
            cancellationToken);

        if (account is not { } loaded)
            return NotFound();

        var domainAccount = loaded.Value;
        return Ok(new
        {
            AccountId = domainAccount.Id.Value,
            UserId = domainAccount.UserId.Value,
        });
    }
}
