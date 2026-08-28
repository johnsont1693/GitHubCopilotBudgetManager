# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/BudgetManager.Worker/BudgetManager.Worker.csproj --locked-mode
RUN dotnet publish src/BudgetManager.Worker/BudgetManager.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra AS final
WORKDIR /app

ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "BudgetManager.Worker.dll"]