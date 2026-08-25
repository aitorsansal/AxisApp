using AxisApp.Models;

namespace AxisApp.Services;

public interface IMembersRepository
{
    Task<List<Member>> GetForGroupAsync(Guid groupId);
    Task<Member?> GetByIdAsync(Guid memberId);

    /// <summary>Adds a phantom member (no linked account yet) created by the current user.</summary>
    Task<Member> AddPhantomAsync(string displayName);

    /// <summary>Joins an existing member (phantom or claimed) to a group via a group_members row.</summary>
    Task AddToGroupAsync(Guid groupId, Guid memberId);
}
