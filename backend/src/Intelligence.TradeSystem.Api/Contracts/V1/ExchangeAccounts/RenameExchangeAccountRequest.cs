using System.ComponentModel.DataAnnotations;

namespace Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

/// <summary>Запрос на изменение пользовательского отображаемого имени подключения.</summary>
public sealed record RenameExchangeAccountRequest
{
    /// <summary>
    /// Новое отображаемое имя. Пробелы по краям отбрасываются; после этого значение
    /// не может быть пустым и длиннее 100 символов.
    /// </summary>
    [Required]
    public required string DisplayName { get; init; }
}
