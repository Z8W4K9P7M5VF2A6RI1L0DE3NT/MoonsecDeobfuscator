# STAGE 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy files and restore
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the source code and build
COPY . ./
RUN dotnet publish -c Release -o /app/out /p:UseAppHost=false

# STAGE 2: Runtime
# IMPORTANT: Changed to aspnet to support the Port 3000 web listener
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy the compiled files from the build stage
COPY --from=build /app/out ./

# Set the Environment to Production
ENV DOTNET_ENVIRONMENT=Production

# Expose the port Render uses
EXPOSE 3000

# Ensure the DLL name matches your actual output name (MoonsecBot.dll)
ENTRYPOINT ["dotnet", "MoonsecBot.dll"]
