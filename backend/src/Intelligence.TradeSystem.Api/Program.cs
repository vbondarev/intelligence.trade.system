using System.Reflection;
using System.Text.Json.Serialization;
using FluentValidation;
using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Api.Validation;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Exchanges;
using Intelligence.TradeSystem.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Intelligence.TradeSystem.Api;

public partial class Program
{
    private const string SubjectClaim = "sub";
    private const string ScopeClaim = "scope";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        builder.Services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            var xmlFileName = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlFilePath = Path.Combine(AppContext.BaseDirectory, xmlFileName);

            if (File.Exists(xmlFilePath))
            {
                options.IncludeXmlComments(xmlFilePath, includeControllerXmlComments: true);
            }
        });
        builder.Services.AddApplication();
        builder.Services.AddBybitExchange();
        builder.Services.AddInfrastructure(builder.Configuration);
        ConfigureAuthentication(builder);
        var freshnessOptions = builder.Configuration
            .GetSection(SnapshotFreshnessOptions.SectionName)
            .Get<SnapshotFreshnessOptions>() ?? SnapshotFreshnessOptions.Default;
        builder.Services.AddOptions<SnapshotFreshnessOptions>().Configure(o =>
        {
            // no-op: options bound via singleton below
        });
        builder.Services.AddSingleton(freshnessOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<SnapshotFreshnessOptions>>(
            sp => Microsoft.Extensions.Options.Options.Create(sp.GetRequiredService<SnapshotFreshnessOptions>()));
        builder.Services.AddSingleton<ISnapshotHealthEvaluator, SnapshotHealthEvaluator>();
        builder.Services.AddScoped<IValidator<SnapshotAnalysisRequest>, SnapshotAnalysisRequestValidator>();
        builder.Services.AddScoped<IValidator<LlmPayloadRequest>, LlmPayloadRequestValidator>();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/", () => Results.Ok(new
        {
            Service = "Intelligence.TradeSystem.Api",
            Status = "Started",
        }));

        app.MapControllers();
        app.MapDefaultEndpoints();

        app.Run();
    }

    private static void ConfigureAuthentication(WebApplicationBuilder builder)
    {
        var authentication = builder.Configuration
            .GetSection(AuthenticationOptions.SectionName)
            .Get<AuthenticationOptions>() ?? new AuthenticationOptions();
        var issuer = authentication.Issuer
            ?? "http://localhost:5001";
        var audience = authentication.Audience
            ?? "intelligence-trade-api";

        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
            || issuerUri is null
            || issuerUri.Scheme is not ("http" or "https")
            || (builder.Environment.IsProduction() && issuerUri.Scheme != Uri.UriSchemeHttps))
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
            || (builder.Environment.IsProduction() && metadataUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Authentication:MetadataAddress must be an absolute HTTPS URL in Production.");
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = metadataUri.AbsoluteUri;
                options.Audience = audience;
                options.RequireHttpsMetadata =
                    !builder.Environment.IsDevelopment()
                    && !builder.Environment.IsEnvironment("Testing");
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
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("TradeApi", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.FindAll(ScopeClaim)
                        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        .Contains("trade.api", StringComparer.Ordinal));
                policy.RequireClaim(SubjectClaim);
            });
        });
    }
}
