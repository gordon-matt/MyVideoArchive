# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.
# Slim Alpine-based image for production.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MyVideoArchive/MyVideoArchive.csproj", "MyVideoArchive/"]
RUN dotnet restore "./MyVideoArchive/MyVideoArchive.csproj"
COPY . .
WORKDIR "/src/MyVideoArchive"
# Restore client-side libraries (wwwroot/lib). Build package may not run in Docker, so run CLI explicitly.
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli --version 2.1.113 && \
    export PATH="${PATH}:/root/.dotnet/tools" && \
    libman restore --verbose
RUN test -d wwwroot/lib && test -f wwwroot/lib/jquery/dist/jquery.min.js || (echo "ERROR: wwwroot/lib missing; LibMan restore may have failed." && exit 1)
RUN dotnet build "./MyVideoArchive.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MyVideoArchive.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
# Ensure wwwroot/lib is in the publish output (publish can omit content added after restore)
RUN cp -r /src/MyVideoArchive/wwwroot/lib /app/publish/wwwroot/
# Scoped CSS (Razor) may be at publish root; copy to wwwroot so UseStaticFiles can serve it
RUN if [ -f /app/publish/MyVideoArchive.styles.css ]; then cp /app/publish/MyVideoArchive.styles.css /app/publish/wwwroot/; fi

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyVideoArchive.dll"]