using AmongUs.GameOptions;
using System;
using TOHE.Roles.AddOns;
using TOHE.Roles.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using static TOHE.Translator;

namespace TOHE;

[HarmonyPatch(typeof(TaskAdderGame), nameof(TaskAdderGame.ShowFolder))]
public static class ShowFolderPatch
{
    private const string ModeFolderName = "TOHEEFreeplayModes";
    private const string RoleRootName = "TOHEEFreeplayRoles";
    private static TaskFolder CustomRolesFolder;
    private static TaskFolder modesFolder;
    private static TaskFolder injectedRoot;
    private static CustomGameMode roleFolderMode;
    private static TaskAdderGame owner;
    private static Transform scaledTaskParent;
    private static Vector3 originalTaskScale;
    private static readonly List<TaskFolder> injectedFolders = [];
    private static readonly Dictionary<string, CustomRoles[]> RoleFolders = [];
    internal static readonly Dictionary<TaskAddButton, CustomRoles> CustomButtons = [];
    internal static readonly Dictionary<TaskAddButton, CustomGameMode> ModeButtons = [];
    private static readonly (CustomGameMode Mode, string Name)[] FreeplayModes =
    [
        (CustomGameMode.Standard, "Standard"),
        (CustomGameMode.FFA, "FFA"),
        (CustomGameMode.CandR, "C&R"),
        (CustomGameMode.Speedrun, "Speedrun")
    ];
    public static bool TryGetCustomRole(TaskAddButton button, out CustomRoles role)
        => CustomButtons.TryGetValue(button, out role);

    internal static void Release(TaskAdderGame instance)
    {
        if (owner != instance) return;
        RestoreTaskScale();
        // Destroy is deferred. Prevent late Start/Update callbacks from treating
        // these custom buttons as native task buttons after their mapping is gone.
        foreach (var button in CustomButtons.Keys.Concat(ModeButtons.Keys))
        {
            if (button == null) continue;
            button.gameObject.SetActive(false);
            if (ControllerManager.Instance != null && button.Button != null)
                ControllerManager.Instance.RemoveSelectableUiElement(button.Button);
        }
        if (injectedRoot)
        {
            injectedRoot.SubFolders.Remove(CustomRolesFolder);
            injectedRoot.SubFolders.Remove(modesFolder);
        }
        foreach (var folder in injectedFolders)
            if (folder) Object.Destroy(folder.gameObject);
        injectedFolders.Clear();
        injectedRoot = null;
        owner = null;
        CustomRolesFolder = null;
        modesFolder = null;
        CustomButtons.Clear();
        ModeButtons.Clear();
        RoleFolders.Clear();
    }

