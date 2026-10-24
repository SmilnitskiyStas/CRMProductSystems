using System.Net;
using System.Net.NetworkInformation;

namespace ShelfGuard.Api.Infrastructure;

public static class WorkerPeerBoundary
{
    public const string OriginalPeer = "ShelfGuard.WorkerOriginalPeer";
    public static IApplicationBuilder UseWorkerPeerBoundary(this IApplicationBuilder app) => app.Use((context, next) => {
        // Capture before ForwardedHeaders can replace the peer with a client-supplied address.
        context.Items[OriginalPeer] = context.Connection.RemoteIpAddress;
        return next(context);
    });
    public static bool IsTrusted(HttpContext context, IConfiguration configuration)
    {
        if (context.Items[OriginalPeer] is not IPAddress address) return false;
        var peer = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (IPAddress.IsLoopback(peer)) return true;
        // Docker's loopback-published host port DNATs the host-network worker through
        // the container's bridge gateway. Gateway-container peers are different addresses.
        if (!configuration.GetValue<bool>("WorkerAuth:AllowHostBridge") || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true") return false;
        try {
            return NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Any(g => (g.Address.IsIPv4MappedToIPv6 ? g.Address.MapToIPv4() : g.Address).Equals(peer));
        }
        catch (NetworkInformationException) { return false; }
    }
}
