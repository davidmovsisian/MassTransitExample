FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ServiceB_RMQ/ServiceB_RMQ.csproj ServiceB_RMQ/
RUN dotnet restore ServiceB_RMQ/ServiceB_RMQ.csproj
COPY ServiceB_RMQ/ ServiceB_RMQ/
RUN dotnet publish ServiceB_RMQ/ServiceB_RMQ.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ServiceB_RMQ.dll"]
