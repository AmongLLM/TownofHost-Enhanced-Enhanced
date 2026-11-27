using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP;
using InnerNet;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace TOHE;

internal static class CustomMapCompatibility
{
    public const string SubmergedGuid = "Submerged";
    public const string LevelImposterGuid = "com.DigiWorm.LevelImposter";

    // LevelImposter can preload a map while a vanilla map is selected.
    private const byte SubmergedMapId = 6;
    private const byte LevelImposterMapId = 7;
    internal const SystemTypes SubmergedOxygenSystem = (SystemTypes)0x82;
    private static byte? SelectedMapId => AmongUsClient.Instance && GameStates.IsFreePlay
        ? (byte)AmongUsClient.Instance.TutorialMapId
        : GameOptionsManager.Instance?.CurrentGameOptions?.MapId;
    internal static bool IsActive => SelectedMapId is SubmergedMapId or LevelImposterMapId || IsSubmergedShip;
    internal static bool IsLevelImposter => SelectedMapId == LevelImposterMapId;
    internal static bool IsSubmergedShip => ShipStatus.Instance && ShipStatus.Instance.Type == (ShipStatus.MapType)SubmergedMapId;
    internal static bool IsSubmergedOxygenRepair(SystemTypes systemType, byte amount) =>
        systemType == SubmergedOxygenSystem && amount == 64 && IsSubmergedShip && Utils.IsActive(systemType);

    private static MethodInfo getSubmergedFloorSystem;
    private static MethodInfo changeSubmergedFloor;
    private static byte unavailableHostMapId;
    private static ShipStatus cornerShip;
    private static Vector2 upperLeft;

    internal static string GetMapDescription()
    {
        var mapId = IsSubmergedShip ? SubmergedMapId : SelectedMapId;
        var ship = ShipStatus.Instance;
        return mapId switch
        {
            SubmergedMapId => $"Submerged ({mapId})",
            LevelImposterMapId when ship => $"LevelImposter ({mapId}) / {ship.name}",
            LevelImposterMapId => $"LevelImposter ({mapId})",
            byte id => $"{(MapNames)id} ({id})",
            _ => "Unknown"
        };
    }

    internal static void Initialize(Harmony harmony)
    {
        var plugins = IL2CPPChainloader.Instance.Plugins;
        bool submergedLoaded = plugins.TryGetValue(SubmergedGuid, out var submerged) && submerged.Instance != null;
        bool levelImposterLoaded = plugins.TryGetValue(LevelImposterGuid, out var levelImposter) && levelImposter.Instance != null;
        if (submergedLoaded)
        {
            var assembly = submerged.Instance.GetType().Assembly;
            var floorSystem = assembly.GetType("Submerged.Floors.SubmarinePlayerFloorSystem");
            if (floorSystem != null)
            {
                getSubmergedFloorSystem = AccessTools.PropertyGetter(floorSystem, "Instance");
                changeSubmergedFloor = AccessTools.Method(floorSystem, "ChangePlayerFloorState", [typeof(byte), typeof(bool)]);
            }
            PatchExile(harmony, assembly, "Submerged.ExileCutscene.SubmergedExileController", "WrapUpAndSpawn", nameof(SubmergedExilePostfix));
            Logger.Info("Loaded Submerged compatibility", "Custom Maps");
        }

        if (levelImposterLoaded)
        {
            PatchExile(harmony, levelImposter.Instance.GetType().Assembly,
                "LevelImposter.Core.Components.LIExileController", "WrapUpAlt", nameof(LevelImposterExilePostfix));
            Logger.Info("Loaded LevelImposter compatibility", "Custom Maps");
        }

        // The game shares saved host options across installs and assumes every map ID has an icon.
        if (submergedLoaded == levelImposterLoaded) return;
        unavailableHostMapId = submergedLoaded ? LevelImposterMapId : SubmergedMapId;
        harmony.Patch(AccessTools.Method(typeof(CreateGameOptions), nameof(CreateGameOptions.Show)),
            prefix: new HarmonyMethod(typeof(CustomMapCompatibility), nameof(CheckSavedHostMaps)));
        harmony.Patch(AccessTools.Method(typeof(InnerNetClient), nameof(InnerNetClient.HostGame)),
            prefix: new HarmonyMethod(typeof(CustomMapCompatibility), nameof(CheckHostMap)));
    }

