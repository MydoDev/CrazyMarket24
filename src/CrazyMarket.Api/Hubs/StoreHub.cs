using Microsoft.AspNetCore.SignalR;

namespace CrazyMarket.Api.Hubs;

/// <summary>
/// Jediný real-time kanál prodejny. Klienti se po připojení zařadí do skupiny podle své role
/// (buď query parametrem <c>?client=admin</c>, nebo voláním <see cref="JoinGroup"/>),
/// server pak cílí události jen tam, kam patří (např. výzva k platbě jen do simulátoru).
/// </summary>
public class StoreHub(ILogger<StoreHub> logger) : Hub<IStoreHubClient>
{
    public const string Path = "/hubs/store";

    public override async Task OnConnectedAsync()
    {
        var client = Context.GetHttpContext()?.Request.Query["client"].ToString();
        if (!string.IsNullOrEmpty(client))
        {
            await JoinGroup(client);
        }

        logger.LogInformation("SignalR připojen: {ConnectionId} (client={Client})", Context.ConnectionId, client);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("SignalR odpojen: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGroup(string group)
    {
        if (!HubGroups.All.Contains(group))
            throw new HubException($"Neznámá skupina '{group}'. Povolené: {string.Join(", ", HubGroups.All)}.");

        await Groups.AddToGroupAsync(Context.ConnectionId, group.ToLowerInvariant());
    }

    public Task LeaveGroup(string group) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, group.ToLowerInvariant());

    /// <summary>Diagnostika spojení z klienta / simulátoru.</summary>
    public string Ping() => $"pong {DateTime.UtcNow:O}";
}

/// <summary>Názvy SignalR skupin podle typu klienta.</summary>
public static class HubGroups
{
    public const string Admin = "admin";
    public const string Kiosk = "kiosk";
    public const string Simulator = "simulator";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Admin, Kiosk, Simulator
    };
}
