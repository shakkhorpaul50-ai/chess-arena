# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["WebApplication1.csproj", "."]
RUN dotnet restore "./WebApplication1.csproj"
COPY . .
RUN dotnet build "./WebApplication1.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WebApplication1.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Downloads the official Stockfish binary for Linux x64
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS stockfish
ARG STOCKFISH_VERSION=17.1
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /stockfish \
    && curl -fsSL "https://github.com/official-stockfish/Stockfish/releases/download/sf_${STOCKFISH_VERSION}/stockfish-ubuntu-x86-64.tar" -o /tmp/stockfish.tar \
    && tar -xf /tmp/stockfish.tar -C /stockfish --strip-components=1 \
    && find /stockfish -name 'stockfish*' -type f -executable | head -n 1 | xargs -I{} cp {} /stockfish/stockfish \
    && chmod +x /stockfish/stockfish

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=stockfish /stockfish/stockfish ./Stockfish/stockfish
RUN chmod +x ./Stockfish/stockfish
ENTRYPOINT ["dotnet", "WebApplication1.dll"]