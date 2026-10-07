using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Domain;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

/// <summary>
/// Публикует в OpenAPI лимит <c>displayName</c> из <see cref="ExchangeAccount.DisplayNameMaxLength"/>.
/// DataAnnotations <c>[MaxLength]</c> здесь не используется: model validation проверила бы длину
/// до trim, тогда как контракт ограничивает значение после отбрасывания пробелов по краям.
/// </summary>
internal sealed class ExchangeAccountDisplayNameSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if ((context.Type != typeof(CreateExchangeAccountRequest) &&
             context.Type != typeof(RenameExchangeAccountRequest)) ||
            schema.Properties is null ||
            !schema.Properties.TryGetValue("displayName", out var displayNameSchema) ||
            displayNameSchema is not OpenApiSchema concreteDisplayNameSchema)
        {
            return;
        }

        concreteDisplayNameSchema.MaxLength = ExchangeAccount.DisplayNameMaxLength;
    }
}
