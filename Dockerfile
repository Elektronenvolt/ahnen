# Build the TypeScript bundle
FROM node:22-alpine AS client
WORKDIR /src
COPY Ahnen.Web/package.json Ahnen.Web/package-lock.json ./
RUN npm ci
COPY Ahnen.Web/ClientSrc ./ClientSrc
COPY Ahnen.Web/tsconfig.json ./
RUN mkdir -p wwwroot/js && npm run build

# Build the .NET app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Ahnen.Web/Ahnen.Web.csproj Ahnen.Web/
RUN dotnet restore Ahnen.Web/Ahnen.Web.csproj
COPY Ahnen.Web Ahnen.Web
COPY --from=client /src/wwwroot/js Ahnen.Web/wwwroot/js
# the client bundle is prebuilt; skip the npm MSBuild target
RUN dotnet publish Ahnen.Web/Ahnen.Web.csproj -c Release -o /app /p:BuildClientSkip=true

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
VOLUME /app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ahnen.Web.dll"]
