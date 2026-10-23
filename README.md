# DiscordBot

A Discord bot for the Cyberpunk 2077 modding community, built on the [Nexus Mods API](https://graphql.nexusmods.com/).

Its first feature is a **prediction market**, similar to Polymarket but with play money.
People bet on which mods will be released on Nexus Mods before a deadline. The bot then checks Nexus and pays out the winners.

> Everything runs on **eddies (€$)**, a play-money currency with no real-world value.
> You can't buy eddies or cash them out.

## How the market works

1. Someone opens a market, for example:
   `/market create duration:1w keyword:Johnny`
   → *"Will a new Cyberpunk 2077 mod with "Johnny" in the name be released?"*
2. The bot posts the market with **Bet YES** / **Bet NO** buttons and live odds.
3. Betting buys **shares**. Prices are set by an automated market maker ([LMSR](https://www.cultivatelabs.com/crowdsourced-forecasting-guide/how-does-logarithmic-market-scoring-rule-lmsr-work)):
   - A share's price is the crowd's current probability. At 30% YES, a YES share costs about 0.30 €$.
   - Each bet moves the price towards the side that was bought.
   - **Every winning share pays 1 €$**, so betting early on an unlikely outcome pays best.
   - You can sell shares back at the current price any time before the market closes.
4. The bot checks Nexus Mods every couple of minutes:
   - When enough matching mods have been **created** inside the market's window, the market resolves **YES** right away.
   - If the deadline passes without that, the market resolves **NO** (after a 15-minute grace period for late indexing).
5. Winners are paid and the result is posted with links to the matching mods.

Moderators can also run free-form markets ("Will CDPR announce a patch this month?") and resolve them by hand.

## Commands

| Command | Who | What it does |
|---|---|---|
| `/market create duration [keyword] [category] [author] [count]` | everyone | Open a Nexus market. Filters are case-insensitive. `count` is the number of matching mods needed for YES (default 1). |
| `/market list` | everyone | Running markets with their odds |
| `/market view market` | everyone | Re-post a market with its buttons |
| `/market buy market side amount` | everyone | Buy YES/NO shares (the buttons do the same thing) |
| `/market sell market side [shares]` | everyone | Sell shares back (all of them if `shares` is left empty) |
| `/wallet` | everyone | Your balance and open positions |
| `/daily` | everyone | +100 €$ once per UTC day |
| `/leaderboard` | everyone | Richest forecasters on the server |
| `/nexus latest [count]` | everyone | Newest Cyberpunk 2077 mods; handy for checking the Nexus connection |
| `/market-admin custom question duration` | Manage Server | Free-form market resolved by a moderator |
| `/market-admin resolve market outcome [note]` | Manage Server | Settle any market by hand |
| `/market-admin cancel market [reason]` | Manage Server | Cancel and refund everyone |

New users start with 1000 €$. Each user can have 3 open markets at a time; moderators have no limit.
Markets can run from 1 hour to 30 days. Each server has its own wallets and leaderboard.

## Running it

### 1. Create the Discord application

1. Go to <https://discord.com/developers/applications> → **New Application** → **Bot** → **Reset Token**, and copy the token.
2. Under **OAuth2 → URL Generator**, select the scopes `bot` and `applications.commands`, plus these bot permissions: *Send Messages*, *Embed Links*, *Read Message History*.
   Open the generated URL to invite the bot to your server.

No privileged gateway intents are needed.

### 2. Get a Nexus Mods API key (recommended)

Get a personal key at <https://next.nexusmods.com/settings/api-keys>.
Public mod queries also work without a key, but with a key you get proper rate limits.

### 3. Configure

Settings come from `src/DiscordBot/appsettings.json`, which you can override with environment variables (use `__` for nesting):

| Setting | Env variable | Default |
|---|---|---|
| Discord bot token (**required**) | `Discord__Token` | – |
| Nexus API key | `Nexus__ApiKey` | – |
| Game | `Nexus__GameDomain` / `Nexus__GameName` | `cyberpunk2077` / `Cyberpunk 2077` |
| Database file | `Database__Path` | `data/discordbot.db` (relative to the working directory) |
| Starting balance / daily bonus | `Market__StartingBalance` / `Market__DailyBonus` | `1000` / `100` |
| Liquidity (how much prices move per bet; higher means they move less) | `Market__Liquidity` | `300` |
| Duration limits | `Market__MinDuration` / `Market__MaxDuration` | `01:00:00` / `30.00:00:00` |
| Open markets per user | `Market__MaxOpenMarketsPerUser` | `3` |

For local development you can keep secrets out of the repo with user secrets:

```sh
dotnet user-secrets --project src/DiscordBot set "Discord:Token" "<token>"
dotnet user-secrets --project src/DiscordBot set "Nexus:ApiKey" "<key>"
```

(User secrets are only loaded when `DOTNET_ENVIRONMENT=Development`.)

### 4. Run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/DiscordBot
```

Or with Docker:

```sh
docker build -t discordbot .
docker run -d --name discordbot -e Discord__Token=... -e Nexus__ApiKey=... -v discordbot-data:/app/data discordbot
```

Slash commands are registered globally the first time the bot starts.

## Development

```sh
dotnet build
dotnet test
```

```
src/DiscordBot/
  Program.cs              host setup (NetCord gateway, DI, SQLite)
  Data/                   EF Core entities + DbContext (SQLite, created on first start)
  Markets/                LMSR pricing, MarketService (trades, payouts), background resolver
  Nexus/                  Nexus Mods v2 GraphQL client, mod tracker cache, market criteria
  Discord/                embeds/buttons (MarketView), message updates, command modules
tests/DiscordBot.Tests/   xUnit tests for pricing, markets, resolution and the Nexus client
```

Built with [NetCord](https://netcord.dev). It's still pre-1.0, so the package version is pinned exactly in `DiscordBot.csproj`; upgrade it on purpose.

The database schema is created with `EnsureCreated`. If you change the entities before launch, delete `data/discordbot.db`. Add EF Core migrations once there's data worth keeping.
