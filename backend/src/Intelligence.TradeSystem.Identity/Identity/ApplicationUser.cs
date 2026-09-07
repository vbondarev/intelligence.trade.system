using Microsoft.AspNetCore.Identity;

namespace Intelligence.TradeSystem.Identity.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.NewGuid();
    }
}
