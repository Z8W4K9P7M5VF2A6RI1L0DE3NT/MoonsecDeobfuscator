# STAGE 1: Build
# Using the full SDK to restore and publish
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy solution and project files first to cache dependencies
COPY *.sln ./
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the source code and build
COPY . ./
RUN dotnet publish -c Release -o /app/out /p:UseAppHost=false

# STAGE 2: Runtime
# Using a slim runtime for a tiny final image size
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app

# Copy the compiled files from the build stage
COPY --from=build /app/out ./

# Use ENTRYPOINT for more reliable execution on Render
# REPLACE 'MoonsecDeobfuscator.dll' with your ACTUAL .csproj name if different!
ENTRYPOINT ["dotnet", "MoonsecDeobfuscator.dll"]