    internal static void Prefix(TaskAdderGame __instance, [HarmonyArgument(0)] TaskFolder taskFolder)
    {
        if (!GameStates.IsFreePlay || !FreeplayInitialization.IsReady) return;

        // Only expose native roles that have a matching mod role wrapper.
        for (int index = taskFolder.RoleChildren.Count - 1; index >= 0; index--)
            if (!AddTaskButtonPatch.TryMapNativeRole(taskFolder.RoleChildren[index].Role, out _))
                taskFolder.RoleChildren.RemoveAt(index);

        // Native ShowFolder destroys old items at end of frame. Retire their controller entries now.
        foreach (var item in __instance.ActiveItems)
        {
            if (item == null) continue;
            foreach (var element in item.GetComponentsInChildren<UiElement>(true))
                ControllerManager.Instance.RemoveSelectableUiElement(element);
        }
        foreach (var button in CustomButtons.Keys.Concat(ModeButtons.Keys))
            if (button != null) button.gameObject.SetActive(false);
        RestoreTaskScale();
        CustomButtons.Clear();
        ModeButtons.Clear();
        bool showingRoot = __instance.Root == taskFolder;
        if (owner != __instance || injectedRoot &&
            (injectedRoot != __instance.Root || showingRoot && roleFolderMode != FreeplayInitialization.Mode))
        {
            Release(owner);
            owner = __instance;
        }

        if (showingRoot && CustomRolesFolder == null)
        {
            injectedRoot = taskFolder;
            roleFolderMode = FreeplayInitialization.Mode;
            CustomRolesFolder = CreateFolder(__instance, Main.ModName);
            CustomRolesFolder.name = RoleRootName;
            // Keep the mod entry on the first native row even when the ship exposes many task folders.
            taskFolder.SubFolders.Insert(0, CustomRolesFolder);
            modesFolder = CreateFolder(__instance, GetString("GameMode"));
            modesFolder.name = ModeFolderName;
            taskFolder.SubFolders.Insert(1, modesFolder);

            if (roleFolderMode != CustomGameMode.Standard) return;

            var roles = CreateFolder(__instance, GetString("Roles"));
            var addons = CreateFolder(__instance, GetString("Addons"));
            CustomRolesFolder.SubFolders.Add(roles);
            CustomRolesFolder.SubFolders.Add(addons);

            var available = CustomRolesHelper.AllRoles.Where(role =>
                role is not (CustomRoles.NotAssigned or CustomRoles.ShapeshifterTOHE or CustomRoles.GM) && !role.IsVanilla() && IsRoleAvailableInMode(role) &&
                (!Options.CustomRoleSpawnChances.TryGetValue(role, out var option) || !option.IsHidden) &&
                (role.IsAdditionRole() || role.GetStaticRoleClass() is not DefaultSetup));
            var comparer = GameOptionsMenuPatch.GetRoleNameComparer();
            void AddGroup(TaskFolder parent, string key, IEnumerable<CustomRoles> members)
            {
                var sorted = members.OrderBy(role => Options.CustomRoleSpawnChances.TryGetValue(role, out var option)
                    ? option.GetName(disableColor: true).RemoveHtmlTags()
                    : Utils.GetRoleName(role).RemoveHtmlTags(), comparer).ToArray();
                var folder = CreateFolder(__instance, GetCategoryLabel(key));
                folder.name = $"TOHEEFreeplayRoleFolder{RoleFolders.Count}";
                RoleFolders[folder.name] = sorted;
                parent.SubFolders.Add(folder);
            }

            foreach (var team in available.Where(role => !role.IsAdditionRole())
                .GroupBy(role => role.IsMadmate() ? Custom_Team.Impostor : role.GetCustomRoleTeam()).OrderBy(group => group.Key))
            {
                var faction = CreateFolder(__instance, GetString($"Team{team.Key}"));
                roles.SubFolders.Add(faction);
                foreach (var category in team.GroupBy(GetRoleCategory))
                    AddGroup(faction, category.Key, category);
            }

            foreach (var type in available.Where(role => role.IsAdditionRole())
                .GroupBy(role => CustomRoleManager.AddonClasses.TryGetValue(role, out var addon) ? addon.Type : AddonTypes.Misc)
                .OrderBy(group => group.Key))
                AddGroup(addons, $"RoleType.{type.Key}", type);
        }
    }

    private static string GetRoleCategory(CustomRoles role)
    {
        var definition = role.GetStaticRoleClass();
        if (definition.IsExperimental) return "Experimental.Roles";

        return definition.ThisRoleType switch
        {
            Custom_RoleType.ImpostorVanilla or Custom_RoleType.CrewmateVanilla or Custom_RoleType.CrewmateVanillaGhosts => "RoleType.VanillaRoles",
            Custom_RoleType.ImpostorKilling => "RoleType.ImpKilling",
            Custom_RoleType.ImpostorSupport => "RoleType.ImpSupport",
            Custom_RoleType.ImpostorConcealing => "RoleType.ImpConcealing",
            Custom_RoleType.ImpostorHindering => "RoleType.ImpHindering",
            Custom_RoleType.ImpostorGhosts => "RoleType.ImpGhost",
            Custom_RoleType.Madmate => "RoleType.Madmate",
            Custom_RoleType.CrewmateBasic => "RoleType.CrewBasic",
            Custom_RoleType.CrewmateSupport => "RoleType.CrewSupport",
            Custom_RoleType.CrewmateKilling => "RoleType.CrewKilling",
            Custom_RoleType.CrewmatePower => "RoleType.CrewPower",
            Custom_RoleType.CrewmateGhosts => "RoleType.CrewGhost",
            Custom_RoleType.NeutralBenign => "RoleType.NeutralBenign",
            Custom_RoleType.NeutralEvil => "RoleType.NeutralEvil",
            Custom_RoleType.NeutralChaos => "RoleType.NeutralChaos",
            Custom_RoleType.NeutralKilling => "RoleType.NeutralKilling",
            Custom_RoleType.NeutralApocalypse => "RoleType.NeutralApocalypse",
            _ => "RoleType.OtherRoles",
        };
    }