    private static void CheckSavedHostMaps()
    {
        var manager = GameOptionsManager.Instance;
        CheckHostMap(manager.normalGameHostOptions?.Cast<IGameOptions>());
        CheckHostMap(manager.hideNSeekGameHostOptions?.Cast<IGameOptions>());
    }

    private static void CheckHostMap([HarmonyArgument(0)] IGameOptions options)
    {
        if (options == null || options.MapId != unavailableHostMapId) return;

        options.SetByte(ByteOptionNames.MapId, (byte)MapNames.Skeld);
        Logger.Info($"Saved map {unavailableHostMapId} is unavailable, using The Skeld.", "Custom Maps");
    }

    private static void PatchExile(Harmony harmony, Assembly assembly, string controllerName, string wrapUpName, string postfix)
    {
        var controller = assembly.GetType(controllerName);
        var wrapUp = controller == null ? null : AccessTools.DeclaredMethod(controller, wrapUpName);
        if (wrapUp == null)
        {
            Logger.Warn($"Could not find {controllerName}.{wrapUpName}", "Custom Maps");
            return;
        }

        harmony.Patch(wrapUp, postfix: new HarmonyMethod(typeof(CustomMapCompatibility), postfix));
    }

    private static void SubmergedExilePostfix(ExileController __instance, ref IEnumerator __result)
    {
        __result = FinishAfterSpawn(__result, __instance.initData?.networkedPlayer);
    }

    private static IEnumerator FinishAfterSpawn(IEnumerator spawn, NetworkedPlayerInfo exiled)
    {
        while (spawn.MoveNext()) yield return spawn.Current;
        ExileControllerWrapUpPatch.FinishMeeting(exiled, "SubmergedExileController.WrapUpAndSpawn");
    }

    private static void LevelImposterExilePostfix(ExileController __instance)
    {
        ExileControllerWrapUpPatch.FinishMeeting(__instance.initData?.networkedPlayer, "LIExileController.WrapUpAlt");
    }

    internal static void SyncSubmergedFloor(byte playerId, float y)
    {
        if (changeSubmergedFloor == null || !IsSubmergedShip) return;

        var floorSystem = getSubmergedFloorSystem?.Invoke(null, null);
        if (floorSystem != null)
            changeSubmergedFloor.Invoke(floorSystem, [playerId, y > -6.19f]); // FloorHandler.FLOOR_CUTOFF
    }

    internal static Vector2 GetUpperLeftPosition()
    {
        // Keep SnapTo within the coordinate range NetHelpers can serialize.
        const float coordinateLimit = 49f;
        var fallback = new Vector2(-coordinateLimit, coordinateLimit);
        var ship = ShipStatus.Instance;
        if (!ship) return fallback;
        if (ship == cornerShip) return upperLeft;

        float left = float.PositiveInfinity;
        float top = float.NegativeInfinity;
        foreach (var collider in ship.GetComponentsInChildren<Collider2D>())
        {
            if (!collider.enabled || collider.isTrigger || (Constants.ShipOnlyMask & (1 << collider.gameObject.layer)) == 0) continue;
            var bounds = collider.bounds;
            left = Mathf.Min(left, bounds.min.x);
            top = Mathf.Max(top, bounds.max.y);
        }

        cornerShip = ship;
        upperLeft = float.IsPositiveInfinity(left) ? fallback : new Vector2(
            Mathf.Clamp(left - 2f, -coordinateLimit, coordinateLimit),
            Mathf.Clamp(top + 2f, -coordinateLimit, coordinateLimit));
        return upperLeft;
    }
}
