FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["ShelfGuard.Api/ShelfGuard.Api.csproj", "ShelfGuard.Api/"]
COPY ["ShelfGuard.Application/ShelfGuard.Application.csproj", "ShelfGuard.Application/"]
COPY ["ShelfGuard.Domain/ShelfGuard.Domain.csproj", "ShelfGuard.Domain/"]
COPY ["ShelfGuard.Infrastructure/ShelfGuard.Infrastructure.csproj", "ShelfGuard.Infrastructure/"]

COPY . .
RUN dotnet publish "ShelfGuard.Api/ShelfGuard.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 5100

ENV ASPNETCORE_URLS=http://+:5100
ENV ASPNETCORE_ENVIRONMENT=Production

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# start-period covers EF migrations that run at startup
HEALTHCHECK --interval=15s --timeout=5s --start-period=90s --retries=3 \
  CMD curl -fsS http://localhost:5100/health/live || exit 1

ENTRYPOINT ["dotnet", "ShelfGuard.Api.dll"]
