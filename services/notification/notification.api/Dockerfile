FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files and restore dependencies
COPY ["notification.api/notification.api.csproj", "notification.api/"]
COPY ["notification.consumer/notification.consumer.csproj", "notification.consumer/"]

RUN dotnet restore "notification.api/notification.api.csproj"

# Copy remaining source and publish
COPY ["notification.api/", "notification.api/"]
COPY ["notification.consumer/", "notification.consumer/"]

WORKDIR /src/notification.api
RUN dotnet publish "notification.api.csproj" -c Release -o /app/publish /p:UseAppHost=false

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
ENTRYPOINT ["dotnet", "notification.api.dll"]