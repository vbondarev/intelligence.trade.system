using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1720:Identifiers should not contain type names",
    Justification = "Long является устоявшимся финансовым термином предметной области трейдинга.",
    Scope = "member",
    Target = "~F:Intelligence.TradeSystem.Domain.Snapshots.PositionSide.Long")]

[assembly: SuppressMessage(
    "Naming",
    "CA1720:Identifiers should not contain type names",
    Justification = "Short является устоявшимся финансовым термином предметной области трейдинга.",
    Scope = "member",
    Target = "~F:Intelligence.TradeSystem.Domain.Snapshots.PositionSide.Short")]

