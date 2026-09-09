using System.Reflection;
using System.Text.Json.Serialization;
using FluentValidation;
using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Authentication;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Api.Validation;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Exchanges;
using Intelligence.TradeSystem.Infrastructure;
using Intelligence.TradeSystem.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Intelligence.TradeSystem.Api;

public partial class Program
{
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
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ApiProblemDetails.Customize;
        });
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.Configure<ExceptionHandlerOptions>(options =>
        {
            options.SuppressDiagnosticsCallback = context =>
                ApiExceptionHandler.ShouldSuppressDiagnostics(
                    context.Exception,
                    context.HttpContext.RequestAborted.IsCancellationRequested);
        });
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
               var detail = context.ModelState.Values
                       .SelectMany(state => state.Errors)
                       .Select(error => error.ErrorMessage)
                       .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                   ?? "The request could not be processed.";
               var problemDetails =
                   ApiProblemDetails.CreateValidation(context.HttpContext, detail);
               ApiProblemDetails.AddModelStateErrors(
                   problemDetails,
                   context.ModelState);

               return new BadRequestObjectResult(problemDetails);
            };
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
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddExchangeAccountBackgroundSynchronization(builder.Configuration);
        }
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
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, ClaimsPrincipalCurrentUserContext>();

        var app = builder.Build();

        app.UseExceptionHandler();

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

        var backchannelBaseAddress = authentication.BackchannelBaseAddress
            ?? new Uri(metadataUri.GetLeftPart(UriPartial.Authority)).AbsoluteUri;

        if (!Uri.TryCreate(backchannelBaseAddress, UriKind.Absolute, out var backchannelUri)
            || backchannelUri is null
            || backchannelUri.Scheme is not ("http" or "https")
            || (builder.Environment.IsProduction() && backchannelUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Authentication:BackchannelBaseAddress must be an absolute HTTPS URL in Production.");
        }

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = metadataUri.AbsoluteUri;
                options.BackchannelHttpHandler = new PublicIssuerBackchannelHandler(
                    issuerUri,
                    backchannelUri,
                    new HttpClientHandler());
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
            var tradeApi = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => TradeAuthorization.HasApiScope(context.User))
                .Build();
            var tradeUser = new AuthorizationPolicyBuilder(tradeApi)
                .RequireClaim(
                    TradeAuthorization.PrincipalTypeClaim,
                    TradeAuthorization.UserPrincipalType)
                .RequireAssertion(context =>
                    TradeAuthorization.TryGetUserId(context.User, out _))
                .Build();

            options.AddPolicy(TradeAuthorization.ApiPolicy, tradeApi);
            options.AddPolicy(TradeAuthorization.UserPolicy, tradeUser);
        });
    }
}
