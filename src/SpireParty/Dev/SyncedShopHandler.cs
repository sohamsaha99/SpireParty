using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.AutoSlay.Handlers;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireParty.Dev;

/// <summary>
/// The autopilot's shop step. Like the game's AutoSlay <c>ShopRoomHandler</c> (open the inventory, buy random
/// affordable items, close, Proceed), but every purchase goes through
/// <c>MerchantSynchronizer.PurchaseLocalEntry</c>, which is what clicking a slot does (<c>NMerchantSlot.OnSelected</c>).
/// AutoSlay's handler calls <c>MerchantEntry.OnTryPurchaseUnsynchronized</c>: the purchase happens only on the buyer's
/// own game, the other players never see it, and a Blitz shop reward then diverges (run b5, findings → M3, task 3.9).
/// </summary>
public sealed class SyncedShopHandler : IRoomHandler
{
    readonly Func<Task, CancellationToken, Task> _drainOverlayScreensUntil;

    public SyncedShopHandler(Func<Task, CancellationToken, Task> drainOverlayScreensUntil) =>
        _drainOverlayScreensUntil = drainOverlayScreensUntil;

    public RoomType[] HandledTypes => new[] { RoomType.Shop };
    public TimeSpan Timeout => TimeSpan.FromSeconds(120);

    public async Task HandleAsync(Rng random, CancellationToken ct)
    {
        await WaitHelper.Until(() => NMerchantRoom.Instance?.Visible ?? false, ct, null, "Merchant room not found");
        var room = NMerchantRoom.Instance!;
        room.OpenInventory();
        await Task.Delay(500, ct);
        for (int attempt = 0; attempt < 50; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var slots = room.Inventory.GetAllSlots()
                .Where(s => s is not NMerchantCardRemoval && s.Entry is { IsStocked: true, EnoughGold: true })
                .ToList();
            if (slots.Count == 0) break;
            var slot = random.NextItem(slots)!;
            Log.Info($"{ModEntry.LogTag} autopilot: buying {slot.GetType().Name} (cost {slot.Entry.Cost})");
            await _drainOverlayScreensUntil(RunManager.Instance.MerchantSynchronizer.PurchaseLocalEntry(slot.Entry), ct);
            await Task.Delay(300, ct);
        }
        if (UiHelper.FindFirst<NBackButton>(room) is { } back)
        {
            await UiHelper.Click(back);
            await Task.Delay(300, ct);
        }
        Log.Info($"{ModEntry.LogTag} autopilot: shop proceed");
        await UiHelper.Click(room.ProceedButton);
    }
}
