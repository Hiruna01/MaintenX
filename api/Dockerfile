# The API's image for Render, which has no native .NET runtime. Build context is api/.
#
# The runtime is the Debian aspnet image, deliberately not Alpine or chiseled: Program.cs
# resolves Scheduling:TimeZone (Asia/Colombo) at startup and needs tzdata and ICU, which
# those smaller images leave out.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CampusFacilities.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish CampusFacilities.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
# The image's non-root user.
USER app
# Render tells the service which port to listen on through PORT; 8080 is the .NET 8
# container default when it does not.
CMD ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet CampusFacilities.Api.dll"]
