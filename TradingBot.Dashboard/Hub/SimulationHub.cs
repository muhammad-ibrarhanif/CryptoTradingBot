using Microsoft.AspNetCore.SignalR;
using TradingBot.Dashboard.Controllers;
using TradingBot.Dashboard.Models;

namespace TradingBot.Dashboard.Hubs;

public class SimulationHub : Hub
{
    public async Task SendState(SimulationState state)
    {
        await Clients.All.SendAsync("ReceiveState", state);
    }

    public async Task SendSignal(TradingSignal signal)
    {
        await Clients.All.SendAsync("ReceiveSignal", signal);
    }

    public async Task SendComplete(SimulationComplete complete)
    {
        await Clients.All.SendAsync("ReceiveComplete", complete);
    }

    public async Task SendProgress(int current, int total, string message)
    {
        await Clients.All.SendAsync("ReceiveProgress", new { current, total, message });
    }
}