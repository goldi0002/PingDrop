using System.Text.Json;
using System.Text.Json.Serialization;
using PingDrop.Abstractions;
using PingDrop.Internal;

namespace PingDrop.Tests.Unit;

/// <summary>A clock tests advance by hand.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>Writes its text as a raw JSON string without escaping non-ASCII characters.</summary>
[JsonConverter(typeof(RawUtf8Converter))]
public sealed record RawUtf8(string Text);

public sealed class RawUtf8Converter : JsonConverter<RawUtf8>
{
    public override RawUtf8? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, RawUtf8 value, JsonSerializerOptions options) => writer.WriteRawValue("\"" + value.Text + "\"");
}

public class PayloadLimitTests
{
    private static (PingDropHub hub, PDConnectionStore store, PDEventHistoryStore history, NotificationFactory factory) Create(int? max)
    {
        var store = new PDConnectionStore();
        var history = new PDEventHistoryStore(10);
        var factory = new NotificationFactory();
        var hub = new PingDropHub(store, history, factory, null, new PingDropOptions { MaxPayloadBytes = max });
        return (hub, store, history, factory);
    }

    [Fact]
    public async Task Broadcast_OverLimit_Throws_AndConsumesNoIdOrHistory()
    {
        var (hub, store, history, factory) = Create(10);
        var client = store.Add(null, "u");

        var ex = await Assert.ThrowsAsync<PingDropPayloadTooLargeException>(async () => await hub.BroadcastAsync("0123456789", "evt"));

        Assert.IsAssignableFrom<ArgumentException>(ex);
        Assert.Equal(12, ex.Size);
        Assert.Equal(10, ex.Limit);
        Assert.Equal(0, factory.LastIssuedId);
        Assert.Equal(0, history.Count);
        Assert.Equal(0, client.PendingNotifications);
        Assert.Equal(0, hub.TotalEventsPublished);

        // The next accepted event gets id 1.
        await hub.BroadcastAsync("ok", "evt");
        Assert.Equal(1, factory.LastIssuedId);
    }

