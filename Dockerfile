FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Maps.csproj .
RUN dotnet restore Maps.csproj

COPY . .
RUN dotnet publish Maps.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# SkiaSharp deps + Microsoft TrueType core fonts (Arial, etc.) via ttf-mscorefonts-installer
RUN echo "deb http://deb.debian.org/debian bookworm contrib non-free" \
        > /etc/apt/sources.list.d/contrib.list \
    && echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula boolean true" \
        | debconf-set-selections \
    && apt-get update && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 \
        fontconfig \
        ttf-mscorefonts-installer \
    && rm -rf /var/lib/apt/lists/* \
    && fc-cache -f

COPY --from=build /app/publish .
COPY res/ res/

# Frontend static files served from content root (GetCurrentDirectory = /app)
COPY borders/ borders/
COPY lib/ lib/
COPY make/ make/
COPY print/ print/
COPY doc/ doc/
COPY 404.html favicon.ico favicon.svg index.css index.html index.js \
     map.js offline.html redir.html robots.txt site.css sitemap.xml sw.js \
     world_util.js ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Maps.dll"]
