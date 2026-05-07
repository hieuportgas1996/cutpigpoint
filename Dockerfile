# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

COPY CutPig/CutPig.csproj CutPig/
RUN dotnet restore CutPig/CutPig.csproj

COPY CutPig/ CutPig/
RUN dotnet publish CutPig/CutPig.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "CutPig.dll"]
