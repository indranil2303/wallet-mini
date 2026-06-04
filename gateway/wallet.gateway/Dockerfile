FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files and restore dependencies
COPY ["gateway/wallet.gateway/wallet.gateway.csproj", "gateway/wallet.gateway/"]
COPY ["services/wallet/wallet.domain/wallet.domain.csproj", "services/wallet/wallet.domain/"]
COPY ["services/wallet/wallet.infrastructure/wallet.infrastructure.csproj", "services/wallet/wallet.infrastructure/"]
COPY ["services/wallet/wallet.application/wallet.application.csproj", "services/wallet/wallet.application/"]

RUN dotnet restore "gateway/wallet.gateway/wallet.gateway.csproj"

# Copy remaining source and publish
COPY ["gateway/wallet.gateway/", "gateway/wallet.gateway/"]
COPY ["services/wallet/", "services/wallet/"]

WORKDIR /src/gateway/wallet.gateway
RUN dotnet publish "wallet.gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

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

ENTRYPOINT ["dotnet", "wallet.gateway.dll"]