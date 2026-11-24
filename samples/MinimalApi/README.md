# Minimal API sample

The smallest useful PingDrop app: `AddPingDrop`, `MapPingDrop`, three send endpoints and a static page.

```
dotnet run --project samples/MinimalApi
```

Open the printed URL with `?userId=alice` (for example http://localhost:5000/?userId=alice), then in another terminal:

```
curl -X POST "http://localhost:5000/broadcast?text=hello+everyone"
curl -X POST "http://localhost:5000/send/alice?text=just+for+alice"
curl -X POST "http://localhost:5000/send-many?users=alice&users=bob&text=two+users"
```

What to look at:

- `Program.cs`: wiring, the query-string `TargetKeyResolver` (demo only, see [authentication](../../docs/authentication.md)) and the three sends, including `SendToAsync` with a list of keys.
- `wwwroot/index.html`: the bundled `PingDropClient` loaded from `/pingdrop/stream/client.js` ([JavaScript client](../../docs/javascript-client.md)).
- The dashboard is enabled in Development at `/pingdrop/stream/dashboard` (see [dashboard](../../docs/dashboard.md)).
