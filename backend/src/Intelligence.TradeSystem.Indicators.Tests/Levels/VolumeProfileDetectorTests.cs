using FluentAssertions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Indicators.Levels;
using Intelligence.TradeSystem.Indicators.Tests.Helpers;

namespace Intelligence.TradeSystem.Indicators.Tests.Levels;

public sealed class VolumeProfileDetectorTests
{
    [Fact]
    public void Returns_All_Nulls_When_Empty_Array()
    {
        var result = VolumeProfileDetector.Detect([]);

        result.Support1.Should().BeNull();
        result.Support2.Should().BeNull();
        result.Resistance1.Should().BeNull();
        result.Resistance2.Should().BeNull();
    }

    [Fact]
    public void Returns_All_Nulls_When_Range_Is_Zero()
    {
        // Все OHLC одинаковы → range == 0 → уровни не определяются (null)
        var kline = KlineFactory.Create(open: 100m, high: 100m, low: 100m, close: 100m);
        var result = VolumeProfileDetector.Detect([kline]);

        result.Support1.Should().BeNull();
        result.Support2.Should().BeNull();
        result.Resistance1.Should().BeNull();
        result.Resistance2.Should().BeNull();
    }

    [Fact]
    public void Support_Levels_Are_Below_Current_Price()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);
        var currentPrice = klines[^1].Close;

        if (result.Support1 is { } s1a)
        {
            s1a.Price.Should().BeLessThan(currentPrice);
        }

        if (result.Support2 is { } s2a)
        {
            s2a.Price.Should().BeLessThan(currentPrice);
        }
    }

    [Fact]
    public void Resistance_Levels_Are_Above_Current_Price()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);
        var currentPrice = klines[^1].Close;

        if (result.Resistance1 is { } r1a)
        {
            r1a.Price.Should().BeGreaterThan(currentPrice);
        }

        if (result.Resistance2 is { } r2a)
        {
            r2a.Price.Should().BeGreaterThan(currentPrice);
        }
    }

    [Fact]
    public void Support1_Is_Closer_To_Price_Than_Support2()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        if (result.Support1 is { } s1 && result.Support2 is { } s2)
        {
            s1.Price.Should().BeGreaterThan(s2.Price);
        }
    }

    [Fact]
    public void Resistance1_Is_Closer_To_Price_Than_Resistance2()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        if (result.Resistance1 is { } r1 && result.Resistance2 is { } r2)
        {
            r1.Price.Should().BeLessThan(r2.Price);
        }
    }

    [Fact]
    public void High_Volume_Bucket_Is_Selected_As_Level()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Зона A: низкие цены (~45–55), огромный объём → ожидаем поддержку здесь
        var lowZone = Enumerable.Range(0, 10).Select(i => KlineFactory.Create(
            open: 50m, high: 55m, low: 45m, close: 52m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        // Зона B: текущая цена (~95–105), минимальный объём
        var highZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 100m, high: 105m, low: 95m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10 + i))).ToArray();

        var klines = lowZone.Concat(highZone).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        // Support1 или Support2 должна попасть в высокообъёмную зону [45, 55]
        var isInHighVolumeZone = (result.Support1 is { } s1hv && s1hv.Price >= 45m && s1hv.Price <= 55m)
                              || (result.Support2 is { } s2hv && s2hv.Price >= 45m && s2hv.Price <= 55m);

        isInHighVolumeZone.Should().BeTrue(
            because: "высокообъёмная ценовая зона (45–55) должна быть определена как уровень поддержки");
    }

    // ── Price-side invariants (strict, unconditional) ────────────────────────

    /// <summary>
    /// Детерминированный сценарий: две HVN-зоны ниже текущей цены.
    /// Обе поддержки гарантированно обнаружены → утверждения безусловные.
    /// </summary>
    [Fact]
    public void Support1_Is_Strictly_Below_Current_Price_When_Detected()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zoneA = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 24m, high: 30m, low: 20m, close: 26m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var zoneB = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 44m, high: 50m, low: 40m, close: 46m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(5 + i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 99m, high: 102m, low: 98m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10));

        var klines = zoneA.Concat(zoneB).Append(anchor).ToArray();
        var currentPrice = klines[^1].Close;

        var result = VolumeProfileDetector.Detect(klines);

        result.Support1.Should().NotBeNull(because: "две HVN-зоны ниже цены гарантируют наличие Support1");
        result.Support1!.Price.Should().BeLessThan(currentPrice,
            because: "Support1 обязан быть ниже текущей цены");
    }

    /// <summary>
    /// Детерминированный сценарий: две HVN-зоны ниже текущей цены.
    /// Support2 гарантированно обнаружен → утверждение безусловное.
    /// </summary>
    [Fact]
    public void Support2_Is_Strictly_Below_Current_Price_When_Detected()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zoneA = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 24m, high: 30m, low: 20m, close: 26m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var zoneB = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 44m, high: 50m, low: 40m, close: 46m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(5 + i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 99m, high: 102m, low: 98m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10));

        var klines = zoneA.Concat(zoneB).Append(anchor).ToArray();
        var currentPrice = klines[^1].Close;

        var result = VolumeProfileDetector.Detect(klines);

        result.Support2.Should().NotBeNull(because: "две HVN-зоны ниже цены гарантируют наличие Support2");
        result.Support2!.Price.Should().BeLessThan(currentPrice,
            because: "Support2 обязан быть ниже текущей цены");
    }

    /// <summary>
    /// Детерминированный сценарий: две HVN-зоны выше текущей цены.
    /// Resistance1 гарантированно обнаружен → утверждение безусловное.
    /// </summary>
    [Fact]
    public void Resistance1_Is_Strictly_Above_Current_Price_When_Detected()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zoneC = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 114m, high: 120m, low: 110m, close: 116m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var zoneD = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 144m, high: 150m, low: 140m, close: 146m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(5 + i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 99m, high: 102m, low: 98m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10));

        var klines = zoneC.Concat(zoneD).Append(anchor).ToArray();
        var currentPrice = klines[^1].Close;

        var result = VolumeProfileDetector.Detect(klines);

        result.Resistance1.Should().NotBeNull(because: "две HVN-зоны выше цены гарантируют наличие Resistance1");
        result.Resistance1!.Price.Should().BeGreaterThan(currentPrice,
            because: "Resistance1 обязан быть выше текущей цены");
    }

    /// <summary>
    /// Детерминированный сценарий: две HVN-зоны выше текущей цены.
    /// Resistance2 гарантированно обнаружен → утверждение безусловное.
    /// </summary>
    [Fact]
    public void Resistance2_Is_Strictly_Above_Current_Price_When_Detected()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zoneC = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 114m, high: 120m, low: 110m, close: 116m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var zoneD = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 144m, high: 150m, low: 140m, close: 146m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(5 + i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 99m, high: 102m, low: 98m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10));

        var klines = zoneC.Concat(zoneD).Append(anchor).ToArray();
        var currentPrice = klines[^1].Close;

        var result = VolumeProfileDetector.Detect(klines);

        result.Resistance2.Should().NotBeNull(because: "две HVN-зоны выше цены гарантируют наличие Resistance2");
        result.Resistance2!.Price.Should().BeGreaterThan(currentPrice,
            because: "Resistance2 обязан быть выше текущей цены");
    }

    // ── Absent level is null, never zero ─────────────────────────────────────

    [Fact]
    public void Absent_Support2_Is_Null_Not_Zero()
    {
        // Одна HVN-зона снизу → Support1 найдена, Support2 отсутствует.
        // Контракт: отсутствующий уровень возвращается как null, а не как объект с Price=0.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var singleSupport = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 44m, high: 50m, low: 40m, close: 46m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 99m, high: 102m, low: 98m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(5));

        var klines = singleSupport.Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        result.Support1.Should().NotBeNull(because: "единственная HVN-зона снизу образует Support1");
        result.Support2.Should().BeNull(
            because: "отсутствующий второй уровень поддержки должен быть null, а не объектом с Price=0");
    }

    [Fact]
    public void Absent_Resistance2_Is_Null_Not_Zero()
    {
        // Одна HVN-зона сверху → Resistance1 найдена, Resistance2 отсутствует.
        // Контракт: отсутствующий уровень возвращается как null, а не как объект с Price=0.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var singleResistance = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 144m, high: 150m, low: 140m, close: 146m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 99m, high: 102m, low: 98m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(5));

        var klines = singleResistance.Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        result.Resistance1.Should().NotBeNull(because: "единственная HVN-зона сверху образует Resistance1");
        result.Resistance2.Should().BeNull(
            because: "отсутствующий второй уровень сопротивления должен быть null, а не объектом с Price=0");
    }

    [Fact]
    public void All_Levels_Absent_Returns_All_Null_Not_Zero()
    {
        // Нет ни одной HVN-зоны (объём 0) → все четыре уровня null.
        // Проверяет, что нулевой объём не порождает фиктивных уровней с Price=0.
        var klines = new[]
        {
            KlineFactory.Create(open: 90m, high: 110m, low: 80m, close: 100m, volume: 0m),
        };

        var result = VolumeProfileDetector.Detect(klines);

        result.Support1.Should().BeNull(because: "без HVN-зон Support1 должен быть null, не 0");
        result.Support2.Should().BeNull(because: "без HVN-зон Support2 должен быть null, не 0");
        result.Resistance1.Should().BeNull(because: "без HVN-зон Resistance1 должен быть null, не 0");
        result.Resistance2.Should().BeNull(because: "без HVN-зон Resistance2 должен быть null, не 0");
    }

    // ── Guard-clause coverage ────────────────────────────────────────────────

    [Fact]
    public void Throws_ArgumentNullException_When_Klines_Is_Null()
    {
        var act = () => VolumeProfileDetector.Detect(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("klines");
    }

    // ── Zero-volume profile ──────────────────────────────────────────────────

    [Fact]
    public void Returns_All_Nulls_When_All_Volumes_Are_Zero()
    {
        // Ценовой диапазон существует, но весь объём равен 0 → профиль пуст → уровней нет.
        // Детектор не должен генерировать искусственные уровни из пустого профиля.
        var klines = new[]
        {
            KlineFactory.Create(open: 90m, high: 110m, low: 80m, close: 100m, volume: 0m),
            KlineFactory.Create(open: 95m, high: 115m, low: 85m, close: 105m, volume: 0m),
        };

        var result = VolumeProfileDetector.Detect(klines);

        result.Support1.Should().BeNull();
        result.Support2.Should().BeNull();
        result.Resistance1.Should().BeNull();
        result.Resistance2.Should().BeNull();
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void Returns_Deterministic_Result_For_Same_Input()
    {
        // Эвристический алгоритм должен быть полностью детерминированным:
        // повторный вызов на одних и тех же данных обязан давать идентичный результат.
        var klines = KlineFactory.CreateSeries(30).ToArray();

        var first = VolumeProfileDetector.Detect(klines);
        var second = VolumeProfileDetector.Detect(klines);

        second.Should().Be(first);
    }

    // ── Clustering behaviour ─────────────────────────────────────────────────

    [Fact]
    public void Clusters_Adjacent_High_Volume_Buckets_Into_One_Zone()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Весь объём сосредоточен в диапазоне цен 20–30 (несколько соседних HVN-бакетов).
        // При корректной кластеризации все соседние бакеты объединяются в ОДИН кластер
        //   → Support1 попадает в зону [20, 30], Support2 остаётся 0 (нет второго кластера).
        // При сломанной кластеризации каждый бакет становится отдельным кластером
        //   → Support2 заполняется соседним значением → тест падает.
        var highVolumeZone = Enumerable.Range(0, 10)
            .Select(i => KlineFactory.Create(
                open: 24m, high: 30m, low: 20m, close: 26m,
                volume: 1_000_000m,
                startTime: baseTime.AddHours(i)))
            .ToArray();

        // Якорная свеча задаёт текущую цену (150) — выше зоны, чтобы зона стала поддержкой.
        var anchor = KlineFactory.Create(
            open: 148m, high: 155m, low: 145m, close: 150m,
            volume: 10m,
            startTime: baseTime.AddHours(10));

        var klines = highVolumeZone.Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        result.Support1!.Price.Should().BeInRange(20m, 31m,
            because: "центр HVN-кластера должен попасть в диапазон зоны 20–30");
        result.Support2.Should().BeNull(
            because: "соседние бакеты одной HVN-зоны должны объединяться в один кластер, не в два");
    }

    // ── Proximity-ordering regression ────────────────────────────────────────

    [Fact]
    public void Selects_Closest_Strong_Support_Levels_When_Multiple_High_Volume_Zones_Exist()
    {
        // Три равнообъёмные HVN-зоны ниже текущей цены (100):
        //   ближняя ≈ 85, средняя ≈ 55, дальняя ≈ 25.
        // Детектор обязан вернуть два БЛИЖАЙШИХ уровня, не самых объёмных и не самых дальних.
        // Регрессия: смена OrderByDescending на OrderBy → тест упадёт.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Kline[] MakeZone(decimal mid, int hourOffset) =>
            [.. Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
                open: mid - 2m, high: mid + 5m, low: mid - 5m, close: mid,
                volume: 1_000_000m,
                startTime: baseTime.AddHours(hourOffset + i)))];

        var klines = MakeZone(mid: 85m, hourOffset: 0)   // ближняя  (~80–90)
            .Concat(MakeZone(mid: 55m, hourOffset: 5))   // средняя  (~50–60)
            .Concat(MakeZone(mid: 25m, hourOffset: 10))   // дальняя  (~20–30)
            .Append(KlineFactory.Create(
                open: 99m, high: 102m, low: 98m, close: 100m,
                volume: 10m,
                startTime: baseTime.AddHours(15)))
            .ToArray();

        var result = VolumeProfileDetector.Detect(klines);

        // S1 = ближайшая зона ≈ 85; границы совпадают с физическим диапазоном зоны [80, 90]
        result.Support1!.Price.Should().BeInRange(80m, 90m,
            because: "Support1 должна быть ближайшей к цене поддержкой (~85)");
        // S2 = вторая по близости ≈ 55; границы совпадают с физическим диапазоном зоны [50, 60]
        result.Support2!.Price.Should().BeInRange(50m, 60m,
            because: "Support2 должна быть второй по близости поддержкой (~55)");
        // Дальняя зона (~25) не должна попасть ни в один слот
        result.Support1.Price.Should().BeGreaterThan(35m);
        result.Support2.Price.Should().BeGreaterThan(35m);
    }

    [Fact]
    public void Selects_Closest_Strong_Resistance_Levels_When_Multiple_High_Volume_Zones_Exist()
    {
        // Зеркальный сценарий для сопротивлений.
        // Три равнообъёмные HVN-зоны выше текущей цены (100):
        //   ближняя ≈ 115, средняя ≈ 145, дальняя ≈ 175.
        // R1 и R2 должны быть именно двумя БЛИЖАЙШИМИ зонами.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Kline[] MakeZone(decimal mid, int hourOffset) =>
            [.. Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
                open: mid - 2m, high: mid + 5m, low: mid - 5m, close: mid,
                volume: 1_000_000m,
                startTime: baseTime.AddHours(hourOffset + i)))];

        var klines = MakeZone(mid: 115m, hourOffset: 0)   // ближняя  (~110–120)
            .Concat(MakeZone(mid: 145m, hourOffset: 5))   // средняя  (~140–150)
            .Concat(MakeZone(mid: 175m, hourOffset: 10))   // дальняя  (~170–180)
            .Append(KlineFactory.Create(
                open: 99m, high: 102m, low: 98m, close: 100m,
                volume: 10m,
                startTime: baseTime.AddHours(15)))
            .ToArray();

        var result = VolumeProfileDetector.Detect(klines);

        // R1 = ближайшая зона ≈ 115; границы совпадают с физическим диапазоном зоны [110, 120]
        result.Resistance1!.Price.Should().BeInRange(110m, 120m,
            because: "Resistance1 должна быть ближайшим к цене сопротивлением (~115)");
        // R2 = вторая по близости ≈ 145; границы совпадают с физическим диапазоном зоны [140, 150]
        result.Resistance2!.Price.Should().BeInRange(140m, 150m,
            because: "Resistance2 должна быть вторым по близости сопротивлением (~145)");
        // Дальняя зона (~175) не должна попасть ни в один слот
        result.Resistance1.Price.Should().BeLessThan(135m);
        result.Resistance2.Price.Should().BeLessThan(165m);
    }

    // ── Symmetry: upper HVN → resistance ────────────────────────────────────

    [Fact]
    public void High_Volume_Upper_Zone_Is_Selected_As_Resistance()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Зона B: высокие цены (~145–155), огромный объём → ожидаем сопротивление здесь.
        // Идёт первой, чтобы последняя свеча не сдвигала текущую цену вверх.
        var highZone = Enumerable.Range(0, 10).Select(i => KlineFactory.Create(
            open: 150m, high: 155m, low: 145m, close: 150m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        // Зона A: текущая цена (~95–105), минимальный объём → последняя свеча close=100.
        var currentZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 100m, high: 105m, low: 95m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10 + i))).ToArray();

        var klines = highZone.Concat(currentZone).ToArray();   // current price = 100
        var result = VolumeProfileDetector.Detect(klines);

        // Resistance1 или Resistance2 должна попасть в высокообъёмную зону [145, 155]
        var isInHighVolumeZone = (result.Resistance1 is { } r1hv && r1hv.Price >= 145m && r1hv.Price <= 155m)
                              || (result.Resistance2 is { } r2hv && r2hv.Price >= 145m && r2hv.Price <= 155m);

        isInHighVolumeZone.Should().BeTrue(
            because: "высокообъёмная ценовая зона (145–155) должна быть определена как уровень сопротивления");
    }

    // ── Single-cluster contract ───────────────────────────────────────────────

    [Fact]
    public void Returns_Only_One_Support_When_Only_One_Lower_Cluster_Exists()
    {
        // Контракт: одна HVN-зона ниже текущей цены → Support1 заполнен, Support2 = 0.
        // Фиксирует, что алгоритм не создаёт "ghost"-уровень в пустом месте профиля.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var supportZone = Enumerable.Range(0, 10)
            .Select(i => KlineFactory.Create(
                open: 44m, high: 50m, low: 40m, close: 46m,
                volume: 1_000_000m,
                startTime: baseTime.AddHours(i)))
            .ToArray();

        var anchor = KlineFactory.Create(
            open: 98m, high: 105m, low: 95m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10));

        var klines = supportZone.Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        result.Support1!.Price.Should().BeInRange(40m, 51m,
            because: "единственная HVN-зона снизу должна быть определена как Support1");
        result.Support2.Should().BeNull(
            because: "при наличии одного нижнего кластера Support2 должен быть null");
    }

    [Fact]
    public void Returns_Only_One_Resistance_When_Only_One_Upper_Cluster_Exists()
    {
        // Зеркальный контрактный тест для resistance:
        // одна HVN-зона выше текущей цены → Resistance1 заполнен, Resistance2 = 0.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var resistanceZone = Enumerable.Range(0, 10)
            .Select(i => KlineFactory.Create(
                open: 144m, high: 150m, low: 140m, close: 146m,
                volume: 1_000_000m,
                startTime: baseTime.AddHours(i)))
            .ToArray();

        var anchor = KlineFactory.Create(
            open: 98m, high: 105m, low: 95m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10));

        var klines = resistanceZone.Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        result.Resistance1!.Price.Should().BeInRange(140m, 151m,
            because: "единственная HVN-зона сверху должна быть определена как Resistance1");
        result.Resistance2.Should().BeNull(
            because: "при наличии одного верхнего кластера Resistance2 должен быть null");
    }

    // ── Anti-merge regression ─────────────────────────────────────────────────

    [Fact]
    public void Does_Not_Merge_Two_Distinct_Nearby_High_Volume_Clusters()
    {
        // Две отдельные HVN-зоны разделены ценовым gap-ом (30–40) с нулевым объёмом.
        // Нулевые бакеты не проходят HVN-порог → merge невозможен → два отдельных кластера.
        //
        // Регрессия: слишком агрессивный merge объединит зоны в один кластер
        //   → Support2 == 0 или Support1 ≠ в диапазоне Zone B → тест упадёт.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Zone A: 20–30 (ниже gap-а)
        var zoneA = Enumerable.Range(0, 10).Select(i => KlineFactory.Create(
            open: 24m, high: 30m, low: 20m, close: 26m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        // Zone B: 40–50 (выше gap-а); намеренно не касается Zone A
        var zoneB = Enumerable.Range(0, 10).Select(i => KlineFactory.Create(
            open: 44m, high: 50m, low: 40m, close: 46m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(10 + i))).ToArray();

        // Якорная свеча задаёт текущую цену (150), чтобы обе зоны стали поддержками
        var anchor = KlineFactory.Create(
            open: 148m, high: 155m, low: 145m, close: 150m,
            volume: 10m,
            startTime: baseTime.AddHours(20));

        var klines = zoneA.Concat(zoneB).Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        // Zone B ближе к 150 → Support1; Zone A дальше → Support2
        result.Support1!.Price.Should().BeInRange(40m, 51m,
            because: "Zone B (40–50) ближе к текущей цене → должна стать Support1");
        result.Support2!.Price.Should().BeInRange(20m, 31m,
            because: "Zone A (20–30) дальше от текущей цены → должна стать Support2");
        result.Support2.Should().NotBeNull(
            because: "две раздельные HVN-зоны не должны быть склеены в один кластер");
    }

    // ── VolumeProfileOptions integration ─────────────────────────────────────

    [Fact]
    public void Null_Options_Uses_Default_And_Produces_Same_Result_As_Explicit_Default()
    {
        // Вызов без options и с явным Default должен давать идентичный результат.
        var klines = KlineFactory.CreateSeries(50).ToArray();

        var withNull = VolumeProfileDetector.Detect(klines, options: null);
        var withDefault = VolumeProfileDetector.Detect(klines, VolumeProfileOptions.Default);

        withNull.Should().Be(withDefault);
    }

    [Fact]
    public void Custom_Options_With_Default_Values_Produces_Same_Result_As_Default()
    {
        // Явно созданные options со стандартными значениями дают тот же результат, что Default.
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var customOptions = new VolumeProfileOptions(bucketCount: 100, hvnThresholdRatio: 0.7m);

        var withDefault = VolumeProfileDetector.Detect(klines, VolumeProfileOptions.Default);
        var withCustom = VolumeProfileDetector.Detect(klines, customOptions);

        withCustom.Should().Be(withDefault);
    }

    [Fact]
    public void Lower_HvnThresholdRatio_Detects_More_Levels()
    {
        // Снижение порога HVN включает больше бакетов → больше кластеров → больше уровней.
        //
        // Структура данных:
        //   Зона A (20–30): объём 1 000 000 — это максимум (100%).
        //   Зона B (40–50): объём 600 000 — 60% от максимума.
        //   Якорная свеча (close=100): объём 1 (незначительный, только для задания текущей цены).
        //
        //   ratio=0.5: Zone B (60%) >= 50% → два кластера поддержки.
        //   ratio=0.9: Zone B (60%) <  90% → только Zone A (100%) квалифицируется → один кластер.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zoneA = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 24m, high: 30m, low: 20m, close: 26m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var zoneB = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 44m, high: 50m, low: 40m, close: 46m,
            volume: 600_000m,
            startTime: baseTime.AddHours(5 + i))).ToArray();

        // Якорная свеча: объём = 1, чтобы не создавать конкурирующих HVN-бакетов.
        var anchor = KlineFactory.Create(
            open: 99m, high: 102m, low: 98m, close: 100m,
            volume: 1m,
            startTime: baseTime.AddHours(10));

        var klines = zoneA.Concat(zoneB).Append(anchor).ToArray();

        // ratio=0.5: Zone B (60%) >= 50% → Support1 и Support2 заполнены
        var looseOptions = new VolumeProfileOptions(hvnThresholdRatio: 0.5m);
        var looseResult = VolumeProfileDetector.Detect(klines, looseOptions);

        // ratio=0.9: Zone B (60%) < 90% → только Zone A образует кластер → Support2 = null
        var strictOptions = new VolumeProfileOptions(hvnThresholdRatio: 0.9m);
        var strictResult = VolumeProfileDetector.Detect(klines, strictOptions);

        looseResult.Support1.Should().NotBeNull(
            because: "при пороге 0.5 Zone A (100%) образует Support1");
        looseResult.Support2.Should().NotBeNull(
            because: "при пороге 0.5 Zone B (60%) >= порог → образует второй кластер поддержки");

        strictResult.Support1.Should().NotBeNull(
            because: "при пороге 0.9 Zone A (100%) всё равно преодолевает порог");
        strictResult.Support2.Should().BeNull(
            because: "при пороге 0.9 Zone B (60%) < порог → не образует кластер → Support2 = null");
    }

    [Fact]
    public void Custom_BucketCount_Does_Not_Change_Level_Direction()
    {
        // Кастомное количество бакетов меняет точность, но не нарушает инвариант:
        // support < currentPrice, resistance > currentPrice.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var lowZone = Enumerable.Range(0, 10).Select(i => KlineFactory.Create(
            open: 50m, high: 55m, low: 45m, close: 52m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var highZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 100m, high: 105m, low: 95m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10 + i))).ToArray();

        var klines = lowZone.Concat(highZone).ToArray();
        var currentPrice = klines[^1].Close;

        var options = new VolumeProfileOptions(bucketCount: 50);
        var result = VolumeProfileDetector.Detect(klines, options);

        if (result.Support1 is { } s1)
        {
            s1.Price.Should().BeLessThan(currentPrice,
                because: "support уровень должен быть ниже текущей цены независимо от bucketCount");
        }

        if (result.Resistance1 is { } r1)
        {
            r1.Price.Should().BeGreaterThan(currentPrice,
                because: "resistance уровень должен быть выше текущей цены независимо от bucketCount");
        }
    }

    // ── Signal vs noise ───────────────────────────────────────────────────────

    [Fact]
    public void Selects_Strong_Zone_Over_Noisy_Background()
    {
        // Сценарий реального рынка: 25 шумовых свечей с малым объёмом распределены
        // по широкому диапазону + одна доминирующая HVN-зона.
        // Шумовые бакеты не преодолевают порог (70% от max volume) → фильтруются.
        // Только доминирующая зона образует кластер.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 25 шумовых свечей равномерно распределены по диапазону 60–160 (объём 10 каждая).
        // Максимум шума на бакет ≈ 5 единиц — несравнимо ниже HVN-порога (~250 000).
        var noiseCandles = Enumerable.Range(0, 25).Select(i => KlineFactory.Create(
            open: 62m + i * 4m, high: 65m + i * 4m,
            low: 60m + i * 4m, close: 63m + i * 4m,
            volume: 10m,
            startTime: baseTime.AddHours(i))).ToArray();

        // Одна доминирующая зона (20–30), объём 500 000 >> шум (10) → только она создаёт кластер
        var strongZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 24m, high: 30m, low: 20m, close: 25m,
            volume: 500_000m,
            startTime: baseTime.AddHours(25 + i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 98m, high: 105m, low: 95m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(30));

        var klines = noiseCandles.Concat(strongZone).Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        // Доминирующая зона (20–30) должна стать Support1
        result.Support1!.Price.Should().BeInRange(20m, 31m,
            because: "доминирующая high-volume зона должна быть выбрана как Support1, а не шумовой бакет");
        // Ни один шумовой бакет (начинаются с ≥ 60) не образовал второй кластер
        result.Support2.Should().BeNull(
            because: "шумовые бакеты не преодолевают HVN-порог → единственный support кластер");
    }

    // ── LevelStrength contracts ───────────────────────────────────────────────

    [Fact]
    public void Dominant_Single_Cluster_Has_Strength_1()
    {
        // Единственный кластер — доминирующий → normalization by maxClusterVolume → Strength = 1.0.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var strongZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 24m, high: 30m, low: 20m, close: 26m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 98m, high: 105m, low: 95m, close: 100m,
            volume: 1m,
            startTime: baseTime.AddHours(5));

        var klines = strongZone.Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        result.Support1.Should().NotBeNull(because: "доминирующая зона должна образовать Support1");
        result.Support1!.Strength.Should().Be(1.0m,
            because: "единственный кластер является доминирующим → Strength = 1.0");
    }

    [Fact]
    public void Weaker_Cluster_Has_Lower_Strength_Than_Dominant_Cluster()
    {
        // Две зоны с разным объёмом: Zone A (1 000 000) > Zone B (500 000).
        // После нормализации: Zone A → Strength = 1.0, Zone B → Strength < 1.0.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var dominantZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 24m, high: 30m, low: 20m, close: 26m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        var weakerZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 44m, high: 50m, low: 40m, close: 46m,
            volume: 500_000m,
            startTime: baseTime.AddHours(5 + i))).ToArray();

        var anchor = KlineFactory.Create(
            open: 98m, high: 105m, low: 95m, close: 100m,
            volume: 1m,
            startTime: baseTime.AddHours(10));

        var klines = dominantZone.Concat(weakerZone).Append(anchor).ToArray();
        var result = VolumeProfileDetector.Detect(klines, new VolumeProfileOptions(hvnThresholdRatio: 0.4m));

        result.Support1.Should().NotBeNull(because: "Zone B ближе к цене → Support1");
        result.Support2.Should().NotBeNull(because: "Zone A дальше → Support2");

        // Zone A (доминирующая, 1M) дальше от текущей цены → Support2
        result.Support2!.Strength.Should().Be(1.0m,
            because: "доминирующая Zone A должна иметь Strength = 1.0");

        // Zone B (слабее, 500K) ближе → Support1
        result.Support1!.Strength.Should().BeInRange(0.4m, 0.99m,
            because: "Zone B (500K = 50% от 1M) должна иметь Strength < 1.0");

        result.Support1.Strength.Should().BeLessThan(result.Support2.Strength,
            because: "слабый кластер должен иметь меньшую силу, чем доминирующий");
    }

    [Fact]
    public void Strength_Is_Always_In_Range_0_To_1()
    {
        // Инвариант: Strength ∈ [0, 1] для любого обнаруженного уровня.
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        foreach (var level in new[] { result.Support1, result.Support2, result.Resistance1, result.Resistance2 })
        {
            if (level is { } l)
            {
                l.Strength.Should().BeInRange(0m, 1m,
                    because: "Strength должен быть в диапазоне [0, 1] после нормализации по maxClusterVolume");
            }
        }
    }
}
