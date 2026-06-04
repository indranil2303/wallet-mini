using wallet.domain.entities;

namespace wallet.application.interfaces;

public interface IUserRepository
{
    Task<bool> IsUserExistByEmailAsync(string email, CancellationToken cancellationToken = default!);
    Task<AppUser> CreateUserAsync(string googleId, string email, string alias, string fullname, CancellationToken cancellationToken = default!);
}