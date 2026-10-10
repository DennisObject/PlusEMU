using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern.Actions;

namespace Plus.HabboHotel.Items.Interactor;

internal class InteractorFreezeTimer : IFurniInteractor
{
    public void OnPlace(GameClient? session, Item item)
    {
        if (WiredCounterController.Recognizes(item)) {
            item.GetRoom()?.GetWired().AttachRoomItem(item);

            return;
        }

        item.LegacyDataString = "30";
        item.UpdateState();
    }

    public void OnRemove(GameClient? session, Item item) { }

    public void OnTrigger(GameClient? session, Item item, int request, bool hasRights)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        if (!hasRights) {
            return;
        }

        if (WiredCounterController.Recognizes(item)) {
            itemRoom.GetWired().TryUseCounter(item, request);

            return;
        }

        var oldValue = 0;

        if (!int.TryParse(item.LegacyDataString, out oldValue)) {
            item.LegacyDataString = "30";
            oldValue = 30;
        }

        if (request == 0 && oldValue == 0) {
            oldValue = 30;
        }
        else if (request == 2) {
            if (itemRoom.GetFreeze().GameIsStarted && item.PendingReset && oldValue > 0) {
                oldValue = 0;
                item.PendingReset = false;
            }
            else {
                if (oldValue < 30) {
                    oldValue = 30;
                }
                else if (oldValue == 30) {
                    oldValue = 60;
                }
                else if (oldValue == 60) {
                    oldValue = 120;
                }
                else if (oldValue == 120) {
                    oldValue = 180;
                }
                else if (oldValue == 180) {
                    oldValue = 300;
                }
                else if (oldValue == 300) {
                    oldValue = 600;
                }
                else {
                    oldValue = 0;
                }

                item.UpdateNeeded = false;
            }
        }
        else if (request == 1 || request == 0) {
            if (request == 1 && oldValue == 0) {
                item.LegacyDataString = "30";
                oldValue = 30;
            }

            if (!itemRoom.GetFreeze().GameIsStarted) {
                item.UpdateNeeded = !item.UpdateNeeded;

                if (item.UpdateNeeded) {
                    itemRoom.GetFreeze().StartGame();
                }

                item.PendingReset = true;
            }
            else {
                item.UpdateNeeded = !item.UpdateNeeded;

                if (item.UpdateNeeded) {
                    itemRoom.GetFreeze().StopGame(true);
                }

                item.PendingReset = true;
            }
        }

        item.LegacyDataString = Convert.ToString(oldValue);
        item.UpdateState();
    }

    public void OnWiredTrigger(Item item)
    {
        var itemRoom = item.GetRoom();

        if (itemRoom == null) {
            return;
        }

        if (itemRoom.GetFreeze().GameIsStarted) {
            itemRoom.GetFreeze().StopGame(true);
        }

        item.PendingReset = true;
        item.UpdateNeeded = true;
        item.LegacyDataString = "30";
        item.UpdateState();
        itemRoom.GetFreeze().StartGame();
    }
}
