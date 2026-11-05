using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ShelfGuard.Application.Features.BeautyControl;
using Xunit;

namespace ShelfGuard.Tests.BeautyControl;

/// <summary>Signature v1 against the shared ADR-018 test vector, plus the inbound verifier rules.</summary>
public sealed class BeautyHmacTests
{
    public static IEnumerable<object[]> Names => VectorFile.Vectors.Select(v => new object[] { v.Name });

    [Theory, MemberData(nameof(Names))]
    public void Canonical_And_Signature_Match_Vector(string name)
    {
        var v = VectorFile.Get(name);
        var canonical = BeautyHmac.Canonical(VectorFile.Kid, v.Timestamp.ToString(), v.Nonce, v.Method, v.Path, v.Query, v.Body);
        Assert.Equal(v.Canonical, canonical);
        Assert.Equal(v.ExpectedSignature, BeautyHmac.Sign(VectorFile.Key, canonical));
    }

    [Theory, MemberData(nameof(Names))]
    public void One_Extra_Body_Byte_Changes_Signature(string name)
    {
        var v = VectorFile.Get(name);
        var tampered = v.Body.Concat(Encoding.UTF8.GetBytes(" ")).ToArray();
        var sig = BeautyHmac.Sign(VectorFile.Key, VectorFile.Kid, v.Timestamp.ToString(), v.Nonce, v.Method, v.Path, v.Query, tampered);
        Assert.NotEqual(v.ExpectedSignature, sig);
        Assert.Equal(v.TrailingSpaceSignature, sig);
    }

    [Fact]
    public void Changing_Any_Canonical_Field_Changes_Signature()
    {
        var v = VectorFile.Get("create_tenant_post");
        string S(string kid = VectorFile.Kid, long ts = -1, string? nonce = null, string? method = null, string? path = null) =>
            BeautyHmac.Sign(VectorFile.Key, kid, (ts < 0 ? v.Timestamp : ts).ToString(), nonce ?? v.Nonce,
                method ?? v.Method, path ?? v.Path, "", v.Body);
        Assert.Equal(v.ExpectedSignature, S());
        Assert.NotEqual(v.ExpectedSignature, S(kid: "test-k2"));
        Assert.NotEqual(v.ExpectedSignature, S(ts: v.Timestamp + 1));
        Assert.NotEqual(v.ExpectedSignature, S(nonce: "TEST_nonce_0000000000000009"));
        Assert.NotEqual(v.ExpectedSignature, S(method: "PUT"));
        Assert.NotEqual(v.ExpectedSignature, S(path: "/api/control/v1/tenants/"));
    }

    [Fact]
    public void Key_Is_Base64_Decoded_Raw_Bytes_And_Fingerprint_Matches()
    {
        Assert.True(BeautyHmac.TryDecodeKey(VectorFile.SecretBase64, out var key));
        Assert.Equal(32, key.Length);
        Assert.Equal(VectorFile.Fingerprint, BeautyHmac.Fingerprint(key));
        Assert.False(BeautyHmac.TryDecodeKey("c2hvcnQ=", out _)); // < 32 bytes: fail closed
        Assert.False(BeautyHmac.TryDecodeKey("not base64!!", out _));
    }

    // ── Inbound verifier ────────────────────────────────────────────────────

