FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files and restore dependencies
COPY ["services/wallet/wallet.api/wallet.api.csproj", "services/wallet/wallet.api/"]
COPY ["services/wallet/wallet.application/wallet.application.csproj", "services/wallet/wallet.application/"]
COPY ["services/wallet/wallet.domain/wallet.domain.csproj", "services/wallet/wallet.domain/"]
COPY ["services/wallet/wallet.infrastructure/wallet.infrastructure.csproj", "services/wallet/wallet.infrastructure/"]

# Restore
RUN dotnet restore "services/wallet/wallet.api/wallet.api.csproj"

# Copy remaining source and publish
COPY . .

WORKDIR /src/services/wallet/wallet.api
RUN dotnet publish "wallet.api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS="http://+:8080" \
    ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "wallet.api.dll"]