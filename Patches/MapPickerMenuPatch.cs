using UnityEngine;

namespace TOHE.Patches;

// Thanks: https://github.com/SubmergedAmongUs/Submerged/blob/4a5a6b47cbed526670ae4b7eae76acd7c42e35de/Submerged/UI/Patches/MapSelectButtonPatches.cs#L49
class CreateOptionsPickerPatch
{
    public static bool SetDleks = false;
    [HarmonyPatch(typeof(GameOptionsMapPicker))]
    public static class GameOptionsMapPickerPatch
    {
        [HarmonyPatch(nameof(GameOptionsMapPicker.SetupMapButtons))]
        [HarmonyPrefix, HarmonyAfter(CustomMapCompatibility.SubmergedGuid)]
        public static void AddDleksIcon(GameOptionsMapPicker __instance)
        {
            if (!IsLobbyMapPicker(__instance)) return;
            MapIconByName[] icons = __instance.AllMapIcons.ToArray();
            var skeld = icons.FirstOrDefault(icon => icon.Name == MapNames.Skeld);
            if (skeld == null) return;

            if (!icons.Any(icon => icon.Name == MapNames.Dleks))
            {
                var airshipIndex = System.Array.FindIndex(icons, icon => icon.Name == MapNames.Airship);
                __instance.AllMapIcons.Insert(airshipIndex >= 0 ? airshipIndex : icons.Length, new MapIconByName
                {
                    Name = MapNames.Dleks,
                    MapIcon = skeld.MapIcon,
                    MapImage = Utils.LoadSprite("TOHE.Resources.Images.DleksBanner.png", 100f),
                    NameImage = Utils.LoadSprite("TOHE.Resources.Images.DleksBanner-Wordart.png", 100f)
                });
            }

            if (SetDleks && __instance.selectedMapId == (int)MapNames.Skeld)
                __instance.selectedMapId = (int)MapNames.Dleks;
        }

