using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Application.Features.BeautyControl;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.BeautyControl;

/// <summary>
/// TASK-825 interop: a request frozen from the real Beauty worker (worker/src/feedback/delivery.ts deliver(), vector
/// key test-k1, now=1793000000, fixed nonce) must pass the ShelfGuard verifier and end as a critical bug lead.
/// </summary>
public sealed class BeautyWorkerInteropTests
{
    private const long Ts = 1793000000;
    private const string Nonce = "WorkerNonce0000000000001";
    private const string Sig = "v1=cYBEhUI7BbGUtvBNHUJ7wrDLPLbbcD0zf25RRe_xep4";
    private const string Path = "/api/integrations/beauty/feedback";
    private const string BodyB64 =
        "eyJzb3VyY2VUZW5hbnRSZWYiOiJzZy0xMjMiLCJ0ZW5hbnROYW1lIjoiU2Fsb24gWCIsInR5cGUiOiJidWciLCJtZXNzYWdlIjoi0J3QtSDQstGW0LTQutGA0LjQstCw0ZTRgtGM0YHRjyDQutCw0LvQtdC90LTQsNGAINC80LDQudGB0YLRgNCwIiwiZmVlZGJhY2tJZCI6ImIxYjFiMWIxLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDAwMSIsInNldmVyaXR5Ijoibm9ybWFsIiwiY29udGFjdCI6Im93bmVyQHgudGVzdCIsInVzZXJSb2xlIjoib3duZXIiLCJwYWdlVXJsIjoiL2JlYXV0eS9jYWxlbmRhciJ9";

    private static BeautyInboundAuthenticator Auth() => new(new TestKeyProvider(), new FakeNonceStore(),
        new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(Ts)), NullLogger<BeautyInboundAuthenticator>.Instance);

    private static BeautyInboundAuthInput Input(string sig = Sig) =>
        new("POST", Path, "", VectorFile.Kid, Ts.ToString(), Nonce, sig, Convert.FromBase64String(BodyB64));

    [Fact]
    public void Worker_Signed_Request_Is_Accepted_Then_Replay_And_Bad_Signature_Are_Rejected()
    {
        var sut = Auth();
        Assert.Equal(BeautyAuthOutcome.Failed, Auth().Verify(Input("v1=" + new string('A', 43))));
        Assert.Equal(BeautyAuthOutcome.Ok, sut.Verify(Input()));
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input())); // same nonce
    }

    [Fact]
    public async Task Worker_Body_Deserializes_Like_The_Controller_And_Bug_Becomes_Critical_Lead()
    {
        var req = JsonSerializer.Deserialize<BeautyFeedbackRequest>(Convert.FromBase64String(BodyB64),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(req);

        var leads = Substitute.For<ILandingLeadRepository>();
        LandingLead? saved = null;
        await leads.AddAsync(Arg.Do<LandingLead>(l => saved = l), Arg.Any<CancellationToken>());
        var svc = new BeautyFeedbackService(leads, Substitute.For<IBeautyTenantLinkRepository>(),
            Substitute.For<ILeadNotifier>(), NullLogger<BeautyFeedbackService>.Instance);

        var res = await svc.IngestAsync(req!, CancellationToken.None);

        Assert.Equal(BeautyFeedbackStatus.Created, res.Status);
        Assert.Equal("beauty", saved!.Source);
        Assert.Equal("bug", saved.RequestType);
        Assert.Equal("sg-123", saved.BeautyTenantRef);
        Assert.Equal("b1b1b1b1-0000-4000-8000-000000000001", saved.ExternalId);
        Assert.Equal("owner", saved.ReporterRole);
        Assert.True(saved.IsCritical); // severity=normal, but bug -> critical
    }
}
