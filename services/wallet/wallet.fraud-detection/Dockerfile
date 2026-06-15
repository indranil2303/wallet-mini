FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 3. Copy only .csproj files first to leverage Docker layer caching
COPY ["services/wallet/wallet.fraud-detection/wallet.fraud-detection.csproj", "services/wallet/wallet.fraud-detection/"]
COPY ["services/wallet/wallet.domain/wallet.domain.csproj", "services/wallet/wallet.domain/"]
COPY ["services/wallet/wallet.application/wallet.application.csproj", "services/wallet/wallet.application/"]
COPY ["services/wallet/wallet.infrastructure/wallet.infrastructure.csproj", "services/wallet/wallet.infrastructure/"]
COPY ["shared/wallet.messaging/wallet.messaging.csproj", "shared/wallet.messaging/"]
COPY ["shared/wallet.telemetry/wallet.telemetry.csproj", "shared/wallet.telemetry/"]

# Restore dependencies
RUN dotnet restore "services/wallet/wallet.fraud-detection/wallet.fraud-detection.csproj"

COPY . .

WORKDIR /src/services/wallet/wallet.fraud-detection
RUN dotnet publish "wallet.fraud-detection.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "wallet.fraud-detection.dll"]