# Build context — корень репозитория: BFF image объединяет backend/src и frontend artifact.
FROM node:24-alpine AS frontend
WORKDIR /frontend

COPY frontend/intelligence-trade-web/package.json frontend/intelligence-trade-web/package-lock.json ./
RUN npm ci

COPY frontend/intelligence-trade-web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY backend/src/ .

RUN dotnet restore Intelligence.TradeSystem.Bff/Intelligence.TradeSystem.Bff.csproj

RUN dotnet publish Intelligence.TradeSystem.Bff/Intelligence.TradeSystem.Bff.csproj \
    --configuration $BUILD_CONFIGURATION \
    --no-restore \
    -p:BuildClientAssets=false \
    --output /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=frontend /frontend/dist ./wwwroot
ENTRYPOINT ["dotnet", "Intelligence.TradeSystem.Bff.dll"]