        [HarmonyPatch(nameof(GameOptionsMapPicker.SetupMapButtons))]
        [HarmonyPostfix]
        public static void BindDleksButton(GameOptionsMapPicker __instance)
        {
            if (!IsLobbyMapPicker(__instance)) return;
            var dleks = __instance.MapButtons.ToArray().FirstOrDefault(button => button.MapID == (int)MapNames.Dleks);
            var skeld = __instance.AllMapIcons.ToArray().FirstOrDefault(icon => icon.Name == MapNames.Skeld);
            if (!dleks || skeld == null) return;

            foreach (var icon in dleks.MapIcon)
                icon.transform.localScale = new Vector3(-1f, 1f, 1f);

            dleks.Button.OnClick.RemoveAllListeners();
            dleks.Button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                // The lobby still advertises Skeld to vanilla clients until game start.
                __instance.SelectMap(skeld);
                if (GameStates.IsNormalGame) Main.NormalOptions.MapId = (byte)MapNames.Skeld;
                else if (GameStates.IsHideNSeek) Main.HideNSeekOptions.MapId = (byte)MapNames.Skeld;
                if (__instance.selectedButton) __instance.selectedButton.Button.SelectButton(false);
                __instance.selectedButton = dleks;
                dleks.Button.SelectButton(true);
                __instance.SelectMap((int)MapNames.Dleks);
                SetDleks = true;
            }));
        }

        [HarmonyPatch(nameof(GameOptionsMapPicker.SelectMap), typeof(MapIconByName))]
        [HarmonyPostfix]
        public static void TrackMapSelection(GameOptionsMapPicker __instance, [HarmonyArgument(0)] MapIconByName icon)
        {
            if (IsLobbyMapPicker(__instance)) SetDleks = icon.Name == MapNames.Dleks;
        }

        private static bool IsLobbyMapPicker(GameOptionsMapPicker picker) =>
            picker.TryCast<CreateGameMapPicker>() == null && picker.TryCast<FilterMapPicker>() == null;
    }

    [HarmonyPatch(typeof(CreateOptionsPicker), nameof(CreateOptionsPicker.Awake))]
    [HarmonyAfter(CustomMapCompatibility.SubmergedGuid, CustomMapCompatibility.LevelImposterGuid)]
    class MenuMapPickerPatch
    {
        public static void Postfix(CreateOptionsPicker __instance)
        {
            var mapButtons = __instance.MapMenu?.MapButtons;
            // This legacy layout only fits the original five buttons and dlekS.
            if (mapButtons?.Length != 5 || mapButtons[0].MapId != MapNames.Skeld || mapButtons[1].MapId != MapNames.MiraHQ
                || mapButtons[2].MapId != MapNames.Polus || mapButtons[3].MapId != MapNames.Airship || mapButtons[4].MapId != MapNames.Fungle) return;

            Transform mapPickerTransform = __instance.transform.Find("MapPicker");
            MapPickerMenu mapPickerMenu = mapPickerTransform.Find("Map Picker Menu").GetComponent<MapPickerMenu>();

            MapFilterButton airhipIconInMenu = mapButtons[3];
            MapFilterButton fungleIconInMenu = mapButtons[4];
            MapFilterButton skeldIconInMenu = mapButtons[0];
            MapFilterButton dleksIconInMenuCopy = UnityEngine.Object.Instantiate(airhipIconInMenu, airhipIconInMenu.transform.parent);

            Transform skeldMenuButton = mapPickerMenu.transform.Find("Skeld");
            Transform polusMenuButton = mapPickerMenu.transform.Find("Polus");
            Transform airshipMenuButton = mapPickerMenu.transform.Find("Airship");
            Transform fungleMenuButton = mapPickerMenu.transform.Find("Fungle");
            Transform dleksMenuButtonCopy = UnityEngine.Object.Instantiate(airshipMenuButton, airshipMenuButton.parent);

            // Set mapid for Dleks button
            PassiveButton dleksButton = dleksMenuButtonCopy.GetComponent<PassiveButton>();
            dleksButton.OnClick.m_PersistentCalls.m_Calls._items[0].arguments.intArgument = (int)MapNames.Dleks;

            SpriteRenderer dleksImage = dleksMenuButtonCopy.Find("Image").GetComponent<SpriteRenderer>();
            dleksImage.sprite = skeldMenuButton.Find("Image").GetComponent<SpriteRenderer>().sprite;

            dleksIconInMenuCopy.name = "Dleks";
            dleksIconInMenuCopy.transform.localPosition = new Vector3(0.8f, airhipIconInMenu.transform.localPosition.y, airhipIconInMenu.transform.localPosition.z);
            dleksIconInMenuCopy.MapId = MapNames.Dleks;
            dleksIconInMenuCopy.Button = dleksButton;
            dleksIconInMenuCopy.ButtonCheck = dleksMenuButtonCopy.Find("selectedCheck").GetComponent<SpriteRenderer>();
            dleksIconInMenuCopy.ButtonImage = dleksImage;
            dleksIconInMenuCopy.ButtonOutline = dleksImage.transform.parent.GetComponent<SpriteRenderer>();
            dleksIconInMenuCopy.Icon.sprite = skeldIconInMenu.Icon.sprite;

            dleksMenuButtonCopy.name = "Dleks";
            dleksMenuButtonCopy.position = new Vector3(dleksMenuButtonCopy.position.x, 2f * dleksMenuButtonCopy.position.y - polusMenuButton.transform.position.y, dleksMenuButtonCopy.position.z);
            fungleMenuButton.position = new Vector3(fungleMenuButton.position.x, dleksMenuButtonCopy.transform.position.y - 0.6f, fungleMenuButton.position.z);

            __instance.MapMenu.MapButtons = HarmonyLib.CollectionExtensions.AddItem(__instance.MapMenu.MapButtons, dleksIconInMenuCopy).ToArray();

            float xPos = -1f;
            for (int index = 0; index < 6; ++index)
            {
                __instance.MapMenu.MapButtons[index].transform.SetLocalX(xPos);
                xPos += 0.34f;
            }

            if (__instance.mode == SettingsMode.Host)
            {
                mapPickerMenu.transform.localPosition = new Vector3(mapPickerMenu.transform.localPosition.x, 0.85f, mapPickerMenu.transform.localPosition.z);

                mapPickerTransform.localScale = new Vector3(0.86f, 0.85f, 1f);
                mapPickerTransform.transform.localPosition = new Vector3(mapPickerTransform.transform.localPosition.x + 0.05f, mapPickerTransform.transform.localPosition.y + 0.03f, mapPickerTransform.transform.localPosition.z);
            }

            SwapIconOrButtomsPositions(airhipIconInMenu, dleksIconInMenuCopy);
            SwapIconOrButtomsPositions(fungleIconInMenu, airhipIconInMenu);

            SwapIconOrButtomsPositions(airshipMenuButton, dleksMenuButtonCopy);

            // set flipped dleks map Icon/button
            __instance.MapMenu.MapButtons[5].SetFlipped(true);

            mapPickerMenu.transform.Find("Backdrop").localScale *= 5;
        }
        private static void SwapIconOrButtomsPositions(Component one, Component two)
        {
            Transform transform1 = one.transform;
            Transform transform2 = two.transform;
            Vector3 position1 = two.transform.position;
            Vector3 position2 = one.transform.position;
            transform1.position = position1;
            Vector3 vector3 = position2;
            transform2.position = vector3;
        }
    }
}
