FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app
EXPOSE 80

FROM base AS baseWithEnv
RUN mkdir -p /usr/share/man/man1
RUN apt-get update && apt-get -y install texlive-base
RUN apt-get update && apt-get -y install default-jre libreoffice-java-common libreoffice-writer

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["DocumentTextExtractorApi.csproj", ""]
RUN dotnet restore "./DocumentTextExtractorApi.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "DocumentTextExtractorApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DocumentTextExtractorApi.csproj" -c Release -o /app/publish

FROM baseWithEnv AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DocumentTextExtractorApi.dll"]
