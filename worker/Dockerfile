FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY ["PlayOffs.Worker.csproj", "./"]
RUN dotnet restore "PlayOffs.Worker.csproj"

COPY . .
RUN dotnet publish "PlayOffs.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PlayOffs.Worker.dll"]