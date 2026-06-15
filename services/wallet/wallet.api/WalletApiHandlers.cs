using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using wallet.api.extensions;
using wallet.application.command;
using wallet.application.interfaces;
using wallet.application.query;
using wallet.domain.contracts;
using wallet.infrastructure.persistence;
using StackExchange.Redis;

namespace wallet.api;

public static class WalletApiHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> GetWalletSummary(HttpContext context, IMediator mediator)
    {
        var session = context.GetSession();
        var result = await mediator.Send(new GetWalletSummaryQuery(session));
        return Results.Ok(result);
    }

    public static async Task<IResult> LookupRecipient(
        string alias,
        HttpContext context,
        AppdbContext dbContext,
        IConnectionMultiplexer redis,
        CancellationToken cancellationToken)
    {
        var session = context.GetSession();
        var cleanAlias = alias.Trim().ToLowerInvariant();
        var cacheKey = $"lookup:alias:{cleanAlias}";
        var database = redis.GetDatabase();

        var cachedResult = await database.StringGetAsync(cacheKey);
        if (cachedResult.HasValue)
        {
            var response = JsonSerializer.Deserialize<RecipientLookupRecord>(cachedResult.ToString()!, JsonOptions);
            return response is null
                ? Results.NotFound(new { Message = $"Alias not found: {alias}" })
                : Results.Ok(response);
        }

        var recipient = await dbContext.Users
            .Include(x => x.WalletAccount)
            .AsNoTracking()
            .Where(w => w.Alias == cleanAlias && w.Id != session.APP_USR_ID && w.IsActive)
            .Select(w => new { WalletId = w.WalletAccount.Id, FullName = w.FullName, Alias = w.Alias })
            .FirstOrDefaultAsync(cancellationToken);

        if (recipient is null)
        {
            return Results.NotFound(new { Message = $"Alias not found: {alias}" });
        }

        var lookupResponse = new RecipientLookupRecord(recipient.WalletId, MaskName(recipient.FullName), recipient.Alias);
        await database.StringSetAsync(cacheKey, JsonSerializer.Serialize(lookupResponse, JsonOptions), TimeSpan.FromMinutes(15));

        return Results.Ok(lookupResponse);
    }

    public static async Task<IResult> GetSupportedCurrencies(
        string? baseCurrency,
        ICurrencyRepository currencyRepository)
    {
        if (!string.IsNullOrWhiteSpace(baseCurrency))
        {
            var currency = await currencyRepository.GetCurrencyAsync(baseCurrency);
            return currency is null
                ? Results.NotFound(new { Message = $"Currency '{baseCurrency}' not found." })
                : Results.Ok(currency);
        }

        var currencies = await currencyRepository.GetAllCurrenciesAsync();
        return Results.Ok(currencies?.OrderBy(x => x.Code));
    }

    public static async Task<IResult> GetFxQuote(
        FxQuoteRequest request,
        IFxRateProvider fxProvider,
        ILogger<Program> logger)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SourceCurrency) || string.IsNullOrWhiteSpace(request.DestinationCurrency) || request.ReceivingAmount <= 0)
        {
            return Results.BadRequest(new { Message = "Invalid FX quote request payload." });
        }

        var fx = await fxProvider.GetRateAsync(request.SourceCurrency, request.DestinationCurrency);
        if (fx is null)
        {
            return Results.BadRequest(new { Message = $"FX rate not available for {request.SourceCurrency} to {request.DestinationCurrency}" });
        }

        var convertedAmount = Math.Round(request.ReceivingAmount / fx.Rate, 2);
        var transactionFee = Math.Round(convertedAmount * 0.01m, 2);

        logger.LogInformation(
            "FX Quote: {SourceCurrency} to {DestinationCurrency} - Rate: {Rate}, Converted Amount: {ConvertedAmount}, Transaction Fee: {TransactionFee}",
            request.SourceCurrency,
            request.DestinationCurrency,
            fx.Rate,
            convertedAmount,
            transactionFee);

        return Results.Ok(new FxQuoteResponse(convertedAmount, fx.Rate, transactionFee));
    }

    public static async Task<IResult> UpdateWalletStatus(
        WalletStatusRequest request,
        HttpContext context,
        IMediator mediator)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CurrencyCode))
        {
            return Results.BadRequest(new { Message = "Currency code is required." });
        }

        var session = context.GetSession();
        await mediator.Send(new SaveWalletStatusCommand(session.APP_USR_ID, request.CurrencyCode));
        return Results.NoContent();
    }

    public static async Task<IResult> SendMoney(
        SendMoneyRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        HttpContext context,
        IMediator mediator)
    {
        idempotencyKey = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { Message = "Missing Idempotency-Key header value." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ReceiverWalletId) || request.SourceAmount <= 0 || string.IsNullOrWhiteSpace(request.DestinationCurrency) || request.DestinationAmount <= 0 || request.FxRate <= 0 || string.IsNullOrWhiteSpace(request.FeeCurrency) || request.TransactionFee < 0)
        {
            return Results.BadRequest(new { Message = "Invalid payment request payload." });
        }

        var session = context.GetSession();
        var transactionId = await mediator.Send(new SendMoneyCommand(
            idempotencyKey,
            session.APP_USR_ID,
            request.ReceiverWalletId,
            request.SourceAmount,
            request.DestinationCurrency,
            request.DestinationAmount,
            request.FxRate,
            request.FeeCurrency,
            request.TransactionFee));

        return Results.Ok(new { id = transactionId });
    }

    public static async Task<IResult> GetTransactions(
        HttpContext context,
        IMediator mediator,
        DateTime? startDate,
        DateTime? endDate,
        int pageIndex,
        int pageSize)
    {
        var session = context.GetSession();
        var result = await mediator.Send(new GetTransactionsQuery(session.APP_USR_ID, startDate, endDate, pageIndex, pageSize));
        return Results.Ok(result);
    }

    public static string MaskName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1
            ? $"{parts[0]} {parts[1][0]}."
            : fullName;
    }
}
