# BallBank web

Next.js front end for BallBank. Ships as static files (`output: "export"`) and talks to the API at `NEXT_PUBLIC_API_URL`.

```bash
npm install
cp .env.example .env.local   # or run everything through the Aspire AppHost
npm run dev
```

`npm run lint` and `npm run build` must pass before a PR. See the root [README](../README.md) for the whole system.
