namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Текстовая метка силы ценового уровня, вычисленная из нормализованного <c>strength</c>.
///
/// Whitelist допустимых значений V1:
/// <list type="table">
///   <listheader><term>Label</term><description>Условие</description></listheader>
///   <item><term>Strong</term>      <description>strength &gt;= 0.70</description></item>
///   <item><term>Moderate</term>    <description>strength &gt;= 0.40 &amp;&amp; strength &lt; 0.70</description></item>
///   <item><term>Weak</term>        <description>strength &lt; 0.40</description></item>
///   <item><term>Unavailable</term> <description>strength == null (источник не поддерживает оценку)</description></item>
/// </list>
/// </summary>
public enum LevelStrengthLabel
{
    /// <summary>Сильный уровень: strength &gt;= 0.70.</summary>
    Strong = 1,

    /// <summary>Умеренный уровень: strength &gt;= 0.40.</summary>
    Moderate = 2,

    /// <summary>Слабый уровень: strength &lt; 0.40.</summary>
    Weak = 3,

    /// <summary>Сила уровня недоступна: источник не поддерживает оценку.</summary>
    Unavailable = 4,
}
