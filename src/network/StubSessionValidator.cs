using System;

namespace Game.Network;

public static class StubSessionValidator
{
    public static bool TryValidate(
        string sessionToken,
        bool devMode,
        bool sessionValidateDisabled,
        out string accountId,
        out string displayName
    )
    {
        accountId = string.Empty;
        displayName = string.Empty;

        if (sessionValidateDisabled)
        {
            accountId = "local-disabled";
            displayName = "Player";
            return true;
        }

        if (devMode && !string.IsNullOrWhiteSpace(sessionToken))
        {
            accountId =
                $"dev-{sessionToken[..Math.Min(8, sessionToken.Length)]}";
            displayName = "Dev Player";
            return true;
        }

        return false;
    }
}