    internal static bool IsRoleAvailableInMode(CustomRoles role) => FreeplayInitialization.Mode switch
    {
        CustomGameMode.CandR => role is CustomRoles.GM or CustomRoles.Cop or CustomRoles.Robber,
        CustomGameMode.Speedrun => role is CustomRoles.GM or CustomRoles.Runner,
        CustomGameMode.FFA => role is CustomRoles.GM or CustomRoles.Killer,
        _ => role is not (CustomRoles.Cop or CustomRoles.Robber or CustomRoles.Runner or CustomRoles.Killer),
    };

    private static string GetCategoryLabel(string key)
    {
        var label = GetString(key);
        // Settings headings can include help commands or a caution in smaller text.
        var note = label.IndexOf("<size=", StringComparison.Ordinal);
        if (note >= 0) label = label[..note];
        var command = label.IndexOf(" /", StringComparison.Ordinal);
        if (command < 0) command = label.IndexOf("/ghostinfo", StringComparison.Ordinal);
        if (command < 0) command = label.IndexOf("/apocalypseinfo", StringComparison.Ordinal);
        if (command >= 0) label = label[..command];
        return label.RemoveHtmlTags().Trim('★', ' ');
    }

    private static TaskFolder CreateFolder(TaskAdderGame instance, string name)
    {
        var folder = Object.Instantiate(instance.RootFolderPrefab, instance.transform);
        folder.gameObject.SetActive(false);
        folder.FolderName = name;
        folder.SubFolders = new();
        folder.TaskChildren = new();
        folder.RoleChildren = new();
        injectedFolders.Add(folder);
        return folder;
    }

    private static void RestoreTaskScale()
    {
        if (scaledTaskParent) scaledTaskParent.localScale = originalTaskScale;
        scaledTaskParent = null;
    }

    private static void FitRoleFiles(TaskAdderGame instance)
    {
        var parent = instance.TaskParent;
        var background = instance.transform.Find("Background");
        var frame = background ? background.GetComponent<SpriteRenderer>() : null;
        if (!parent || !frame) return;

        float availableDepth = -parent.InverseTransformPoint(frame.bounds.min).y - 0.15f;
        float contentDepth = 0f;
        foreach (var button in CustomButtons.Keys)
        {
            float iconDepth = -parent.InverseTransformPoint(button.FileImage.bounds.min).y;
            float textDepth = -parent.InverseTransformPoint(button.Text.renderer.bounds.min).y;
            contentDepth = Mathf.Max(contentDepth, Mathf.Max(iconDepth, textDepth));
        }
        if (availableDepth <= 0f || contentDepth <= availableDepth) return;

        scaledTaskParent = parent;
        originalTaskScale = parent.localScale;
        float fit = availableDepth / contentDepth;
        parent.localScale = new Vector3(originalTaskScale.x * fit, originalTaskScale.y * fit, originalTaskScale.z);
    }

    internal static void Postfix(TaskAdderGame __instance, [HarmonyArgument(0)] TaskFolder taskFolder)
    {
        if (!GameStates.IsFreePlay || !FreeplayInitialization.IsReady) return;

        Logger.Info("Opened " + taskFolder.FolderName, "TaskFolder");
        float xCursor = 0f;
        float yCursor = 0f;
        float maxHeight = 0f;
        TaskAddButton AddButton(string label, Color color)
        {
            TaskAddButton button = Object.Instantiate(__instance.RoleButton);
            button.Text.text = label;
            button.Text.enableWordWrapping = false;
            button.Text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            button.Text.ForceMeshUpdate();
            button.SafePositionWorld = __instance.SafePositionWorld;
            __instance.AddFileAsChild(taskFolder, button, ref xCursor, ref yCursor, ref maxHeight);
            button.Overlay.sprite = button.CheckImage;
            button.FileImage.color = color;
            button.RolloverHandler.OutColor = color;
            button.RolloverHandler.OverColor = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f);
            if (button.Button != null) ControllerManager.Instance.AddSelectableUiElement(button.Button);
            return button;
        }

