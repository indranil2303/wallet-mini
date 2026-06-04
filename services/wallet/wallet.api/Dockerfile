# Use the .NET SDK image to restore and publish the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files and restore dependencies
COPY ["wallet.api/wallet.api.csproj", "wallet.api/"]
COPY ["wallet.application/wallet.application.csproj", "wallet.application/"]
COPY ["wallet.domain/wallet.domain.csproj", "wallet.domain/"]
COPY ["wallet.infrastructure/wallet.infrastructure.csproj", "wallet.infrastructure/"]

RUN dotnet restore "wallet.api/wallet.api.csproj"

# Copy remaining source and publish
COPY ["wallet.api/", "wallet.api/"]
COPY ["wallet.application/", "wallet.application/"]
COPY ["wallet.domain/", "wallet.domain/"]
COPY ["wallet.infrastructure/", "wallet.infrastructure/"]

WORKDIR /src/wallet.api
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