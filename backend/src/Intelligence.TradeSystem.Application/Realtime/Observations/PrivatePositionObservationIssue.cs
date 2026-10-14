namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>
/// Причина, по которой наблюдение позиции классифицировано как
/// <see cref="PrivatePositionObservationKind.Unsupported"/> или
/// <see cref="PrivatePositionObservationKind.Ambiguous"/>.
/// </summary>
public enum PrivatePositionObservationIssue
{
    /// <summary>Исходное сообщение недоступно, поэтому наличие обязательных полей не проверено.</summary>
    UnverifiableMessage = 1,

    /// <summary>В сообщении отсутствует обязательное поле.</summary>
    MissingField = 2,

    /// <summary>Символ инструмента пуст или имеет недопустимый формат.</summary>
    InvalidSymbol = 3,

    /// <summary>Категория рынка не поддерживается.</summary>
    UnsupportedCategory = 4,

    /// <summary>Значение <c>positionIdx</c> не определено биржей.</summary>
    InvalidPositionIdx = 5,

    /// <summary>Отрицательный размер позиции.</summary>
    InvalidSize = 6,

    /// <summary>Нет ни биржевой последовательности, ни времени обновления позиции.</summary>
    MissingRevision = 7,

    /// <summary>Направление позиции с ненулевым размером не определено.</summary>
    UnknownSide = 8,

    /// <summary>Направление противоречит стороне hedge mode, заданной <c>positionIdx</c>.</summary>
    PositionIdxSideMismatch = 9,

    /// <summary>Актив расчёта не подтверждён метаданными инструмента.</summary>
    SettlementAssetUnconfirmed = 10,

    /// <summary>Подтверждённый актив расчёта не поддерживается.</summary>
    UnsupportedSettlementAsset = 11,
}
