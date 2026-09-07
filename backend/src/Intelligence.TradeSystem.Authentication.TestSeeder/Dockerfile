FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY src/ . 

RUN dotnet restore Intelligence.TradeSystem.Authentication.TestSeeder/Intelligence.TradeSystem.Authentication.TestSeeder.csproj

RUN dotnet publish Intelligence.TradeSystem.Authentication.TestSeeder/Intelligence.TradeSystem.Authentication.TestSeeder.csproj \
    --configuration $BUILD_CONFIGURATION \
    --no-restore \
    --output /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Intelligence.TradeSystem.Authentication.TestSeeder.dll"]
