# Background sender sample

A `BackgroundService` injects `IPingDropService` and publishes without any HTTP request: a broadcast `tick` event every 3 seconds, and a targeted `private` event to the key `alice` every third tick.

```
dotnet run --project samples/BackgroundSender
```

Open `http://localhost:5000/` (ticks only) and `http://localhost:5000/?userId=alice` (ticks plus private events), or:

```
curl -N "http://localhost:5000/pingdrop/stream?userId=alice"
```

Look at `TickerService.cs` for the injection and the error handling (a failed send must not end the loop).
