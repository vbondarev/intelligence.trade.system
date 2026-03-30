FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Intelligence.TradeSystem.Backend.Host/Intelligence.Trade.SystemBackend.Host.csproj", "Intelligence.TradeSystem.Backend.Host/"]
RUN dotnet restore "Intelligence.TradeSystem.Backend.Host/Intelligence.TradeSystem.Backend.Host.csproj"
COPY . .
WORKDIR "/src/Intelligence.TradeSystem.Backend.Host"
RUN dotnet build "./Intelligence.TradeSystem.Backend.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Intelligence.TradeSystem.Backend.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Intelligence.TradeSystem.Backend.Host.dll"]
