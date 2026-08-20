using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-313: supplier↔client chat. One persistent thread per (SupplierTenantId,
/// ClientTenantId) pair — either side can be the caller, distinguished by
/// isSupplierSide.
/// </summary>
public sealed class SupplierChatServiceTests
{
    private readonly ISupplierChatRepository _repo = Substitute.For<ISupplierChatRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ITenantSessionOverride _tenantSessionOverride = Substitute.For<ITenantSessionOverride>();
    private readonly SupplierChatService _sut;

    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public SupplierChatServiceTests()
    {
        _sut = new SupplierChatService(_repo, _notifications, _tenantSessionOverride);

        // TASK-582: SendMessageAsync's supplier→client branch now runs its notification-enqueue
        // + SaveChanges tail inside _tenantSessionOverride.ExecuteAsync — same pure pass-through
        // convention LoyaltyServiceTests uses for ITenantSessionOverride: invokes the delegate
        // immediately instead of opening a real transaction, so every pre-existing SendMessage
        // assertion still works unchanged.
        _tenantSessionOverride
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<bool>>>()());
    }

    // ── GetOrCreateSessionAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetOrCreateSessionAsync_ExistingSession_ReturnsItWithoutCreating()
    {
        var session = new SupplierChatSession
        {
            SupplierTenantId = _supplierTenantId,
            ClientTenantId   = _clientTenantId,
            CreatedByUserId  = _userId,
        };
        _repo.GetSessionAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
             .Returns(session);
        _repo.GetTenantDisplayNameAsync(_clientTenantId, Arg.Any<CancellationToken>())
             .Returns("Client Co");

        var dto = await _sut.GetOrCreateSessionAsync(
            _supplierTenantId, _clientTenantId, isSupplierSide: true, _userId);

        Assert.Equal(session.Id, dto.Id);
        Assert.Equal(_clientTenantId, dto.OtherTenantId);
        Assert.Equal("Client Co", dto.OtherTenantName);
        await _repo.DidNotReceive().AddSessionAsync(Arg.Any<SupplierChatSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_NoExistingSession_CreatesOne()
    {
        _repo.GetSessionAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
             .Returns((SupplierChatSession?)null);
        _repo.GetTenantDisplayNameAsync(_clientTenantId, Arg.Any<CancellationToken>())
             .Returns("Client Co");

        SupplierChatSession? created = null;
        _repo.AddSessionAsync(Arg.Do<SupplierChatSession>(s => created = s), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var dto = await _sut.GetOrCreateSessionAsync(
            _supplierTenantId, _clientTenantId, isSupplierSide: true, _userId);

        Assert.NotNull(created);
        Assert.Equal(_supplierTenantId, created!.SupplierTenantId);
        Assert.Equal(_clientTenantId, created.ClientTenantId);
        Assert.Equal(_userId, created.CreatedByUserId);
        Assert.Equal(created.Id, dto.Id);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_ClientSideCaller_MapsTenantsCorrectly()
    {
        // isSupplierSide = false: myTenantId (caller) is the client, otherTenantId is the supplier.
        _repo.GetSessionAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
             .Returns((SupplierChatSession?)null);
        _repo.GetTenantDisplayNameAsync(_supplierTenantId, Arg.Any<CancellationToken>())
             .Returns("Supplier Co");

        SupplierChatSession? created = null;
        _repo.AddSessionAsync(Arg.Do<SupplierChatSession>(s => created = s), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);

        var dto = await _sut.GetOrCreateSessionAsync(
            myTenantId: _clientTenantId, otherTenantId: _supplierTenantId, isSupplierSide: false, _userId);

        Assert.Equal(_supplierTenantId, created!.SupplierTenantId);
        Assert.Equal(_clientTenantId, created.ClientTenantId);
        Assert.Equal(_supplierTenantId, dto.OtherTenantId);
        Assert.Equal("Supplier Co", dto.OtherTenantName);
    }

    // ── GetMessagesAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessagesAsync_CallerBelongsToSession_ReturnsMessages()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _repo.GetMessagesAsync(session.Id, Arg.Any<CancellationToken>())
             .Returns(new List<SupplierChatMessage>
             {
                 new() { SessionId = session.Id, SenderTenantId = _supplierTenantId, SenderName = "Sup", Body = "Hi" },
             });

        var (messages, error) = await _sut.GetMessagesAsync(session.Id, _supplierTenantId);

        Assert.Null(error);
        Assert.NotNull(messages);
        Assert.Single(messages!);
    }

    [Fact]
    public async Task GetMessagesAsync_CallerBelongsToSession_MarksOtherPartyMessagesRead()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _repo.GetMessagesAsync(session.Id, Arg.Any<CancellationToken>())
             .Returns(new List<SupplierChatMessage>());

        await _sut.GetMessagesAsync(session.Id, _clientTenantId);

        await _repo.Received(1).MarkMessagesReadAsync(session.Id, _clientTenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMessagesAsync_CallerNotInSession_ReturnsAccessDenied()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var (messages, error) = await _sut.GetMessagesAsync(session.Id, Guid.NewGuid());

        Assert.Null(messages);
        Assert.Equal(SupplierChatService.AccessDeniedError, error);
    }

    [Fact]
    public async Task GetMessagesAsync_CallerNotInSession_DoesNotMarkMessagesRead()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        await _sut.GetMessagesAsync(session.Id, Guid.NewGuid());

        await _repo.DidNotReceive().MarkMessagesReadAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMessagesAsync_SessionNotFound_ReturnsError()
    {
        var sessionId = Guid.NewGuid();
        _repo.GetSessionByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns((SupplierChatSession?)null);

        var (messages, error) = await _sut.GetMessagesAsync(sessionId, _supplierTenantId);

        Assert.Null(messages);
        Assert.Equal(SupplierChatService.SessionNotFoundError, error);
    }

    [Fact]
    public async Task GetMessagesAsync_SessionNotFound_DoesNotMarkMessagesRead()
    {
        var sessionId = Guid.NewGuid();
        _repo.GetSessionByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns((SupplierChatSession?)null);

        await _sut.GetMessagesAsync(sessionId, _supplierTenantId);

        await _repo.DidNotReceive().MarkMessagesReadAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── SendMessageAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_ValidBody_CreatesMessage()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var (message, error) = await _sut.SendMessageAsync(
            session.Id, _supplierTenantId, _userId, "Supplier Person", "Hello there");

        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal("Hello there", message!.Body);
        Assert.Equal(_supplierTenantId, message.SenderTenantId);
        await _repo.Received(1).AddMessageAsync(
            Arg.Is<SupplierChatMessage>(m => m.Body == "Hello there" && m.SessionId == session.Id),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessageAsync_BlankBody_ReturnsValidationError_DoesNotLookUpSession()
    {
        var (message, error) = await _sut.SendMessageAsync(
            Guid.NewGuid(), _supplierTenantId, _userId, "Supplier Person", "   ");

        Assert.Null(message);
        Assert.Equal(SupplierChatService.BodyRequiredError, error);
        await _repo.DidNotReceive().GetSessionByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessageAsync_BodyTooLong_ReturnsValidationError()
    {
        var longBody = new string('a', SupplierChatService.MaxBodyLength + 1);

        var (message, error) = await _sut.SendMessageAsync(
            Guid.NewGuid(), _supplierTenantId, _userId, "Supplier Person", longBody);

        Assert.Null(message);
        Assert.NotNull(error);
        Assert.Contains("4000", error);
    }

    [Fact]
    public async Task SendMessageAsync_CallerNotInSession_ReturnsAccessDenied_DoesNotSave()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var (message, error) = await _sut.SendMessageAsync(
            session.Id, Guid.NewGuid(), _userId, "Intruder", "Hi");

        Assert.Null(message);
        Assert.Equal(SupplierChatService.AccessDeniedError, error);
        await _repo.DidNotReceive().AddMessageAsync(Arg.Any<SupplierChatMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessageAsync_SessionNotFound_ReturnsError()
    {
        var sessionId = Guid.NewGuid();
        _repo.GetSessionByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns((SupplierChatSession?)null);

        var (message, error) = await _sut.SendMessageAsync(
            sessionId, _supplierTenantId, _userId, "Supplier Person", "Hi");

        Assert.Null(message);
        Assert.Equal(SupplierChatService.SessionNotFoundError, error);
    }

    // ── GetSessionsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionsAsync_SupplierSide_MapsOtherTenantAsClient()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionsAsync(_supplierTenantId, true, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierChatSession, string, SupplierChatMessage?, int)>
             {
                 (session, "Client Co", null, 0),
             });

        var sessions = await _sut.GetSessionsAsync(_supplierTenantId, isSupplierSide: true);

        var dto = Assert.Single(sessions);
        Assert.Equal(_clientTenantId, dto.OtherTenantId);
        Assert.Equal("Client Co", dto.OtherTenantName);
    }

    [Fact]
    public async Task GetSessionsAsync_ClientSide_MapsOtherTenantAsSupplier()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionsAsync(_clientTenantId, false, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierChatSession, string, SupplierChatMessage?, int)>
             {
                 (session, "Supplier Co", null, 0),
             });

        var sessions = await _sut.GetSessionsAsync(_clientTenantId, isSupplierSide: false);

        var dto = Assert.Single(sessions);
        Assert.Equal(_supplierTenantId, dto.OtherTenantId);
        Assert.Equal("Supplier Co", dto.OtherTenantName);
    }

    // ── UnreadCount (TASK-319) ─────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionsAsync_NewMessageFromOtherParty_ShowsUnreadCountForRecipient()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        // Message sent by supplier -> unread for the client (recipient) side.
        _repo.GetSessionsAsync(_clientTenantId, false, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierChatSession, string, SupplierChatMessage?, int)>
             {
                 (session, "Supplier Co", null, 1),
             });

        var sessions = await _sut.GetSessionsAsync(_clientTenantId, isSupplierSide: false);

        var dto = Assert.Single(sessions);
        Assert.Equal(1, dto.UnreadCount);
    }

    [Fact]
    public async Task GetSessionsAsync_NewMessageFromSelf_ShowsZeroUnreadForSender()
    {
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        // The sender's own view of the session never counts their own messages as unread.
        _repo.GetSessionsAsync(_supplierTenantId, true, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierChatSession, string, SupplierChatMessage?, int)>
             {
                 (session, "Client Co", null, 0),
             });

        var sessions = await _sut.GetSessionsAsync(_supplierTenantId, isSupplierSide: true);

        var dto = Assert.Single(sessions);
        Assert.Equal(0, dto.UnreadCount);
    }

    [Fact]
    public async Task GetMessagesAsync_MarksRead_ThenGetSessionsAsync_ShowsZeroUnread()
    {
        // Simulates: tenant B opens the thread (GetMessagesAsync marks A's messages read),
        // then a subsequent GetSessionsAsync call for B reflects UnreadCount == 0.
        var session = new SupplierChatSession { SupplierTenantId = _supplierTenantId, ClientTenantId = _clientTenantId };
        _repo.GetSessionByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _repo.GetMessagesAsync(session.Id, Arg.Any<CancellationToken>())
             .Returns(new List<SupplierChatMessage>
             {
                 new() { SessionId = session.Id, SenderTenantId = _supplierTenantId, SenderName = "Sup", Body = "Hi" },
             });

        await _sut.GetMessagesAsync(session.Id, _clientTenantId);

        await _repo.Received(1).MarkMessagesReadAsync(session.Id, _clientTenantId, Arg.Any<CancellationToken>());

        // After marking read, the repo (mocked here) would return UnreadCount 0 for B's session list.
        _repo.GetSessionsAsync(_clientTenantId, false, Arg.Any<CancellationToken>())
             .Returns(new List<(SupplierChatSession, string, SupplierChatMessage?, int)>
             {
                 (session, "Supplier Co", null, 0),
             });

        var sessions = await _sut.GetSessionsAsync(_clientTenantId, isSupplierSide: false);
        Assert.Equal(0, Assert.Single(sessions).UnreadCount);
    }
}
