// JS module imported by Home.razor. Wraps the bundled PingDropClient (loaded from
// /pingdrop/stream/client.js in App.razor) and forwards events to the .NET component.
let client = null;

export function start(dotNetRef, userId) {
  stop();
  client = new PingDropClient({ url: "/pingdrop/stream", params: { userId } });

  const forward = (type) => client.on(type, (data) =>
    dotNetRef.invokeMethodAsync("OnPingDropEvent", type, typeof data === "string" ? data : JSON.stringify(data)));

  forward("notification");
  client.on("open", () => dotNetRef.invokeMethodAsync("OnPingDropEvent", "status", "connected"));
  client.on("error", () => dotNetRef.invokeMethodAsync("OnPingDropEvent", "status", "reconnecting"));
}

export function stop() {
  if (client) { client.close(); client = null; }
}
