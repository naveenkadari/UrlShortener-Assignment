# ---- build stage: full SDK, compiles and publishes ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY UrlShortener/UrlShortener.csproj UrlShortener/
RUN dotnet restore UrlShortener/UrlShortener.csproj
COPY UrlShortener/ UrlShortener/
RUN dotnet publish UrlShortener/UrlShortener.csproj -c Release -o /app --no-restore

# ---- runtime stage: small image, no SDK, no source ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown app:app /data
USER app
ENV ConnectionStrings__Default="Data Source=/data/urls.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "UrlShortener.dll"]
