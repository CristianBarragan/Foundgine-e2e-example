FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

# Restore local .NET tools (requires .config/dotnet-tools.json)
RUN dotnet tool restore

# Restore NuGet packages
RUN dotnet restore

# Create output directory
RUN mkdir -p /out

# Generate EF Core migration bundle
RUN dotnet ef migrations bundle \
    --configuration Release \
    --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

# Copy migration bundle
COPY --from=build /out/efbundle ./efbundle

# Copy migration startup script
COPY run-migrations.sh ./run-migrations.sh

# Set permissions
RUN chmod +x ./efbundle ./run-migrations.sh

ENTRYPOINT ["/app/run-migrations.sh"]