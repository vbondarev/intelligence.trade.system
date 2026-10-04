FROM node:24-alpine AS client
WORKDIR /client

COPY src/Intelligence.TradeSystem.Web/ClientApp/package.json src/Intelligence.TradeSystem.Web/ClientApp/package-lock.json ./
RUN npm ci

COPY src/Intelligence.TradeSystem.Web/ClientApp/ ./
RUN npm run build -- --outDir /client/dist --emptyOutDir

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY src/ .

RUN dotnet restore Intelligence.TradeSystem.Web/Intelligence.TradeSystem.Web.csproj

RUN dotnet publish Intelligence.TradeSystem.Web/Intelligence.TradeSystem.Web.csproj \
    --configuration $BUILD_CONFIGURATION \
    --no-restore \
    -p:BuildClientAssets=false \
    --output /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=client /client/dist ./wwwroot
ENTRYPOINT ["dotnet", "Intelligence.TradeSystem.Web.dll"]
