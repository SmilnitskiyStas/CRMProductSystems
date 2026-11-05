using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShelfGuard.Application.Features.BeautyControl;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Integrations;
using Xunit;

namespace ShelfGuard.Tests.BeautyControl;

public sealed class BeautyFeedbackServiceTests
{
    private readonly ILandingLeadRepository _leads = Substitute.For<ILandingLeadRepository>();
    private readonly IBeautyTenantLinkRepository _links = Substitute.For<IBeautyTenantLinkRepository>();
    private readonly ILeadNotifier _notifier = Substitute.For<ILeadNotifier>();
    private readonly BeautyFeedbackService _sut;

    public BeautyFeedbackServiceTests() =>
        _sut = new(_leads, _links, _notifier, NullLogger<BeautyFeedbackService>.Instance);

    private static BeautyFeedbackRequest Req(string type = "improvement", string message = "Хотілося б експорт у CSV",
        string feedbackId = "fb-0001") =>
        new("11111111-1111-1111-1111-111111111111", "Salon X", type, message, "owner@x.test", "owner", feedbackId);

    [Fact]
    public async Task Creates_Beauty_Lead_With_Type_And_Tenant_Reference()
    {
        LandingLead? saved = null;
        await _leads.AddAsync(Arg.Do<LandingLead>(l => saved = l), Arg.Any<CancellationToken>());

        var result = await _sut.IngestAsync(Req(), CancellationToken.None);

        Assert.Equal(BeautyFeedbackStatus.Created, result.Status);
        Assert.NotNull(saved);
        Assert.Equal("beauty", saved!.Source);
        Assert.Equal("improvement", saved.RequestType);
        Assert.Equal("11111111-1111-1111-1111-111111111111", saved.BeautyTenantRef);
        Assert.Equal("fb-0001", saved.ExternalId);
        Assert.Equal("owner", saved.ReporterRole);
        Assert.False(saved.IsCritical);
        await _leads.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bug_Is_Always_Critical()
    {
        LandingLead? saved = null;
        await _leads.AddAsync(Arg.Do<LandingLead>(l => saved = l), Arg.Any<CancellationToken>());

        await _sut.IngestAsync(Req(type: "bug", message: "Не відкривається календар"), CancellationToken.None);

        Assert.True(saved!.IsCritical);
        Assert.Contains("bug", saved.CriticalReason);
    }

    [Fact]
    public void Evaluator_Bug_Rule_Combines_With_Existing_Reasons()
    {
        Assert.Equal("bug", LeadCriticalityEvaluator.Evaluate(null, "щось не працює", false, "bug"));
        Assert.Equal("urgent+bug", LeadCriticalityEvaluator.Evaluate(null, "терміново", false, "bug"));
        Assert.Null(LeadCriticalityEvaluator.Evaluate(null, "щось не працює", false, "improvement"));
        Assert.Null(LeadCriticalityEvaluator.Evaluate(null, "щось не працює", false));
    }

    [Fact]
    public async Task Duplicate_FeedbackId_Replays_Original_Lead_Without_Saving()
    {
        var original = LandingLead.Create("Salon X", "-", "Salon X", "m", "beauty", externalId: "fb-0001");
        _leads.GetByExternalIdAsync("fb-0001", Arg.Any<CancellationToken>()).Returns(original);

        var result = await _sut.IngestAsync(Req(), CancellationToken.None);

        Assert.Equal(BeautyFeedbackStatus.Replayed, result.Status);
        Assert.Equal(original.Id, result.LeadId);
        await _leads.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<LeadNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lost_Race_On_Unique_Index_Becomes_Replay()
    {
        var winner = LandingLead.Create("Salon X", "-", "Salon X", "m", "beauty", externalId: "fb-0001");
        _leads.GetByExternalIdAsync("fb-0001", Arg.Any<CancellationToken>()).Returns(null, winner);
        _leads.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("dup"));

        var result = await _sut.IngestAsync(Req(), CancellationToken.None);

        Assert.Equal(BeautyFeedbackStatus.Replayed, result.Status);
        Assert.Equal(winner.Id, result.LeadId);
    }

    [Theory]
    [InlineData("invalid_type", "weird", "msg", "fb-1")]
    [InlineData("invalid_message", "bug", "  ", "fb-1")]
    [InlineData("invalid_feedback_id", "bug", "msg", "bad id!")]
    public async Task Invalid_Input_Is_Rejected(string code, string type, string message, string feedbackId)
    {
        var result = await _sut.IngestAsync(Req(type, message, feedbackId), CancellationToken.None);
        Assert.Equal(BeautyFeedbackStatus.Invalid, result.Status);
        Assert.Equal(code, result.ErrorCode);
        await _leads.DidNotReceive().AddAsync(Arg.Any<LandingLead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nested_Contract_Shape_Is_Accepted()
    {
        var nested = new BeautyFeedbackRequest(null, null, "bug", "Не працює оплата", null, null, "fb-0002",
            Tenant: new("sg-123", "t-uuid", "salon-x", "Salon X"), User: new("admin", "a@x.test"));
        var result = await _sut.IngestAsync(nested, CancellationToken.None);
        Assert.Equal(BeautyFeedbackStatus.Created, result.Status);
    }
}

public sealed class BeautyProvisioningServiceTests
{
    private const string InviteUrl = "https://app.example/invite#t=SECRET-TOKEN-VALUE";
    private readonly IBeautyControlClient _client = Substitute.For<IBeautyControlClient>();
    private readonly IBeautyTenantLinkRepository _links = Substitute.For<IBeautyTenantLinkRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IActivityLogRepository _logs = Substitute.For<IActivityLogRepository>();
    private readonly List<ActivityLog> _logged = [];
    private readonly BeautyProvisioningService _sut;
    private static readonly BeautyActor Actor = new("prov-1", "Provider");
    private readonly Guid _tenantId = Guid.NewGuid();

    public BeautyProvisioningServiceTests()
    {
        _tenants.GetByIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(Tenant.Create("Acme", "acme"));
        _logs.LogAsync(Arg.Do<ActivityLog>(_logged.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _sut = new(_client, _links, _tenants, _logs, TimeProvider.System, NullLogger<BeautyProvisioningService>.Instance);
    }

    [Fact]
    public async Task Create_Returns_Invite_Once_And_Audit_Has_No_Token_Or_Url()
    {
        _client.CreateTenantAsync(Arg.Any<BeautyCreateTenantCommand>(), Arg.Any<CancellationToken>())
            .Returns(new BeautyResult<BeautyCreatedTenant>(new BeautyCreatedTenant(
                Guid.NewGuid(), _tenantId.ToString(), "salon-x", "active", ["beauty_booking"],
                new BeautyOwnerInfo(Guid.NewGuid(), "o@x.test", "invited"),
                new BeautyInviteInfo(InviteUrl, DateTimeOffset.UtcNow.AddDays(3))), HttpStatus: 201));

        var result = await _sut.CreateAsync(
            new(_tenantId, "Salon X", "Salon-X", "o@x.test", null, null, null), Actor, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(InviteUrl, result.Data!.InviteUrl);
        await _client.Received(1).CreateTenantAsync(
            Arg.Is<BeautyCreateTenantCommand>(c => c.ExternalRef == _tenantId.ToString("D") && c.Slug == "salon-x" && c.OwnerFullName == "o"),
            Arg.Any<CancellationToken>());
        var entry = Assert.Single(_logged);
        Assert.Equal("beauty.tenant.created", entry.Action);
        Assert.Equal(_tenantId, entry.TenantId);
        Assert.DoesNotContain("SECRET-TOKEN-VALUE", entry.Meta ?? "");
        Assert.DoesNotContain("invite", entry.Meta ?? "", StringComparison.OrdinalIgnoreCase);
        await _links.Received(1).AddAsync(Arg.Any<BeautyTenantLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnerInvite_Audit_Has_No_Token_And_Link_Is_Required()
    {
        var link = BeautyTenantLink.Create(_tenantId, null, "salon-x", "active", DateTime.UtcNow);
        _links.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(link);
        _client.IssueOwnerInviteAsync(Arg.Any<string>(), Arg.Any<BeautyActor>(), Arg.Any<CancellationToken>())
            .Returns(new BeautyResult<BeautyInviteInfo>(new BeautyInviteInfo(InviteUrl, DateTimeOffset.UtcNow), HttpStatus: 201));

        var result = await _sut.IssueOwnerInviteAsync(_tenantId, Actor, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(InviteUrl, result.Data!.InviteUrl);
        var entry = Assert.Single(_logged);
        Assert.Equal("beauty.owner_invite.issued", entry.Action);
        Assert.DoesNotContain("SECRET-TOKEN-VALUE", entry.Meta ?? "");
        Assert.DoesNotContain("https://", entry.Meta ?? "");

        var other = await _sut.IssueOwnerInviteAsync(Guid.NewGuid(), Actor, null, CancellationToken.None);
        Assert.Equal(BeautyErrorKind.NotFound, other.Error);
    }

    [Fact]
    public async Task Suspend_Without_Reason_Uses_Default_And_Updates_Link()
    {
        var link = BeautyTenantLink.Create(_tenantId, null, "salon-x", "active", DateTime.UtcNow);
        _links.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(link);
        _client.SuspendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BeautyActor>(), Arg.Any<CancellationToken>())
            .Returns(new BeautyResult<BeautyStatusInfo>(new BeautyStatusInfo("suspended", DateTimeOffset.UtcNow)));

        var result = await _sut.SuspendAsync(_tenantId, new(null), Actor, null, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("suspended", link.BeautyStatus);
        await _client.Received(1).SuspendAsync(Arg.Any<string>(), "suspended by provider operator", Actor, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_Beauty_Call_Writes_No_Audit()
    {
        var link = BeautyTenantLink.Create(_tenantId, null, "salon-x", "active", DateTime.UtcNow);
        _links.GetByTenantIdAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(link);
        _client.ResumeAsync(Arg.Any<string>(), Arg.Any<BeautyActor>(), Arg.Any<CancellationToken>())
            .Returns(BeautyResult<BeautyStatusInfo>.Fail(BeautyErrorKind.Unavailable, "beauty_unavailable"));

        var result = await _sut.ResumeAsync(_tenantId, Actor, null, CancellationToken.None);

        Assert.Equal(BeautyErrorKind.Unavailable, result.Error);
        Assert.Empty(_logged);
    }
}

public sealed class BeautyControlClientTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public byte[] Body = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }

    private static (BeautyControlClient Client, CaptureHandler Handler) Make(bool enabled = true, string baseUrl = "https://beauty.example")
    {
        var handler = new CaptureHandler();
        var opts = new BeautyControlOptions
        {
            Enabled = enabled, BaseUrl = baseUrl, KeyId = VectorFile.Kid, Secret = VectorFile.SecretBase64,
        };
        var clock = new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1793700000));
        var monitor = Substitute.For<IOptionsMonitor<BeautyControlOptions>>();
        monitor.CurrentValue.Returns(opts);
        return (new BeautyControlClient(new HttpClient(handler), monitor, clock, NullLogger<BeautyControlClient>.Instance), handler);
    }

    [Fact]
    public async Task Request_Is_Signed_Exactly_As_Contract_Specifies()
    {
        var (client, handler) = Make();
        handler.Response = """{"status":"suspended","suspendedAt":"2026-11-04T10:00:00Z"}""";

        var result = await client.SuspendAsync("sg-test-123", "Несплата рахунку", new("sg-admin-test", "Тест Адмін"), CancellationToken.None);

        Assert.True(result.Ok);
        var req = handler.Request!;
        Assert.Equal("/api/control/v1/tenants/sg-test-123/suspend", req.RequestUri!.AbsolutePath);
        string H(string n) => req.Headers.GetValues(n).Single();
        var expected = BeautyHmac.Sign(VectorFile.Key, H("X-BCRM-Key-Id"), H("X-BCRM-Timestamp"), H("X-BCRM-Nonce"),
            "POST", req.RequestUri.AbsolutePath, "", handler.Body);
        Assert.Equal(expected, H("X-BCRM-Signature"));
        Assert.Equal("1793700000", H("X-BCRM-Timestamp"));
        Assert.Matches("^[A-Za-z0-9_-]{22,64}$", H("X-BCRM-Nonce"));
        Assert.Equal("application/json; charset=utf-8", req.Content!.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Nonce_Is_New_For_Every_Request_And_Get_Has_Empty_Body()
    {
        var (client, handler) = Make();
        handler.Response = """{"status":"active"}""";
        await client.GetTenantAsync("sg-test-123", CancellationToken.None);
        var n1 = handler.Request!.Headers.GetValues("X-BCRM-Nonce").Single();
        Assert.Empty(handler.Body);
        await client.GetTenantAsync("sg-test-123", CancellationToken.None);
        Assert.NotEqual(n1, handler.Request!.Headers.GetValues("X-BCRM-Nonce").Single());
    }

    [Fact]
    public async Task Disabled_Client_Does_Not_Call_Beauty()
    {
        var (client, handler) = Make(enabled: false);
        var result = await client.ResumeAsync("sg-1", new("a", "b"), CancellationToken.None);
        Assert.Equal(BeautyErrorKind.Disabled, result.Error);
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, BeautyErrorKind.NotFound, """{"code":"tenant_not_found"}""", "tenant_not_found")]
    [InlineData(HttpStatusCode.Conflict, BeautyErrorKind.Conflict, """{"code":"tenant_slug_taken"}""", "tenant_slug_taken")]
    [InlineData(HttpStatusCode.Unauthorized, BeautyErrorKind.AuthFailed, """{"code":"control_auth_failed"}""", "beauty_auth_failed")]
    [InlineData(HttpStatusCode.UnprocessableEntity, BeautyErrorKind.Validation, """{"code":"Weird <script>"}""", "beauty_validation_failed")]
    [InlineData(HttpStatusCode.BadGateway, BeautyErrorKind.Unavailable, "boom", "beauty_unavailable")]
    public async Task Errors_Are_Mapped_To_Typed_Results_Without_Raw_Bodies(
        HttpStatusCode status, BeautyErrorKind kind, string body, string code)
    {
        var (client, handler) = Make();
        handler.Status = status;
        handler.Response = body;
        var result = await client.ResumeAsync("sg-1", new("a", "b"), CancellationToken.None);
        Assert.Equal(kind, result.Error);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public async Task Create_Replay_Is_Detected_From_200()
    {
        var (client, handler) = Make();
        handler.Status = HttpStatusCode.OK;
        handler.Response = """{"slug":"s","status":"active","invite":null}""";
        var result = await client.CreateTenantAsync(
            new("sg-1", "N", "s", null, "o@x.test", "O", new("a", "b")), CancellationToken.None);
        Assert.True(result.Ok);
        Assert.True(result.Replayed);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Redirect_Is_Bad_Response_And_Not_Followed(HttpStatusCode status)
    {
        var (client, handler) = Make();
        handler.Status = status;
        var result = await client.GetTenantAsync("sg-1", CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Equal("beauty_bad_response", result.Code);
    }

    [Theory]
    [InlineData("http://beauty.example", false)]
    [InlineData("http://10.0.0.5", false)]
    [InlineData("ftp://beauty.example", false)]
    [InlineData("https://user:pw@beauty.example", false)]
    [InlineData("https://beauty.example", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://127.0.0.1:5000", true)]
    public async Task BaseUrl_Must_Be_Https_Or_Loopback_Else_Fail_Closed(string url, bool allowed)
    {
        var (client, handler) = Make(baseUrl: url);
        handler.Response = "{\"status\":\"active\"}";
        var result = await client.GetTenantAsync("sg-1", CancellationToken.None);
        if (allowed) Assert.NotNull(handler.Request);
        else
        {
            Assert.Equal(BeautyErrorKind.Disabled, result.Error);
            Assert.Null(handler.Request);
        }
    }
}
