using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Items.Wired;

// A firing owns its arguments; delayed actions and stack calls never store an actor on a box.
internal sealed record WiredExecutionContext(object[] Arguments, int Depth, object? ActorVisit = null, WiredRuntimeContext? Runtime = null);
