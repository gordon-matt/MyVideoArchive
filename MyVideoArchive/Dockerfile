# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.
# .NET 10 Linux images: Ubuntu Noble (10.0-noble) only — Debian is not supported in .NET 10.

# Base stage: used directly by Visual Studio (F5 / fast mode debug).
# Must be Ubuntu/glibc because yt-dlp and python3 are installed here.
# Alpine cannot be used: yt-dlp requires glibc (musl incompatible).
# .NET 10 dropped Debian; Ubuntu Noble (10.0-noble) is the glibc-based Linux target.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS base
# Set PATH at image level so every subprocess (including those yt-dlp spawns) can find python3.
ENV PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
RUN apt-get update \
  && apt-get install -y --no-install-recommends python3 python3-pip ffmpeg \
  && pip3 install --break-system-packages --no-cache-dir "yt-dlp[default]" \
  && rm -rf /var/lib/apt/lists/*
# Verify yt-dlp is present and runnable — build fails here if something went wrong.
RUN test -x /usr/local/bin/yt-dlp && /usr/local/bin/yt-dlp --version
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build stage
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

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MyVideoArchive.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
# Ensure wwwroot/lib is in the publish output (publish can omit content added after restore)
RUN cp -r /src/MyVideoArchive/wwwroot/lib /app/publish/wwwroot/
# Scoped CSS (Razor) may be at publish root; copy to wwwroot so UseStaticFiles can serve it
RUN if [ -f /app/publish/MyVideoArchive.styles.css ]; then cp /app/publish/MyVideoArchive.styles.css /app/publish/wwwroot/; fi

# Final stage: inherits python3 + yt-dlp + ffmpeg from base (Ubuntu Noble).
# Used by both docker compose and Visual Studio in regular (non-fast) mode.
FROM base AS final
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyVideoArchive.dll"]