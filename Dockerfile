FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/DiscordBot/DiscordBot.csproj src/DiscordBot/
RUN dotnet restore src/DiscordBot/DiscordBot.csproj
COPY src/ src/
RUN dotnet publish src/DiscordBot/DiscordBot.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# The SQLite database lives in /app/data; mount a volume there to keep it across restarts.
VOLUME /app/data
ENTRYPOINT ["dotnet", "DiscordBot.dll"]