        // Native folder buttons open Unity clones of the folder templates.
        var folderKey = taskFolder.name;
        int cloneSuffix = folderKey.IndexOf("(Clone)", StringComparison.Ordinal);
        if (cloneSuffix >= 0) folderKey = folderKey[..cloneSuffix];
        if (owner == __instance && folderKey == ModeFolderName)
        {
            foreach (var (mode, name) in FreeplayModes) ModeButtons[AddButton(GetString(name), Color.cyan)] = mode;
        }
        else if (owner == __instance && folderKey == RoleRootName)
        {
            // Standard displays the two category folders before the Game Master file.
            xCursor = taskFolder.SubFolders.Count * __instance.folderWidth;
            CustomRoles[] choices = FreeplayInitialization.Mode switch
            {
                CustomGameMode.CandR => [CustomRoles.Cop, CustomRoles.Robber, CustomRoles.GM],
                CustomGameMode.Speedrun => [CustomRoles.Runner, CustomRoles.GM],
                CustomGameMode.FFA => [CustomRoles.Killer, CustomRoles.GM],
                _ => [CustomRoles.GM],
            };
            foreach (var role in choices) CustomButtons[AddButton(Utils.GetRoleName(role), Utils.GetRoleColor(role))] = role;
        }
        else if (owner == __instance && RoleFolders.TryGetValue(folderKey, out var roles))
        {
            foreach (var role in roles.Where(IsRoleAvailableInMode))
                CustomButtons[AddButton(Utils.GetRoleName(role), Utils.GetRoleColor(role))] = role;
            FitRoleFiles(__instance);
        }

        if (FreeplayInitialization.Mode != CustomGameMode.Standard && taskFolder == __instance.Root)
        {
            foreach (var item in __instance.ActiveItems)
            {
                if (!item) continue;
                var folder = item.GetComponent<TaskFolder>();
                if (!folder || folder.RoleChildren.Count == 0 || folder.TaskChildren.Count != 0 || folder.SubFolders.Count != 0) continue;
                foreach (var element in item.GetComponentsInChildren<UiElement>(true))
                    ControllerManager.Instance.RemoveSelectableUiElement(element);
                item.gameObject.SetActive(false);
            }
        }

        if (ControllerManager.Instance.CurrentUiState?.MenuName == __instance.name &&
            ControllerManager.Instance.CurrentUiState.CurrentSelection == null && __instance.ActiveItems.Count > 0)
        {
            var first = __instance.ActiveItems.ToArray().FirstOrDefault(item => item && item.gameObject.activeInHierarchy)?.GetComponent<UiElement>();
            if (first != null) ControllerManager.Instance.SetCurrentSelected(first);
        }
    }
}

[HarmonyPatch(typeof(TaskAdderGame), nameof(TaskAdderGame.OnDisable))]
public static class TaskAdderClosePatch
{
    public static void Postfix(TaskAdderGame __instance) => ShowFolderPatch.Release(__instance);
}

[HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.Update))]
class TaskAddButtonUpdatePatch
{
    public static bool Prefix(TaskAddButton __instance)
    {
        if (!GameStates.IsFreePlay) return true;

        if (ShowFolderPatch.ModeButtons.TryGetValue(__instance, out var mode))
        {
            __instance.Overlay.enabled = FreeplayInitialization.Mode == mode;
            __instance.Overlay.sprite = __instance.CheckImage;
            return false;
        }

        if (ShowFolderPatch.CustomButtons.TryGetValue(__instance, out var customRole))
        {
            var player = PlayerControl.LocalPlayer;
            __instance.Overlay.enabled = player != null && Main.PlayerStates.TryGetValue(player.PlayerId, out var state)
                && (customRole < CustomRoles.NotAssigned ? state.MainRole == customRole : state.SubRoles.Contains(customRole));
            __instance.Overlay.sprite = __instance.CheckImage;
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.Start))]
class TaskAddButtonStartPatch
{
    public static bool Prefix(TaskAddButton __instance) => TaskAddButtonUpdatePatch.Prefix(__instance);
}
[HarmonyPatch(typeof(TaskAddButton), nameof(TaskAddButton.AddTask))]
class AddTaskButtonPatch
{
    internal static bool TryMapNativeRole(RoleTypes nativeRole, out CustomRoles role)
    {
        role = nativeRole switch
        {
            RoleTypes.Crewmate or RoleTypes.CrewmateGhost => CustomRoles.Crewmate,
            RoleTypes.Impostor or RoleTypes.ImpostorGhost => CustomRoles.Impostor,
            RoleTypes.Engineer => CustomRoles.Engineer,
            RoleTypes.Scientist => CustomRoles.Scientist,
            RoleTypes.GuardianAngel => CustomRoles.GuardianAngel,
            RoleTypes.Shapeshifter => CustomRoles.Shapeshifter,
            RoleTypes.Noisemaker => CustomRoles.Noisemaker,
            RoleTypes.Phantom => CustomRoles.Phantom,
            RoleTypes.Tracker => CustomRoles.Tracker,
            RoleTypes.Detective => CustomRoles.DetectiveTOHE,
            RoleTypes.Judge => CustomRoles.JudgeTOHE,
            RoleTypes.Viper => CustomRoles.ViperTOHE,
            _ => CustomRoles.NotAssigned
        };
        return role != CustomRoles.NotAssigned;
    }

