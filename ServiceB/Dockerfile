FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ServiceB/ServiceB.csproj ServiceB/
RUN dotnet restore ServiceB/ServiceB.csproj
COPY ServiceB/ ServiceB/
RUN dotnet publish ServiceB/ServiceB.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ServiceB.dll"]
