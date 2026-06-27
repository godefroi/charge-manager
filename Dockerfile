# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy the solution and project files
COPY charge-manager.slnx ./
COPY src/ChargeManager/ChargeManager.csproj ./ChargeManager/

# Restore dependencies
RUN dotnet restore ./ChargeManager/ChargeManager.csproj

# Copy the source code
COPY src/ChargeManager/ ./ChargeManager/

# Build the application
RUN dotnet build ./ChargeManager/ChargeManager.csproj -c Release -o /app/build

# Publish the application
RUN dotnet publish ./ChargeManager/ChargeManager.csproj -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0

WORKDIR /app

# Copy the published application from the build stage
COPY --from=build /app/publish .

# Create a non-root user for running the application
RUN useradd -m -u 1000 chargemanager && chown -R chargemanager:chargemanager /app
USER chargemanager

# Run the application
ENTRYPOINT ["dotnet", "ChargeManager.dll"]