    [Fact]
    public async Task SendTo_OverLimit_Throws_EvenWhenNobodyIsConnected()
    {
        var (hub, _, history, factory) = Create(5);

        await Assert.ThrowsAsync<PingDropPayloadTooLargeException>(async () => await hub.SendToAsync("nobody", "toolong", "evt"));
        await Assert.ThrowsAsync<PingDropPayloadTooLargeException>(async () => await hub.SendToAsync(new[] { "a", "b" }, "toolong", "evt"));

        Assert.Equal(0, factory.LastIssuedId);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public async Task ExactLimit_Passes_OneByteOver_Throws()
    {
        var (hub, store, history, _) = Create(6);   // "abcd" serializes to 6 bytes with quotes
        var client = store.Add(null);

        await hub.BroadcastAsync("abcd", "evt");
        Assert.Equal(1, client.PendingNotifications);
        Assert.Equal(1, history.Count);

        await Assert.ThrowsAsync<PingDropPayloadTooLargeException>(async () => await hub.BroadcastAsync("abcde", "evt"));
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public async Task Limit_IsMeasuredInUtf8Bytes_NotCharacters()
    {
        // "é" is one char but two UTF-8 bytes; with the quotes the JSON is 4 bytes (3 chars).
        var (hub, store, history, _) = Create(4);
        store.Add(null);

        await hub.BroadcastAsync(new RawUtf8("é"), "evt");
        Assert.Equal(1, history.Count);

        var (tight, tightStore, tightHistory, _) = Create(3);
        tightStore.Add(null);
        var ex = await Assert.ThrowsAsync<PingDropPayloadTooLargeException>(async () => await tight.BroadcastAsync(new RawUtf8("é"), "evt"));
        Assert.Equal(4, ex.Size);
        Assert.Equal(0, tightHistory.Count);
    }

    [Fact]
    public async Task NoLimit_AcceptsLargePayloads()
    {
        var (hub, store, history, _) = Create(null);
        store.Add(null);

        await hub.BroadcastAsync(new string('x', 100_000), "evt");

        Assert.Equal(1, history.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_RejectNonPositiveValues(int value)
    {
        var o = new PingDropOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => o.MaxPayloadBytes = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.MaxConnectionsPerKey = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.MaxTotalConnections = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.Dashboard.MaxSendsPerMinute = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.Dashboard.MaxFeedConnections = value);
    }

    [Fact]
    public void Options_Defaults_AreUnlimitedExceptDashboardFeeds()
    {
        var o = new PingDropOptions();
        Assert.Null(o.MaxPayloadBytes);
        Assert.Null(o.MaxConnectionsPerKey);
        Assert.Null(o.MaxTotalConnections);
        Assert.Null(o.Dashboard.MaxSendsPerMinute);
        Assert.Equal(10, o.Dashboard.MaxFeedConnections);
        o.MaxTotalConnections = null; // null stays valid
    }
}

public class SendRateLimiterTests
{
    [Fact]
    public void AllowsLimit_ThenRejects_WithRetryAfter_ThenRecoversAfterWindow()
    {
        var clock = new FakeTimeProvider();
        var limiter = new PDSendRateLimiter(2, clock);

        Assert.True(limiter.TryAcquire(out _));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(limiter.TryAcquire(out _));
        Assert.False(limiter.TryAcquire(out int retry));
        Assert.Equal(50, retry);

        clock.Advance(TimeSpan.FromSeconds(49));
        Assert.False(limiter.TryAcquire(out retry));
        Assert.Equal(1, retry);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire(out _));
        Assert.True(limiter.TryAcquire(out _));
        Assert.False(limiter.TryAcquire(out _));
    }

    [Fact]
    public async Task ConcurrentCallers_NeverExceedTheLimit()
    {
        var limiter = new PDSendRateLimiter(25, new FakeTimeProvider());
        int granted = 0;

        await Task.WhenAll(Enumerable.Range(0, 200).Select(unused => Task.Run(() =>
        {
            if (limiter.TryAcquire(out _)) Interlocked.Increment(ref granted);
        })));

        Assert.Equal(25, granted);
    }
}

public class ConnectionLimitStoreTests
{
    private static readonly HashSet<string>? NoEvents = null;

    [Fact]
    public void PerKeyLimit_RejectsOnlyThatKey_AndFreesSlotOnRemove()
    {
        var store = new PDConnectionStore();
        var limits = new PDConnectionLimits(2, null, null);

        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "a", limits, out var a1));
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "a", limits, out _));
        Assert.Equal(PDConnectionRejection.PerKey, store.TryAdd(NoEvents, "a", limits, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "b", limits, out _));
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, null, limits, out _));
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "  ", limits, out _));
        Assert.Equal(4 + 1, store.Count);

        store.Remove(a1!.ConnectionId);
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "a", limits, out _));
    }

    [Fact]
    public void TotalLimit_CountsKeylessAndKeyed_ButNotDashboard()
    {
        var store = new PDConnectionStore();
        var limits = new PDConnectionLimits(null, 2, null);

        store.Add(null, InternalConstants.DashboardSubscriberKey);
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, null, limits, out _));
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "a", limits, out var second));
        Assert.Equal(PDConnectionRejection.Total, store.TryAdd(NoEvents, null, limits, out _));
        Assert.Equal(PDConnectionRejection.Total, store.TryAdd(NoEvents, "z", limits, out _));
        Assert.Equal(2, store.Count);

        store.Remove(second!.ConnectionId);
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "z", limits, out _));
    }

    [Fact]
    public void DashboardFeeds_HaveTheirOwnCap_AndDoNotUseUserLimits()
    {
        var store = new PDConnectionStore();
        var limits = new PDConnectionLimits(1, 1, 2);
        string dash = InternalConstants.DashboardSubscriberKey;

        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, "a", limits, out _));
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, dash, limits, out _));
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, dash, limits, out var second));
        Assert.Equal(PDConnectionRejection.DashboardFeeds, store.TryAdd(NoEvents, dash, limits, out _));
        Assert.Equal(PDConnectionRejection.Total, store.TryAdd(NoEvents, "b", limits, out _));

        store.Remove(second!.ConnectionId);
        Assert.Equal(PDConnectionRejection.None, store.TryAdd(NoEvents, dash, limits, out _));
    }

    [Fact]
    public async Task ConcurrentConnects_NeverExceedTotalOrPerKeyLimit()
    {
        var store = new PDConnectionStore();
        var total = new PDConnectionLimits(null, 10, null);
        int accepted = 0;

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            if (store.TryAdd(NoEvents, "k" + (i % 7), total, out _) == PDConnectionRejection.None) Interlocked.Increment(ref accepted);
        })));

        Assert.Equal(10, accepted);
        Assert.Equal(10, store.Count);

        var perKey = new PDConnectionStore();
        var keyLimit = new PDConnectionLimits(3, null, null);
        accepted = 0;
        await Task.WhenAll(Enumerable.Range(0, 200).Select(unused => Task.Run(() =>
        {
            if (perKey.TryAdd(NoEvents, "same", keyLimit, out _) == PDConnectionRejection.None) Interlocked.Increment(ref accepted);
        })));

        Assert.Equal(3, accepted);
        Assert.Equal(3, perKey.CountByKey("same"));
    }
}
