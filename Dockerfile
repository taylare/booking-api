# Build image. Compiles and publishes the API using the full .NET SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project file first so the package restore layer is cached
# and only re-runs when dependencies change
COPY src/BookingApi/BookingApi.csproj src/BookingApi/
RUN dotnet restore src/BookingApi/BookingApi.csproj

# Copy the rest of the source and publish an optimised release build
COPY src/BookingApi/ src/BookingApi/
RUN dotnet publish src/BookingApi/BookingApi.csproj -c Release -o /app/publish --no-restore

# Runtime image. Much smaller, contains only what is needed to run the API.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Listen on port 8080 inside the container and start the API
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookingApi.dll"]
