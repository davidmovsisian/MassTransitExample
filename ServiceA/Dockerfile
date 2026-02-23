FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ServiceA/ServiceA.csproj ServiceA/
RUN dotnet restore ServiceA/ServiceA.csproj
COPY ServiceA/ ServiceA/
RUN dotnet publish ServiceA/ServiceA.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ServiceA.dll"]
