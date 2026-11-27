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
    public const int MaxPageItems = 6;
    private const string ModeFolderName = "TOHEEFreeplayModes";
    private static TaskFolder CustomRolesFolder;
    private static TaskFolder modesFolder;
    private static TaskFolder injectedRoot;
    private static TaskAdderGame owner;
    private static readonly List<TaskFolder> injectedFolders = [];
    private static readonly Dictionary<string, CustomRoles[]> Pages = [];
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
        Pages.Clear();
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
        CustomButtons.Clear();
        ModeButtons.Clear();
        if (owner != __instance || injectedRoot && injectedRoot != __instance.Root)
        {
            Release(owner);
            owner = __instance;
        }

        if (__instance.Root == taskFolder && CustomRolesFolder == null)
        {
            injectedRoot = taskFolder;
            CustomRolesFolder = CreateFolder(__instance, Main.ModName);
            // Keep the mod entry on the first native row even when the ship exposes many task folders.
            taskFolder.SubFolders.Insert(0, CustomRolesFolder);
            modesFolder = CreateFolder(__instance, GetString("GameMode"));
            modesFolder.name = ModeFolderName;
            taskFolder.SubFolders.Insert(1, modesFolder);

            var roles = CreateFolder(__instance, GetString("Roles"));
            var addons = CreateFolder(__instance, GetString("Addons"));
            CustomRolesFolder.SubFolders.Add(roles);
            CustomRolesFolder.SubFolders.Add(addons);

            var available = CustomRolesHelper.AllRoles.Where(role => role != CustomRoles.NotAssigned);
            var comparer = GameOptionsMenuPatch.GetRoleNameComparer();
            void AddGroup(TaskFolder parent, string label, IEnumerable<CustomRoles> members)
            {
                var sorted = members.OrderBy(role => Options.CustomRoleSpawnChances.TryGetValue(role, out var option)
                    ? option.GetName(disableColor: true).RemoveHtmlTags()
                    : Utils.GetRoleName(role).RemoveHtmlTags(), comparer).ToArray();
                var folder = CreateFolder(__instance, label);
                parent.SubFolders.Add(folder);
                AddPages(__instance, folder, sorted, 0, sorted.Length);
            }

            foreach (var team in available.Where(role => !role.IsAdditionRole()).GroupBy(role => role.GetCustomRoleTeam()).OrderBy(group => group.Key))
                AddGroup(roles, GetString($"Team{team.Key}"), team);

            foreach (var type in available.Where(role => role.IsAdditionRole())
                .GroupBy(role => CustomRoleManager.AddonClasses.TryGetValue(role, out var addon) ? addon.Type : AddonTypes.Misc)
                .OrderBy(group => group.Key))
            {
                var label = GetString($"RoleType.{type.Key}").RemoveHtmlTags();
                var qualifier = label.IndexOf('(');
                if (qualifier >= 0) label = label[..qualifier];
                AddGroup(addons, label.Trim('★', ' '), type);
            }
        }
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

    private static void AddPages(TaskAdderGame instance, TaskFolder folder, CustomRoles[] roles, int start, int count)
    {
        if (count <= MaxPageItems)
        {
            folder.name = $"TOHEEFreeplayRolePage{Pages.Count}";
            Pages[folder.name] = roles[start..(start + count)];
            return;
        }

        // A bounded native folder tree: directory pages and role pages both contain at most six items.
        int groupSize = MaxPageItems;
        while ((long)groupSize * MaxPageItems < count) groupSize *= MaxPageItems;
        for (int offset = 0; offset < count; offset += groupSize)
        {
            int childCount = Math.Min(groupSize, count - offset);
            var firstRole = Utils.GetRoleName(roles[start + offset]).RemoveHtmlTags();
            var child = CreateFolder(instance, $"{firstRole} …");
            AddPages(instance, child, roles, start + offset, childCount);
            folder.SubFolders.Add(child);
        }
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
        else if (owner == __instance && Pages.TryGetValue(folderKey, out var roles))
        {
            foreach (var role in roles) CustomButtons[AddButton(Utils.GetRoleName(role), Utils.GetRoleColor(role))] = role;
        }

        if (ControllerManager.Instance.CurrentUiState?.MenuName == __instance.name &&
            ControllerManager.Instance.CurrentUiState.CurrentSelection == null && __instance.ActiveItems.Count > 0)
        {
            var first = __instance.ActiveItems[0].GetComponent<UiElement>();
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
            if (!FreeplayInitialization.IsReady || player == null
                || !Main.PlayerStates.TryGetValue(player.PlayerId, out var state)) return false;
            if (customRole == CustomRoles.NotAssigned) return false;
            if (customRole > CustomRoles.NotAssigned)
            {
                if (state.SubRoles.Contains(customRole)) state.RemoveSubRole(customRole);
                else state.SetSubRole(customRole, player);
                FreeplayInitialization.SyncTasks(player);
                return false;
            }
            // Keep the native role button's task recovery and safe position behavior.
            if (PlayerTask.DestroyTasksOfType<ImportantTextTask>(player)
                && !PlayerTask.PlayerHasTaskOfType<NormalPlayerTask>(player)) ShipStatus.Instance.Begin();
            FreeplayInitialization.ChangeRole(player, customRole, setNativeRole: true);
            player.transform.position = __instance.SafePositionWorld;
            return false;
        }
        if (__instance.Role != null)
        {
            if (!FreeplayInitialization.IsReady || !TryMapNativeRole(__instance.Role.Role, out var mappedRole)) return false;
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
