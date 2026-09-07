using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Intelligence.TradeSystem.Identity.Identity;
using OpenIddict.EntityFrameworkCore.Models;

namespace Intelligence.TradeSystem.Identity.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<OpenIddictEntityFrameworkCoreApplication> OpenIddictApplications => Set<OpenIddictEntityFrameworkCoreApplication>();
    public DbSet<OpenIddictEntityFrameworkCoreAuthorization> OpenIddictAuthorizations => Set<OpenIddictEntityFrameworkCoreAuthorization>();
    public DbSet<OpenIddictEntityFrameworkCoreScope> OpenIddictScopes => Set<OpenIddictEntityFrameworkCoreScope>();
    public DbSet<OpenIddictEntityFrameworkCoreToken> OpenIddictTokens => Set<OpenIddictEntityFrameworkCoreToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
    }
}
