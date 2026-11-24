# Samples

Small, runnable apps that reference the library project directly (`..\..\PingDrop\PingDrop.csproj`). They are part of `PingDrop.sln`, target `net8.0`, and are never packed.

| Sample | Shows | Run |
|---|---|---|
| [MinimalApi](MinimalApi) | `AddPingDrop`/`MapPingDrop`, broadcast, send to one user, send to several users, the bundled JS client on a static page | `dotnet run --project samples/MinimalApi` |
| [Mvc](Mvc) | Controllers and views, cookie sign-in, claims-based `TargetKeyResolver` (the production-style setup), toasts | `dotnet run --project samples/Mvc` |
| [Blazor](Blazor) | Blazor Server component that starts the JS client through JS interop and renders events | `dotnet run --project samples/Blazor` |
| [BackgroundSender](BackgroundSender) | A `BackgroundService` injecting `IPingDropService` and publishing a ticker plus a per-user event | `dotnet run --project samples/BackgroundSender` |

Each sample has its own README with the URLs to open and `curl` commands to try. All of them are run from the repository root; the printed URL (usually `http://localhost:5000` or the port in the console) is where the page lives. Set `ASPNETCORE_ENVIRONMENT=Development` (the default for `dotnet run`) to get the dashboard at `/pingdrop/stream/dashboard`.

Except for the MVC sample, the samples take the subscriber key from a `?userId=` query parameter to stay short. That is for demos only: any client could claim any identity. See [Authentication](../docs/authentication.md) for the claims-based approach.

## The manual test harness

[`PingDrop.Web.Test`](../PingDrop.Web.Test) is the kitchen-sink manual harness used while developing the library (Razor Pages template, bundled bootstrap and jQuery, dashboard test endpoints, load and stress endpoints). It is not a sample to copy from; start with the projects above.
