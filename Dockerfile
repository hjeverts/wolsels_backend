FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Wolsels.Api/Wolsels.Api.csproj src/Wolsels.Api/
RUN dotnet restore src/Wolsels.Api/Wolsels.Api.csproj
COPY src/ src/
RUN dotnet publish src/Wolsels.Api/Wolsels.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Wolsels="Data Source=/data/wolsels.db"
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Wolsels.Api.dll"]
