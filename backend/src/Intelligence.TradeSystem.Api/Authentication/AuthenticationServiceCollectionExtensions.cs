using Intelligence.TradeSystem.Api.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Intelligence.TradeSystem.Api.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddTradeAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var authentication = configuration
            .GetSection(AuthenticationOptions.SectionName)
            .Get<AuthenticationOptions>() ?? new AuthenticationOptions();
        var issuer = authentication.Issuer ?? "http://localhost:5001";
        var audience = authentication.Audience ?? "intelligence-trade-api";

        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
            || issuerUri is null
            || issuerUri.Scheme is not ("http" or "https")
            || (environment.IsProduction() && issuerUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Authentication:Issuer must be an absolute HTTPS URL in Production.");
        }

        var canonicalIssuer = issuerUri.AbsoluteUri;
        var metadataAddress = authentication.MetadataAddress
            ?? new Uri(issuerUri, ".well-known/openid-configuration").AbsoluteUri;

        if (!Uri.TryCreate(metadataAddress, UriKind.Absolute, out var metadataUri)
            || metadataUri is null
            || metadataUri.Scheme is not ("http" or "https")
            || (environment.IsProduction() && metadataUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Authentication:MetadataAddress must be an absolute HTTPS URL in Production.");
        }

        var backchannelBaseAddress = authentication.BackchannelBaseAddress
            ?? new Uri(metadataUri.GetLeftPart(UriPartial.Authority)).AbsoluteUri;

        if (!Uri.TryCreate(backchannelBaseAddress, UriKind.Absolute, out var backchannelUri)
            || backchannelUri is null
            || backchannelUri.Scheme is not ("http" or "https")
            || (environment.IsProduction() && backchannelUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Authentication:BackchannelBaseAddress must be an absolute HTTPS URL in Production.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = metadataUri.AbsoluteUri;
                options.BackchannelHttpHandler = new PublicIssuerBackchannelHandler(
                    issuerUri,
                    backchannelUri,
                    new HttpClientHandler());
                options.Audience = audience;
                options.RequireHttpsMetadata = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = canonicalIssuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(5),
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        if (!context.Request.Path.StartsWithSegments("/api/v1"))
                        {
                            return Task.CompletedTask;
                        }

                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers["WWW-Authenticate"] =
                            JwtBearerDefaults.AuthenticationScheme;
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            var tradeApi = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => TradeAuthorization.HasApiScope(context.User))
                .Build();
            var tradeUser = new AuthorizationPolicyBuilder(tradeApi)
                .RequireClaim(
                    TradeAuthorization.PrincipalTypeClaim,
                    TradeAuthorization.UserPrincipalType)
                .RequireAssertion(context => TradeAuthorization.TryGetUserId(context.User, out _))
                .Build();

            options.AddPolicy(TradeAuthorization.ApiPolicy, tradeApi);
            options.AddPolicy(TradeAuthorization.UserPolicy, tradeUser);
        });

        return services;
    }
}
