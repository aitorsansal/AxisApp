using AxisApp.Models;

namespace AxisApp.Services;

public interface IMembersRepository
{
    Task<List<Member>> GetForGroupAsync(Guid groupId);
    Task<Member?> GetByIdAsync(Guid memberId);

    /// <summary>The current account's own member row — a claimed account has exactly one, reused
    /// across every group it belongs to (see the members-vs-accounts design note in CLAUDE.md), so
    /// there's no group context needed to find it. Null if the account has never joined/created a
    /// group yet.</summary>
    Task<Member?> GetMyMemberAsync();

    /// <summary>Adds a phantom member (no linked account yet) created by the current user.</summary>
    Task<Member> AddPhantomAsync(string displayName);

    /// <summary>Joins an existing member (phantom or claimed) to a group via a group_members row.</summary>
    Task AddToGroupAsync(Guid groupId, Guid memberId);

    /// <summary>
    /// Finds members whose display name matches the query, scoped by RLS to members the current
    /// account can already see (shares a group with, created, or is themselves) — never an
    /// app-wide directory search. Used to surface "this might already exist" suggestions before
    /// creating a new phantom, so the same person doesn't end up as two unrelated phantom rows
    /// across groups.
    /// </summary>
    Task<List<Member>> SearchVisibleByNameAsync(string query);
}