    public static bool Prefix(TaskAddButton __instance, out bool __state)
    {
        __state = false;
        if (!GameStates.IsFreePlay) return true;

        if (ShowFolderPatch.ModeButtons.TryGetValue(__instance, out var mode))
        {
            FreeplayInitialization.ChangeMode(mode);
            return false;
        }

        if (ShowFolderPatch.CustomButtons.TryGetValue(__instance, out var customRole))
        {
            var player = PlayerControl.LocalPlayer;
            if (!FreeplayInitialization.IsReady || !ShowFolderPatch.IsRoleAvailableInMode(customRole) || player == null
                || !Main.PlayerStates.TryGetValue(player.PlayerId, out var state)) return false;
            if (customRole == CustomRoles.NotAssigned) return false;
            if (FreeplayInitialization.Mode != CustomGameMode.Standard)
            {
                FreeplayInitialization.ChangeMode(FreeplayInitialization.Mode, customRole);
                return false;
            }
            if (customRole > CustomRoles.NotAssigned)
            {
                if (state.SubRoles.Contains(customRole)) state.RemoveSubRole(customRole);
                else
                {
                    state.SetSubRole(customRole, player);
                    if (customRole.IsGhostRole())
                    {
                        FreeplayInitialization.ExileForRole(player, state);
                        player.RpcSetRole(RoleTypes.GuardianAngel, true);
                    }
                }
                FreeplayInitialization.SyncTasks(player);
                return false;
            }
            // Keep the native role button's task recovery and safe position behavior.
            if (!FreeplayInitialization.UsesGhostState(customRole) && PlayerTask.DestroyTasksOfType<ImportantTextTask>(player)
                && !PlayerTask.PlayerHasTaskOfType<NormalPlayerTask>(player)) ShipStatus.Instance.Begin();
            FreeplayInitialization.ChangeRole(player, customRole, setNativeRole: true);
            player.transform.position = __instance.SafePositionWorld;
            return false;
        }
        if (__instance.Role != null)
        {
            if (!FreeplayInitialization.IsReady || FreeplayInitialization.Mode != CustomGameMode.Standard
                || !TryMapNativeRole(__instance.Role.Role, out var mappedRole)) return false;
            if (FreeplayInitialization.UsesGhostState(mappedRole))
            {
                FreeplayInitialization.ChangeRole(PlayerControl.LocalPlayer, mappedRole, setNativeRole: true);
                return false;
            }
            if (mappedRole == CustomRoles.JudgeTOHE) FreeplayInitialization.PrepareJudgeOptions();
            __state = true;
        }
        return true;
    }

    public static void Postfix(TaskAddButton __instance, bool __state, bool __runOriginal)
    {
        if (!__runOriginal || !FreeplayInitialization.IsReady) return;
        if (__instance.Role == null)
        {
            FreeplayInitialization.SyncTasks(PlayerControl.LocalPlayer);
            return;
        }
        if (!__state) return;
        if (!TryMapNativeRole(__instance.Role.Role, out var role)) return;
        FreeplayInitialization.ChangeRole(PlayerControl.LocalPlayer, role, setNativeRole: false);
    }
}
