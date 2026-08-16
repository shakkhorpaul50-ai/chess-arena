namespace WebApplication1.Models;

public enum FriendshipStatus
{
    Pending,
    Accepted,
    Declined
}

public class Friendship
{
    public int Id { get; set; }

    public string RequesterId { get; set; } = "";

    public ApplicationUser? Requester { get; set; }

    public string AddresseeId { get; set; } = "";

    public ApplicationUser? Addressee { get; set; }

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAt { get; set; }
}