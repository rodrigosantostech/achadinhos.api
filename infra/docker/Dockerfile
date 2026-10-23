FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Achadinhos.sln
RUN dotnet publish backend/Achadinhos.Api -c Release -o /out/api --no-restore /p:UseAppHost=false
RUN dotnet publish backend/Achadinhos.Worker -c Release -o /out/worker --no-restore /p:UseAppHost=false
RUN dotnet tool restore
RUN dotnet ef migrations bundle --project backend/Achadinhos.Infrastructure --startup-project backend/Achadinhos.Api --configuration Release --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Achadinhos.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "Achadinhos.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrate
WORKDIR /app
COPY --from=build /out/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]