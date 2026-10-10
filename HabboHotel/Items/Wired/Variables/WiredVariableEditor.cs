using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Writes the explicit room value a definition save may carry, through normal authorization and change events.</summary>
public sealed class WiredVariableEditor(WiredVariableModule variables)
{
    /// <summary>After a definition configuration save, persist its explicit room value through normal authorization and change events.</summary>
    public bool SaveGlobalValue(uint definitionId, long value) => variables.SaveGlobalValue(definitionId, value);
}
