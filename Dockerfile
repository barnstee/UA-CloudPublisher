#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443
EXPOSE 50000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["UA-CloudPublisher.csproj", "."]
ENV DOTNET_EnableWriteXorExecute=0
RUN dotnet restore "./UA-CloudPublisher.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "UA-CloudPublisher.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UA-CloudPublisher.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UA-CloudPublisher.dll"]