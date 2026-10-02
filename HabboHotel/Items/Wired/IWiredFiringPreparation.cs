namespace Plus.HabboHotel.Items.Wired;

// Legacy feedback happens when a firing is accepted, before its delayed action runs.
internal interface IWiredFiringPreparation
{
    bool Prepare(params object[] arguments);
}
