using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.ViewModels;

namespace WebApplication1.Controllers;

[Authorize]
public class FriendsController : Controller
{
    private readonly AppDbContext _db;

    public FriendsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var model = await BuildViewModelAsync(userId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string emailOrName)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        if (string.IsNullOrWhiteSpace(emailOrName))
        {
            ModelState.AddModelError(string.Empty, "Enter an email address or display name.");
            return View("Index", await BuildViewModelAsync(userId));
        }

        var target = await _db.Users
            .FirstOrDefaultAsync(u =>
                (u.Email != null && u.Email.ToLower() == emailOrName.Trim().ToLower()) ||
                u.DisplayName.ToLower() == emailOrName.Trim().ToLower());
        if (target is null)
        {
            ModelState.AddModelError(string.Empty, "No player found with that email or display name.");
            return View("Index", await BuildViewModelAsync(userId));
        }
        if (target.Id == userId || target.IsBot)
        {
            ModelState.AddModelError(string.Empty, "You cannot add that player.");
            return View("Index", await BuildViewModelAsync(userId));
        }

        var exists = await _db.Friendships.AnyAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == target.Id) ||
            (f.RequesterId == target.Id && f.AddresseeId == userId));
        if (exists)
        {
            ModelState.AddModelError(string.Empty, "That player is already on your list.");
            return View("Index", await BuildViewModelAsync(userId));
        }

        _db.Friendships.Add(new Friendship
        {
            RequesterId = userId,
            AddresseeId = target.Id,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var friendship = await _db.Friendships.FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == userId);
        if (friendship is not null)
        {
            friendship.Status = FriendshipStatus.Accepted;
            friendship.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decline(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var friendship = await _db.Friendships.FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == userId);
        if (friendship is not null)
        {
            friendship.Status = FriendshipStatus.Declined;
            friendship.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(string userId)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var friendship = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == currentUserId && f.AddresseeId == userId) ||
            (f.RequesterId == userId && f.AddresseeId == currentUserId));
        if (friendship is not null)
        {
            _db.Friendships.Remove(friendship);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<FriendsViewModel> BuildViewModelAsync(string userId)
    {
        var friends = await _db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                        (f.RequesterId == userId || f.AddresseeId == userId))
            .ToListAsync();

        var friendList = friends
            .Select(f => f.RequesterId == userId ? f.Addressee! : f.Requester!)
            .Select(u => (Friend: u, Wins: 0))
            .OrderBy(x => x.Friend.DisplayName)
            .ToList();

        var incoming = await _db.Friendships
            .Include(f => f.Requester)
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
            .ToListAsync();

        var outgoing = await _db.Friendships
            .Include(f => f.Addressee)
            .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Pending)
            .ToListAsync();

        return new FriendsViewModel
        {
            Friends = friendList,
            IncomingRequests = incoming,
            OutgoingRequests = outgoing
        };
    }
}