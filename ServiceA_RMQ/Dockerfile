FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ServiceA_RMQ/ServiceA_RMQ.csproj ServiceA_RMQ/
RUN dotnet restore ServiceA_RMQ/ServiceA_RMQ.csproj
COPY ServiceA_RMQ/ ServiceA_RMQ/
RUN dotnet publish ServiceA_RMQ/ServiceA_RMQ.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ServiceA_RMQ.dll"]
