# Blazor sample

Blazor Server (interactive server components, no WebAssembly) with a single component that starts the PingDrop JavaScript client through JS interop and renders incoming events.

```
dotnet run --project samples/Blazor
```

Open `http://localhost:5000/?userId=alice`, click "Send to me" or "Broadcast", and watch the list grow. You can also send from a terminal:

```
curl -N "http://localhost:5000/pingdrop/stream?userId=alice"
```

What to look at:

- `Components/App.razor`: loads `/pingdrop/stream/client.js` before `blazor.web.js`.
- `wwwroot/pingdrop-interop.js`: creates `PingDropClient` and forwards events to .NET with `invokeMethodAsync`.
- `Components/Pages/Home.razor`: `[JSInvokable]` handler, `InvokeAsync(StateHasChanged)`, cleanup on dispose, and sending with an injected `IPingDropService`.

The server-side `IPingDropService` is the same singleton regardless of UI technology; the browser connects to the SSE endpoint directly, not through the Blazor circuit.
