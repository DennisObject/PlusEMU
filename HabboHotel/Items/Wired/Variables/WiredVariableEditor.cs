using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Overlay live room-variable values when reopening an editor; the saved definition is not a value cache.</summary>
public sealed class WiredVariableEditor(WiredVariableModule variables)
{
    public WiredConfiguration ForDisplay(string name, uint definitionId, WiredConfiguration saved)
    {
        if (name != "wf_var_room" || saved.IntParams.Length is not (2 or 4)) {
            return saved;
        }

        var value = variables.Read(new(WiredVariableTarget.Global, $"custom:{definitionId}"), new(WiredVariableTarget.Global, 0, 0), new(variables.RoomId, []));

        if (value is null) {
            return saved;
        }

        return value.Value is >= int.MinValue and <= int.MaxValue && saved.IntParams.Length == 2
            ? saved with { IntParams = saved.IntParams.SetItem(1, (int)value.Value) }
            : saved with { IntParams = [saved.IntParams[0], 1, unchecked((int)(value.Value >> 32)), unchecked((int)value.Value)] };
    }

    /// <summary>After a definition configuration save, persist its explicit room value through normal authorization and change events.</summary>
    public bool SaveGlobalValue(uint definitionId, long value) => variables.SaveGlobalValue(definitionId, value);
}
