# MVC sample

Controllers and Razor views with cookie authentication and per-user keys taken from claims, the setup recommended in [docs/authentication.md](../../docs/authentication.md).

```
dotnet run --project samples/Mvc
```

1. Open `http://localhost:5000/login?user=alice` (a fake sign-in that sets an auth cookie), then the home page `/`.
2. Click "Ship order": `HomeController.ShipOrder` calls `SendToAsync(userKey, ...)` and a toast appears.
3. In a second browser profile sign in as `bob`. Alice's "Ship order" only reaches alice; "Announce" reaches both.

What to look at:

- `Program.cs`: cookie auth, the `PingDropClients` policy set as `AuthorizationPolicy`, and `TargetKeyResolver` reading `ClaimTypes.NameIdentifier` (the client never supplies its identity).
- `Controllers/HomeController.cs`: sending from an action with the same key.
- `Views/Home/Index.cshtml`: the bundled JS client with `withCredentials` and a tiny toast renderer.

The `/login` endpoint is a stand-in; never accept a user name from the query string in a real app.
