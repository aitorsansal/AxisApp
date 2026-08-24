using AxisApp.Models;

namespace AxisApp.Services;

public interface IMembersRepository
{
    Task<List<Member>> GetForGroupAsync(Guid groupId);
    Task<Member?> GetByIdAsync(Guid memberId);

    /// <summary>Adds a phantom member (no linked account yet) created by the current user.</summary>
    Task<Member> AddPhantomAsync(string displayName);
}
