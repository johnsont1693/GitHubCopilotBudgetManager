# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/BudgetManager.Api/BudgetManager.Api.csproj --locked-mode
RUN dotnet publish src/BudgetManager.Api/BudgetManager.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "BudgetManager.Api.dll"]