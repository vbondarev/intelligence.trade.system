namespace Intelligence.TradeSystem.Application.Realtime.Ordering;

/// <summary>
/// Вид сохранённого REST-интервала, используемого для согласования порядка с потоковыми
/// наблюдениями между экземплярами backend.
/// </summary>
public enum ExchangeRestOrderingKind
{
    /// <summary>Принятое полное REST-наблюдение позиций аккаунта.</summary>
    FullPositionsComplete = 1,

    /// <summary>Принятое ограниченное наблюдение конкретного символа.</summary>
    ScopedSymbol = 2,

    /// <summary>Принятый достоверный REST-баланс.</summary>
    Balance = 3,
}
