namespace Intelligence.TradeSystem.Bff.Authentication;

/// <summary>
/// Process-local registry refresh gates: один gate на authenticated subject, пока у него есть
/// владелец lock или ожидающий request.
/// </summary>
/// <remarks>
/// Lifetime gate определяется только owner/waiter references, а не session lifetime или cache
/// expiration: иначе второй request того же subject мог бы получить новый semaphore и отправить
/// ещё один rotating refresh grant, пока первый ещё выполняется. Глобальный lock удерживается
/// только на время коротких операций registry и никогда — во время ожидания semaphore или HTTP.
/// </remarks>
internal sealed class RefreshGateRegistry
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, RefreshGate> gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Количество subjects, для которых сейчас существует gate.
    /// </summary>
    internal int Count
    {
        get
        {
            lock (sync)
            {
                return gates.Count;
            }
        }
    }

    /// <summary>
    /// Возвращает gate subject и регистрирует вызывающего как владельца или ожидающего.
    /// Каждый вызов должен завершаться ровно одним <see cref="Release"/>, в том числе при отмене ожидания.
    /// </summary>
    public RefreshGate Acquire(string subject)
    {
        lock (sync)
        {
            if (!gates.TryGetValue(subject, out var gate))
            {
                gate = new RefreshGate();
                gates.Add(subject, gate);
            }

            gate.Users++;
            return gate;
        }
    }

    /// <summary>
    /// Снимает reference, полученный через <see cref="Acquire"/>. Semaphore gate не освобождает.
    /// </summary>
    public void Release(string subject, RefreshGate gate)
    {
        lock (sync)
        {
            gate.Users--;
            if (gate.Users == 0
                && gates.TryGetValue(subject, out var current)
                && ReferenceEquals(current, gate))
            {
                gates.Remove(subject);
            }
        }
    }

    /// <summary>
    /// Количество владельцев и ожидающих gate subject; 0, если gate отсутствует.
    /// </summary>
    internal int UsersOf(string subject)
    {
        lock (sync)
        {
            return gates.TryGetValue(subject, out var gate) ? gate.Users : 0;
        }
    }
}

/// <summary>
/// Per-subject lock refresh вместе со счётчиком owner/waiter references.
/// </summary>
internal sealed class RefreshGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    /// <summary>
    /// Изменяется только <see cref="RefreshGateRegistry"/> под его lock.
    /// </summary>
    public int Users { get; set; }
}
