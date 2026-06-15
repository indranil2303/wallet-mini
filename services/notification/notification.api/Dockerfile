FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["services/wallet/wallet.domain/wallet.domain.csproj", "services/wallet/wallet.domain/"]
COPY ["services/notification/notification.api/notification.api.csproj", "services/notification/notification.api/"]
COPY ["shared/wallet.messaging/wallet.messaging.csproj", "shared/wallet.messaging/"]
COPY ["shared/wallet.telemetry/wallet.telemetry.csproj", "shared/wallet.telemetry/"]

RUN dotnet restore "services/notification/notification.api/notification.api.csproj"

COPY . .

WORKDIR /src/services/notification/notification.api
RUN dotnet build "notification.api.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "notification.api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS="http://+:8080" \
    ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "notification.api.dll"]