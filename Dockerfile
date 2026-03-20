FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/RCDragLiveServer/RCDragLiveServer.csproj src/RCDragLiveServer/
RUN dotnet restore src/RCDragLiveServer/RCDragLiveServer.csproj

COPY . .
RUN dotnet publish src/RCDragLiveServer/RCDragLiveServer.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RCDragLiveServer.dll"]