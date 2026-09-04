namespace Intelligence.TradeSystem.Application.Concurrency;

/// <summary>
/// Обёртка над доменным агрегатом и версией, под которой он был прочитан из хранилища.
/// </summary>
/// <typeparam name="T">Тип доменного агрегата (не хранит версию внутри себя).</typeparam>
/// <param name="Value">Прочитанный доменный агрегат.</param>
/// <param name="Version">Версия, под которой агрегат был прочитан; передаётся в
/// <c>SaveAsync</c> как ожидаемая версия для CAS-обновления.</param>
public sealed record Versioned<T>(T Value, ConcurrencyVersion Version)
    where T : notnull;
