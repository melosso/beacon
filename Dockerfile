FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY Source/Beacon.sln Source/
COPY Source/Beacon/Beacon.csproj Source/Beacon/
COPY Source/Beacon.Core/Beacon.Core.csproj Source/Beacon.Core/
COPY Source/Beacon.Storage/Beacon.Storage.csproj Source/Beacon.Storage/
COPY Source/Beacon.Tokens/Beacon.Tokens.csproj Source/Beacon.Tokens/

# Restore dependencies
RUN dotnet restore Source/Beacon.sln

# Copy remaining source code
COPY Source/ Source/

# Build and publish
RUN dotnet publish Source/Beacon/Beacon.csproj -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r beacon && useradd -r -g beacon beacon

# Copy published output
COPY --from=build /app/publish .

# Set ownership and switch to non-root user
RUN chown -R beacon:beacon /app
USER beacon

# Expose default ASP.NET Core port
EXPOSE 8080

ENTRYPOINT ["dotnet", "Beacon.dll"]
