# Secret Santa Commands (FluxerTools)

Ported from [WaveTechToolBoxx](https://github.com/trolle6/WaveTechToolBoxx). Uses prefix commands (`!ss`).

## Moderator

| Command | Description |
|---------|-------------|
| `!ss start` | Start a new event |
| `!ss shuffle` | Make assignments; DMs sent to participants |
| `!ss stop` | End event and archive to `archive/YYYY.json` |
| `!ss participants` | List current participants |
| `!ss view_gifts` | View submitted gifts |
| `!ss view_comms` | View anonymous Q&A threads |

## Participant

| Command | Description |
|---------|-------------|
| `!ss join` | Sign up |
| `!ss leave` | Leave before shuffle |
| `!ss wishlist add <item>` | Add wishlist item |
| `!ss wishlist remove <n>` | Remove item #n |
| `!ss wishlist view` | View your wishlist |
| `!ss wishlist clear` | Clear wishlist |
| `!ss giftee` | See your giftee's wishlist |
| `!ss ask_giftee <question>` | Ask giftee anonymously (DM) |
| `!ss reply_santa <reply>` | Reply to your Santa (DM) |
| `!ss submit_gift <description>` | Record what you gave |

## Anyone

| Command | Description |
|---------|-------------|
| `!ss help` | Show help |
| `!ss history` | List archived years |
| `!ss history <year>` | View year details |
| `!ss user_history @user` | User's participation years |
| `!ss edit_gift <year> <description>` | Edit your past gift |

## Data

- `cogs/secret_santa_state.json` – Active event
- `cogs/archive/YYYY.json` – Archived years
