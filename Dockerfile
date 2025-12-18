# STAGE 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the source code
COPY . ./

# Publish targeting the project file specifically to avoid Solution-level errors
RUN dotnet publish MoonsecDeobfuscator.csproj -c Release -o /app/out /p:UseAppHost=false

# STAGE 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

# Set the port to 3000
EXPOSE 3000
ENV ASPNETCORE_URLS=http://+:3000

ENTRYPOINT ["dotnet", "MoonsecDeobfuscator.dll"]
