namespace TOHE.Patches;

[HarmonyPatch(typeof(HauntMenuMinigame), nameof(HauntMenuMinigame.SetFilterText))]
public static class HauntMenuMinigameSetFilterTextPatch
{
    public static bool Prefix(HauntMenuMinigame __instance)
    {
        var seer = PlayerControl.LocalPlayer;
        var target = __instance.HauntTarget;
        if (!GameStates.IsModHost || !GameStates.IsNormalGame || target == null
            || !Main.PlayerStates.ContainsKey(target.PlayerId) || !seer.CanSeeOtherRolesAsGhost()) return true;

        __instance.FilterText.text = seer.GetDisplayRoleAndSubName(target, isMeeting: false);
        return false;
    }
}
