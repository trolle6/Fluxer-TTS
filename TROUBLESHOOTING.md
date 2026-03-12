# FluxerTools Troubleshooting

## "Invalid session (resumable=False)"

The Fluxer gateway rejected the bot's authentication. Try these steps:

### 1. Regenerate your bot token

1. Go to [web.fluxer.app](https://web.fluxer.app) → **User Settings** (bottom left) → **Applications**
2. Select your application
3. Click **Regenerate** on the bot token
4. Copy the new token
5. Update `config.env` with the new `FLUXER_TOKEN`

### 2. Ensure the bot is invited to your community

Per [Fluxer Quickstart](https://docs.fluxer.app/quickstart):
- In Applications, check **bot** and copy the **Authorize URL**
- Open that URL in your browser
- Select your community and add the bot
- Without this step, the bot cannot connect

### 3. Confirm you're on the right Fluxer instance

- If you use **web.fluxer.app** (official): `api.fluxer.app` is correct
- If you use a **self-hosted** Fluxer: You need a different API URL (set `FLUXER_API_URL` in config and pass it to the bot)

### 4. Test with the Node.js quickstart

To rule out fluxer.py issues, try the [Fluxer Node.js quickstart](https://docs.fluxer.app/quickstart):
- If Node works with your token → likely a fluxer.py compatibility issue
- If Node also fails → token or invite is the problem
