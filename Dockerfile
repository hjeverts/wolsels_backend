FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Wolsels.Api/Wolsels.Api.csproj src/Wolsels.Api/
RUN dotnet restore src/Wolsels.Api/Wolsels.Api.csproj
COPY src/ src/
RUN dotnet publish src/Wolsels.Api/Wolsels.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Pick up Ubuntu security updates (rebuild with ./scripts/update.sh --force).
RUN apt-get update && apt-get upgrade -y --no-install-recommends && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Wolsels="Data Source=/data/wolsels.db"
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID
EXPOSE 8080
# The aspnet image has no curl/wget; bash can make an HTTP request itself.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\\r\\n\\r\\n' >&3 && grep -q Healthy <&3"]
ENTRYPOINT ["dotnet", "Wolsels.Api.dll"]
