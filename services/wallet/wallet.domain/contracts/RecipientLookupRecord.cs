namespace wallet.domain.contracts;
public record RecipientLookupRecord(Guid walletId, string maskedname, string alias);