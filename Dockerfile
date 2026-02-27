# =========================
# Etapa 1: build
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar csproj y restaurar dependencias
COPY Gateway.csproj ./
RUN dotnet restore

# Copiar todo el código
COPY . ./

# Publicar la app
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# =========================
# Etapa 2: runtime
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Puerto estándar para Render / Railway
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Copiar build final
COPY --from=build /app/publish .

# Ejecutar gateway
ENTRYPOINT ["dotnet", "Gateway.dll"]