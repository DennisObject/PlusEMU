namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>A decoded and bounded variable write from the editor; packet framing stays in the handler.</summary>
public sealed record WiredVariableMenuWrite(int Action, WiredVariableTarget Target, int TargetId, uint DefinitionId, int Value, string Token);
