using Intelligence.TradeSystem.Identity.Configuration;
using Microsoft.AspNetCore.Identity;

namespace Intelligence.TradeSystem.Identity.Identity;

/// <summary>
/// Создаёт локального development user. Существующий пароль никогда не меняется:
/// расхождение с конфигурацией останавливает запуск вместо тихой перезаписи.
/// </summary>
public sealed partial class DevelopmentUserSeeder(
    IServiceScopeFactory scopeFactory,
    DevelopmentUserOptions options,
    ILogger<DevelopmentUserSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var username = options.Username!;
        var password = options.Password!;

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            var result = await userManager.CreateAsync(new ApplicationUser { UserName = username }, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Не удалось создать development user: "
                    + string.Join(", ", result.Errors.Select(error => error.Code)));
            }

            LogUserCreated(username);
            return;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            throw new InvalidOperationException(
                "Development user уже существует с другим паролем; пароль не изменяется автоматически.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Development user {Username} создан.")]
    private partial void LogUserCreated(string username);
}
