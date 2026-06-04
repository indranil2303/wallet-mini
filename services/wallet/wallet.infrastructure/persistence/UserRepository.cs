using wallet.domain.entities;
using wallet.application.interfaces;
using wallet.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

public sealed class UserRepository : IUserRepository, IDisposable
{
    private readonly AppdbContext _dbContext;

    public UserRepository(AppdbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsUserExistByEmailAsync(string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(x => x.Email == email, cancellationToken);
    }

    public async Task<AppUser> CreateUserAsync(string googleId,
        string email,
        string alias,
        string fullname,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(googleId))
        {
            throw new ArgumentException("GoogleId is required.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.");
        }

        var normalizedEmail =
            email.Trim().ToLowerInvariant();
        alias =
            string.IsNullOrWhiteSpace(alias)
                ? normalizedEmail.Split('@')[0] : alias.Trim();
        fullname =
            string.IsNullOrWhiteSpace(fullname)
                ? alias : fullname.Trim();

        var strategy =
            _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await _dbContext.Database
                    .BeginTransactionAsync(cancellationToken);

            try
            {
                // Prevent duplicate creation during retries/race conditions
                var existingUser =
                    await _dbContext.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Email == normalizedEmail || x.GoogleId == googleId, cancellationToken);

                if (existingUser is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return existingUser;
                }

                var newUser = new AppUser
                {
                    GoogleId = googleId,
                    Email = normalizedEmail,
                    Alias = alias,
                    FullName = fullname,
                    CreatedAtUtc = DateTime.UtcNow,
                    IsActive = true
                };

                await _dbContext.Users
                    .AddAsync(newUser, cancellationToken);
                await _dbContext
                    .SaveChangesAsync(cancellationToken);

                await transaction
                    .CommitAsync(cancellationToken);

                return newUser;
            }
            catch
            {
                await transaction
                    .RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}