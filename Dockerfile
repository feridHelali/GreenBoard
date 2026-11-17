# syntax=docker/dockerfile:1
# Multi-stage build pour InfiniteCanvas.Api
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copie les fichiers de projet pour la restauration des d\u00e9pendances
COPY ["InfiniteCanvas.Api/InfiniteCanvas.Api.csproj", "InfiniteCanvas.Api/"]
COPY ["InfiniteCanvas.Core/InfiniteCanvas.Core.csproj", "InfiniteCanvas.Core/"]
COPY ["InfiniteCanvas.Infrastructure/InfiniteCanvas.Infrastructure.csproj", "InfiniteCanvas.Infrastructure/"]
RUN dotnet restore "InfiniteCanvas.Api/InfiniteCanvas.Api.csproj"

# Copie le reste du code et publie
COPY . .
WORKDIR "/src/InfiniteCanvas.Api"
RUN dotnet publish "InfiniteCanvas.Api.csproj" -c Release -o /app/publish --no-restore

# Image runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
EXPOSE 8080

# D\u00e9pendances syst\u00e8me pour EF Core / Npgsql
RUN apt-get update && apt-get install -y \
    libicu-dev \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InfiniteCanvas.Api.dll"]
