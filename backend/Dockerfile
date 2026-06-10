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

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ShelfGuard.Api.dll"]
