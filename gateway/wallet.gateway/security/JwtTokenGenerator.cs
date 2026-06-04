using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using wallet.domain.entities;
using WalletClaim = System.Security.Claims.Claim;
using System.Security.Claims;

namespace wallet.gateway.security;

public sealed class JwtTokenGenerator
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly byte[] _signingKey;
    private static readonly JwtSecurityTokenHandler TokenHandler = new();

    public JwtTokenGenerator(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _issuer = configuration["Jwt:Issuer"]?.Trim() ?? throw new InvalidOperationException("Missing Jwt:Issuer configuration.");
        _audience = configuration["Jwt:Audience"]?.Trim() ?? throw new InvalidOperationException("Missing Jwt:Audience configuration.");
        var secret = configuration["Jwt:Secret"]?.Trim() ?? throw new InvalidOperationException("Missing Jwt:Secret configuration.");
        _signingKey = Encoding.UTF8.GetBytes(secret);
    }

    public string GenerateAccessToken(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var claims = new[]
        {
            new WalletClaim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), // INTERNAL USER ID
            new WalletClaim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new WalletClaim("alias", user.Alias), // Alias
            new WalletClaim(JwtRegisteredClaimNames.Email, user.Email),
            new WalletClaim(JwtRegisteredClaimNames.Name, user.FullName),
            new WalletClaim("google_id", user.GoogleId)
        };

        var credentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(5 * 60),
            signingCredentials: credentials);

        return TokenHandler.WriteToken(token);
    }
}