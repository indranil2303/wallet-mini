namespace wallet.gateway.security;

public sealed record RefreshTokenRequest(int UserId, string Email, string GoogleId, DateTime ExpiresAtUtc);