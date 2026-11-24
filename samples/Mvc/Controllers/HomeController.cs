using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingDrop.Abstractions;

namespace PingDropSamples.Mvc.Controllers;

public class HomeController(IPingDropService pingDrop) : Controller
{
    // GET / : the page that shows toasts.
    public IActionResult Index() => View();

    // POST /Home/ShipOrder : pretend to ship an order and tell the signed-in user about it.
    // Keys match the TargetKeyResolver in Program.cs (the NameIdentifier claim).
    [Authorize, HttpPost]
    public async Task<IActionResult> ShipOrder(int id = 1001)
    {
        var userKey = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        await pingDrop.SendToAsync(userKey,
            new { Title = "Order shipped", Message = $"Order #{id} is on its way." },
            "notification");

        return Done();
    }

    // The page posts with fetch (no reload, so the SSE connection stays open); plain form posts still redirect.
    private IActionResult Done() =>
        Request.Headers["X-Requested-With"] == "fetch" ? NoContent() : RedirectToAction(nameof(Index));

    // POST /Home/Announce : broadcast to everyone, whoever they are.
    [Authorize, HttpPost]
    public async Task<IActionResult> Announce()
    {
        await pingDrop.BroadcastAsync(
            new { Title = "Announcement", Message = $"Sent by {User.Identity?.Name}" },
            "notification");

        return Done();
    }
}
