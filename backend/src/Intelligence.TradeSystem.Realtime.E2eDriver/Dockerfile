FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src
COPY src/ .
RUN dotnet restore Intelligence.TradeSystem.Realtime.E2eDriver/Intelligence.TradeSystem.Realtime.E2eDriver.csproj
RUN dotnet publish Intelligence.TradeSystem.Realtime.E2eDriver/Intelligence.TradeSystem.Realtime.E2eDriver.csproj -c Release --no-restore -o /publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13
WORKDIR /app
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "Intelligence.TradeSystem.Realtime.E2eDriver.dll"]
