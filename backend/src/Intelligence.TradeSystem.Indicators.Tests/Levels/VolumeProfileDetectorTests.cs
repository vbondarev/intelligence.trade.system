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
        var kline  = KlineFactory.Create(open: 100m, high: 100m, low: 100m, close: 100m);
        var result = VolumeProfileDetector.Detect([kline]);

        result.Support1.Should().BeNull();
        result.Support2.Should().BeNull();
        result.Resistance1.Should().BeNull();
        result.Resistance2.Should().BeNull();
    }

    [Fact]
    public void Support_Levels_Are_Below_Current_Price()
    {
        var klines      = KlineFactory.CreateSeries(50).ToArray();
        var result      = VolumeProfileDetector.Detect(klines);
        var currentPrice = klines[^1].Close;

        if (result.Support1 is { } s1a)
            s1a.Should().BeLessThan(currentPrice);

        if (result.Support2 is { } s2a)
            s2a.Should().BeLessThan(currentPrice);
    }

    [Fact]
    public void Resistance_Levels_Are_Above_Current_Price()
    {
        var klines       = KlineFactory.CreateSeries(50).ToArray();
        var result       = VolumeProfileDetector.Detect(klines);
        var currentPrice = klines[^1].Close;

        if (result.Resistance1 is { } r1a)
            r1a.Should().BeGreaterThan(currentPrice);

        if (result.Resistance2 is { } r2a)
            r2a.Should().BeGreaterThan(currentPrice);
    }

    [Fact]
    public void Support1_Is_Closer_To_Price_Than_Support2()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        if (result.Support1 is { } s1 && result.Support2 is { } s2)
            s1.Should().BeGreaterThan(s2);
    }

    [Fact]
    public void Resistance1_Is_Closer_To_Price_Than_Resistance2()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        if (result.Resistance1 is { } r1 && result.Resistance2 is { } r2)
            r1.Should().BeLessThan(r2);
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
        var isInHighVolumeZone = (result.Support1 >= 45m && result.Support1 <= 55m)
                              || (result.Support2 >= 45m && result.Support2 <= 55m);

        isInHighVolumeZone.Should().BeTrue(
            because: "высокообъёмная ценовая зона (45–55) должна быть определена как уровень поддержки");
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

        var first  = VolumeProfileDetector.Detect(klines);
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

        result.Support1.Should().BeInRange(20m, 31m,
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
            Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
                open:  mid - 2m, high: mid + 5m, low: mid - 5m, close: mid,
                volume: 1_000_000m,
                startTime: baseTime.AddHours(hourOffset + i))).ToArray();

        var klines = MakeZone(mid: 85m, hourOffset:  0)   // ближняя  (~80–90)
            .Concat(MakeZone(mid: 55m, hourOffset:  5))   // средняя  (~50–60)
            .Concat(MakeZone(mid: 25m, hourOffset: 10))   // дальняя  (~20–30)
            .Append(KlineFactory.Create(
                open: 99m, high: 102m, low: 98m, close: 100m,
                volume: 10m,
                startTime: baseTime.AddHours(15)))
            .ToArray();

        var result = VolumeProfileDetector.Detect(klines);

        // S1 = ближайшая зона ≈ 85; границы совпадают с физическим диапазоном зоны [80, 90]
        result.Support1.Should().BeInRange(80m, 90m,
            because: "Support1 должна быть ближайшей к цене поддержкой (~85)");
        // S2 = вторая по близости ≈ 55; границы совпадают с физическим диапазоном зоны [50, 60]
        result.Support2.Should().BeInRange(50m, 60m,
            because: "Support2 должна быть второй по близости поддержкой (~55)");
        // Дальняя зона (~25) не должна попасть ни в один слот
        result.Support1.Should().BeGreaterThan(35m);
        result.Support2.Should().BeGreaterThan(35m);
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
            Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
                open:  mid - 2m, high: mid + 5m, low: mid - 5m, close: mid,
                volume: 1_000_000m,
                startTime: baseTime.AddHours(hourOffset + i))).ToArray();

        var klines = MakeZone(mid: 115m, hourOffset:  0)   // ближняя  (~110–120)
            .Concat(MakeZone(mid: 145m, hourOffset:  5))   // средняя  (~140–150)
            .Concat(MakeZone(mid: 175m, hourOffset: 10))   // дальняя  (~170–180)
            .Append(KlineFactory.Create(
                open: 99m, high: 102m, low: 98m, close: 100m,
                volume: 10m,
                startTime: baseTime.AddHours(15)))
            .ToArray();

        var result = VolumeProfileDetector.Detect(klines);

        // R1 = ближайшая зона ≈ 115; границы совпадают с физическим диапазоном зоны [110, 120]
        result.Resistance1.Should().BeInRange(110m, 120m,
            because: "Resistance1 должна быть ближайшим к цене сопротивлением (~115)");
        // R2 = вторая по близости ≈ 145; границы совпадают с физическим диапазоном зоны [140, 150]
        result.Resistance2.Should().BeInRange(140m, 150m,
            because: "Resistance2 должна быть вторым по близости сопротивлением (~145)");
        // Дальняя зона (~175) не должна попасть ни в один слот
        result.Resistance1.Should().BeLessThan(135m);
        result.Resistance2.Should().BeLessThan(165m);
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
        var isInHighVolumeZone = (result.Resistance1 >= 145m && result.Resistance1 <= 155m)
                              || (result.Resistance2 >= 145m && result.Resistance2 <= 155m);

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

        result.Support1.Should().BeInRange(40m, 51m,
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

        result.Resistance1.Should().BeInRange(140m, 151m,
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
        result.Support1.Should().BeInRange(40m, 51m,
            because: "Zone B (40–50) ближе к текущей цене → должна стать Support1");
        result.Support2.Should().BeInRange(20m, 31m,
            because: "Zone A (20–30) дальше от текущей цены → должна стать Support2");
        result.Support2.Should().NotBeNull(
            because: "две раздельные HVN-зоны не должны быть склеены в один кластер");
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
            open:  62m + i * 4m, high: 65m + i * 4m,
            low:   60m + i * 4m, close: 63m + i * 4m,
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
        result.Support1.Should().BeInRange(20m, 31m,
            because: "доминирующая high-volume зона должна быть выбрана как Support1, а не шумовой бакет");
        // Ни один шумовой бакет (начинаются с ≥ 60) не образовал второй кластер
        result.Support2.Should().BeNull(
            because: "шумовые бакеты не преодолевают HVN-порог → единственный support кластер");
    }
}