    private static (BeautyInboundAuthenticator Sut, FixedTimeProvider Clock) Make(string status = "active")
    {
        var clock = new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(VectorFile.Get("create_tenant_post").Timestamp));
        return (new BeautyInboundAuthenticator(new TestKeyProvider(status), new FakeNonceStore(), clock,
            NullLogger<BeautyInboundAuthenticator>.Instance), clock);
    }

    private static BeautyInboundAuthInput Input(Vector v, byte[]? body = null, string? sig = null, string? query = null) =>
        new(v.Method, v.Path, query ?? v.Query, VectorFile.Kid, v.Timestamp.ToString(), v.Nonce, sig ?? v.ExpectedSignature, body ?? v.Body);

    [Theory, MemberData(nameof(Names))]
    public void Valid_Vector_Request_Is_Accepted(string name)
    {
        var (sut, clock) = Make();
        var v = VectorFile.Get(name);
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(v.Timestamp);
        Assert.Equal(BeautyAuthOutcome.Ok, sut.Verify(Input(v)));
    }

    [Fact]
    public void Retiring_Key_Is_Accepted_Unknown_Status_Is_Not()
    {
        Assert.Equal(BeautyAuthOutcome.Ok, Make("retiring").Sut.Verify(Input(VectorFile.Get("create_tenant_post"))));
        Assert.Equal(BeautyAuthOutcome.Failed, Make("disabled").Sut.Verify(Input(VectorFile.Get("create_tenant_post"))));
    }

    [Fact]
    public void Replayed_Nonce_Is_Rejected()
    {
        var (sut, _) = Make();
        var v = VectorFile.Get("create_tenant_post");
        Assert.Equal(BeautyAuthOutcome.Ok, sut.Verify(Input(v)));
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input(v)));
    }

    [Fact]
    public void Bad_Signature_Does_Not_Burn_The_Nonce()
    {
        var (sut, _) = Make();
        var v = VectorFile.Get("create_tenant_post");
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input(v, sig: v.TrailingSpaceSignature)));
        Assert.Equal(BeautyAuthOutcome.Ok, sut.Verify(Input(v)));
    }

    [Fact]
    public void Tampered_Body_Is_Rejected()
    {
        var (sut, _) = Make();
        var v = VectorFile.Get("create_tenant_post");
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input(v, body: v.Body.Concat(new byte[] { 32 }).ToArray())));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(-301)]
    public void Timestamp_Outside_Window_Is_Rejected(int skew)
    {
        var (sut, clock) = Make();
        clock.Now = clock.Now.AddSeconds(skew);
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input(VectorFile.Get("create_tenant_post"))));
    }

    [Fact]
    public void Timestamp_At_Window_Edge_Is_Accepted()
    {
        var (sut, clock) = Make();
        clock.Now = clock.Now.AddSeconds(300);
        Assert.Equal(BeautyAuthOutcome.Ok, sut.Verify(Input(VectorFile.Get("create_tenant_post"))));
    }

    [Fact]
    public void Unknown_Kid_And_Non_Empty_Query_Are_Rejected()
    {
        var (sut, _) = Make();
        var v = VectorFile.Get("create_tenant_post");
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input(v) with { KeyId = "other-key" }));
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input(v, query: "a=1")));
        Assert.Equal(BeautyAuthOutcome.Failed, sut.Verify(Input(v) with { Signature = null }));
    }

    [Fact]
    public void Oversized_Body_Is_Rejected()
    {
        var (sut, _) = Make();
        Assert.Equal(BeautyAuthOutcome.Failed,
            sut.Verify(Input(VectorFile.Get("create_tenant_post"), body: new byte[BeautyHmac.MaxBodyBytes + 1])));
    }

    [Fact]
    public void Rate_Limit_Is_Per_Kid()
    {
        var (sut, _) = Make();
        var v = VectorFile.Get("create_tenant_post");
        BeautyAuthOutcome last = BeautyAuthOutcome.Ok;
        for (var n = 0; n <= BeautyInboundAuthenticator.RateLimitPerMinute; n++)
        {
            var nonce = $"TEST_nonce_{n:D16}ABCDEF";
            var sig = BeautyHmac.Sign(VectorFile.Key, VectorFile.Kid, v.Timestamp.ToString(), nonce, v.Method, v.Path, "", v.Body);
            last = sut.Verify(new BeautyInboundAuthInput(v.Method, v.Path, "", VectorFile.Kid, v.Timestamp.ToString(), nonce, sig, v.Body));
        }
        Assert.Equal(BeautyAuthOutcome.RateLimited, last);
    }
}
