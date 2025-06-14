
using BepInEx;
using BepInEx.Configuration;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System;
using System.Reflection;

namespace MorePlayers
{
    [BepInPlugin("org.gamedreamst.plugins.morelocalplayers", "More Local Players", "0.0.3")]
    public class Plugin : BaseUnityPlugin
    {
        void Awake()
        {
            ConfigEntry<int> playerCountEntry = Config.Bind("General", "PlayerCount", 6, "The desired player count");

            GameObject MorePlayersObj = new("MorePlayersPluginObj");
            DontDestroyOnLoad(MorePlayersObj);
            MorePlayersObj.hideFlags |= (HideFlags)61;
            var plugin = MorePlayersObj.AddComponent<MorePlayersPlugin>();
            plugin.amountOfPlayers = playerCountEntry.Value;
            
            Debug.Log($"Plugin 'More Local Players' is loaded!");
        }
    }

    public class MorePlayersPlugin : MonoBehaviour
    {
        public int amountOfPlayers = 4;
        readonly string characterSelectSceneString = "CharacterSelect";

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void CopyAnimateOrigin(GameObject fromObject, GameObject toObject)
        {
            var fromList = fromObject.GetComponentsInChildren<AnimateInOutUI>();
            var toList = toObject.GetComponentsInChildren<AnimateInOutUI>();

            var classType = typeof(AnimateInOutUI);
            var originalHeightField = classType.GetField("originalHeights", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < fromList.Length; i++)
            {
                var from = fromList[i];
                var to = toList[i];
                originalHeightField.SetValue(to, originalHeightField.GetValue(from));
            }
        }

        //https://forum.openframeworks.cc/t/hsv-color-setting/770
        // H [0, 360] S and V [0.0, 1.0].
        Color HSVToColor(float h, float s, float v)
        {
            int i = (int)Mathf.Floor(h / 60.0f) % 6;
            float f = h / 60.0f - Mathf.Floor(h / 60.0f);
            float p = v * (float)(1 - s);
            float q = v * (float)(1 - s * f);
            float t = v * (float)(1 - (1 - f) * s);
            return i switch
            {
                1 => new(q, v, p),
                2 => new(p, v, t),
                3 => new(p, q, v),
                4 => new(t, p, v),
                5 => new(v, p, q),
                _ => new(v, t, p),
            };
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"Scene {scene.name} loaded with mode {mode}\nWe have {Gamepad.all.Count} gamepads available");

            if(scene.name.ToLower().StartsWith("level"))
            {
                OnGameplayLevelLoad();
                return;
            }

            if (scene.name != characterSelectSceneString || amountOfPlayers <= 4)
                return;

            var selectHandler = FindObjectOfType<CharacterSelectHandler>();
            var handlerType = typeof(CharacterSelectHandler);
            var animateOutDelaysField = handlerType.GetField("animateOutDelays", BindingFlags.Instance | BindingFlags.NonPublic);
            var delays = (float[])animateOutDelaysField.GetValue(selectHandler);
            Array.Resize(ref delays, amountOfPlayers);
            animateOutDelaysField.SetValue(selectHandler, delays);

            var selectionBoxPrefab = selectHandler.characterSelectBoxes[0].gameObject;
            int currentBoxCount = selectHandler.characterSelectBoxes.Length;
            Array.Resize(ref selectHandler.characterSelectBoxes, amountOfPlayers);

            var teamSelectorPrefab = selectionBoxPrefab.GetComponentInChildren<TeamSelector>();
            var teamColors = teamSelectorPrefab.teams.teamColors;
            Array.Resize(ref teamColors, amountOfPlayers);
            float HSegment = 360f / amountOfPlayers;
            for (int i = 0; i < amountOfPlayers; i++)
            {
                float H = HSegment * i;
                var colorFill = HSVToColor(H, 0.6f, 0.84f);
                var colorSaturated = HSVToColor(H, 1f, 1f);
                var colorOutline = colorFill * 0.5f;
                colorOutline.a = 1;

                teamColors[i].team = i;
                teamColors[i].fill = colorFill;
                teamColors[i].saturated = colorSaturated;
                teamColors[i].border = colorOutline;
            }
            teamSelectorPrefab.teams.teamColors = teamColors;

            Debug.Log($"Creating {amountOfPlayers - currentBoxCount} additional players");

            CharacterSelectBox.deviceIds = new int[amountOfPlayers + 1]; // Controllers + Keyboard
            CharacterSelectBox.occupiedRectangles = new bool[amountOfPlayers + 1];
            for (int i = currentBoxCount; i < amountOfPlayers; i++)
            {
                var selectorBox = Instantiate(selectionBoxPrefab).GetComponent<CharacterSelectBox>();
                selectorBox.transform.SetParent(selectHandler.transform, true);
                selectorBox.transform.localScale = selectionBoxPrefab.transform.localScale;
                selectorBox.RectangleIndex = i;
                selectHandler.characterSelectBoxes[i] = selectorBox;
                CopyAnimateOrigin(selectionBoxPrefab.gameObject, selectorBox.gameObject);
            }

            var handlerRect = selectHandler.GetComponent<RectTransform>();
            handlerRect.anchorMin = new(0, 0.5f);
            handlerRect.anchorMax = new(1, 0.5f);
            handlerRect.offsetMin = handlerRect.offsetMax = Vector2.zero;

            selectHandler.gameObject.AddComponent<HorizontalLayoutGroup>();
        }

        void OnGameplayLevelLoad()
        {
            if (amountOfPlayers <= 4)
                return;

            var selectHandler = FindObjectOfType<GameSessionHandler>();
            Array.Resize(ref selectHandler.teamSpawns, amountOfPlayers);

            Debug.Log($"Modifying team spawns");

            for (int i = 4; i < amountOfPlayers; i++)
            {
                selectHandler.teamSpawns[i] = selectHandler.teamSpawns[i % 4];
            }
        }
    }
}