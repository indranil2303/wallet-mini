using System.IO;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using wallet.api;
using wallet.api.extensions;
using wallet.application.command;
using wallet.application.interfaces;
using wallet.application.query;
using wallet.domain.contracts;
using wallet.infrastructure.persistence;
using Xunit;

public class WalletApiHandlersTests
{
    [Fact]
    public async Task GetWalletSummary_ReturnsOkResponse()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetWalletSummaryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletRecord("USD", 150.50m, "Active", true));

        var context = CreateAuthenticatedContext();
        var result = await WalletApiHandlers.GetWalletSummary(context, mediator.Object);

        var response = await ExecuteResultAsync(result);
        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("USD");
    }

    [Fact]
    public async Task GetSupportedCurrencies_ReturnsNotFoundForUnknownCurrency()
    {
        var repository = new Mock<ICurrencyRepository>();
        repository.Setup(x => x.GetCurrencyAsync("XXX", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyRecord?)null);

        var result = await WalletApiHandlers.GetSupportedCurrencies("XXX", repository.Object);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(404);
        response.Body.Should().Contain("not found");
    }

    [Fact]
    public async Task GetSupportedCurrencies_ReturnsCurrencyList()
    {
        var currencies = new[]
        {
            new CurrencyRecord("EUR", "Euro"),
            new CurrencyRecord("USD", "US Dollar")
        };

        var repository = new Mock<ICurrencyRepository>();
        repository.Setup(x => x.GetAllCurrenciesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currencies);

        var result = await WalletApiHandlers.GetSupportedCurrencies(null, repository.Object);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("Euro").And.Contain("US Dollar");
    }

    [Fact]
    public async Task GetFxQuote_ReturnsBadRequestWhenRateNotFound()
    {
        var provider = new Mock<IFxRateProvider>();
        provider.Setup(x => x.GetRateAsync("USD", "GBP", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FrankfurterResponse?)null);

        var logger = Mock.Of<ILogger<Program>>();
        var request = new global::FxQuoteRequest("USD", 100m, "GBP");

        var result = await WalletApiHandlers.GetFxQuote(request, provider.Object, logger);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("not available");
    }

    [Fact]
    public async Task GetFxQuote_ReturnsQuoteResponse()
    {
        var provider = new Mock<IFxRateProvider>();
        provider.Setup(x => x.GetRateAsync("USD", "EUR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FrankfurterResponse(DateTime.UtcNow, "USD", "EUR", 0.85m));

        var logger = Mock.Of<ILogger<Program>>();
        var request = new global::FxQuoteRequest("USD", 100m, "EUR");

        var result = await WalletApiHandlers.GetFxQuote(request, provider.Object, logger);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("finalAmount").And.Contain("transactionFee");
    }

    [Fact]
    public async Task UpdateWalletStatus_ReturnsNoContent()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<SaveWalletStatusCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new global::WalletStatusRequest("USD");
        var result = await WalletApiHandlers.UpdateWalletStatus(request, CreateAuthenticatedContext(), mediator.Object);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task SendMoney_ReturnsBadRequestWhenIdempotencyKeyMissing()
    {
        var mediator = new Mock<IMediator>();
        var request = new global::SendMoneyRequest("00000000-0000-0000-0000-000000000000", 10m, "EUR", 8.5m, 0.85m, "EUR", 0.25m);

        var result = await WalletApiHandlers.SendMoney(request, string.Empty, CreateAuthenticatedContext(), mediator.Object);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("Missing Idempotency-Key");
    }

    [Fact]
    public async Task SendMoney_ReturnsOkWhenValidRequest()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send<Guid>(It.IsAny<SendMoneyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var request = new global::SendMoneyRequest("00000000-0000-0000-0000-000000000000", 10m, "EUR", 8.5m, 0.85m, "EUR", 0.25m);
        var result = await WalletApiHandlers.SendMoney(request, "test-idempotency", CreateAuthenticatedContext(), mediator.Object);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task LookupRecipient_CacheHitReturnsCachedResult()
    {
        var expected = new RecipientLookupRecord(Guid.NewGuid(), "John D.", "john.doe");
        var cachedJson = JsonSerializer.Serialize(expected, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var databaseMock = new Mock<IDatabase>();
        databaseMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(cachedJson));

        var redisMock = new Mock<IConnectionMultiplexer>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(databaseMock.Object);

        var options = new DbContextOptionsBuilder<AppdbContext>()
            .UseInMemoryDatabase("LookupRecipient_CacheHit").Options;

        await using var dbContext = new AppdbContext(options);
        var result = await WalletApiHandlers.LookupRecipient("john.doe", CreateAuthenticatedContext(), dbContext, redisMock.Object, CancellationToken.None);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("John D.");
    }

    [Fact]
    public async Task GetTransactions_ReturnsPagedResults()
    {
        var mediator = new Mock<IMediator>();
        var page = new PagedRecord<TransactionRecord>(new[]
        {
            new TransactionRecord(Guid.NewGuid(), "DR", "alice", Guid.NewGuid(), "bob", Guid.NewGuid(), "USD", 100m, 0.85m, 0.85m, "Completed", DateTime.UtcNow)
        }, 1, 10, 1, 1, false, false);

        mediator.Setup(x => x.Send(It.IsAny<GetTransactionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var result = await WalletApiHandlers.GetTransactions(CreateAuthenticatedContext(), mediator.Object, null, null, 1, 10);
        var response = await ExecuteResultAsync(result);

        response.StatusCode.Should().Be(200);
        response.Body.Should().Contain("alice").And.Contain("bob");
    }

    private static HttpContext CreateAuthenticatedContext()
    {
        var context = new DefaultHttpContext();
        context.Items["APP_SESSION"] = new AppSession(100, "google-id", "user@example.com");
        return context;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        context.RequestServices = services.BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }
}
