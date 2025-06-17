
using BepInEx;
using BepInEx.Configuration;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System;
using System.Text;
using System.Reflection;
using BoplFixedMath;
using System.Collections.Generic;

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

        static bool audioPatched;

        BindingFlags privateFieldBindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void PatchAudio()
        {
            if (audioPatched)
                return;
            audioPatched = true;

            var audio = AudioManager.Get();
            StringBuilder sb = new();
            List<Sound> soundsToAdd = new();
            foreach (var sound in audio.sounds)
            {
                string name = sound.name;

                //sb.AppendLine($"Processing sound {sound.name} with clip {sound.clip.name}");
                string lastLetter = name.Substring(name.Length - 1, 1);
                if (!int.TryParse(lastLetter, out var number) || number != 1)
                    continue;

                void AddSound(string soundTag)
                {
                    for (int i = 4; i < amountOfPlayers; i++)
                    {
                        var newSound = sound;
                        newSound.name = $"{soundTag}{i}";
                        newSound.source = audio.gameObject.AddComponent<AudioSource>();
                        soundsToAdd.Add(newSound);
                        sb.AppendLine($"Added new sound {newSound.name}");
                    }
                }

                if (name.StartsWith("slimeWalk"))
                    AddSound("slimeWalk");
                else if (name.StartsWith("slimeLand"))
                    AddSound("slimeLand");
                else if (name.StartsWith("slimeJump"))
                    AddSound("slimeJump");
            }

            int startIndex = audio.sounds.Length;
            int j = 0;
            Array.Resize(ref audio.sounds, audio.sounds.Length + soundsToAdd.Count);
            for (int i = startIndex; i < audio.sounds.Length; i++)
            {
                audio.sounds[i] = soundsToAdd[j];
                j++;
            }

            var audioType = typeof(AudioManager);
            var timesSoundPlayedField = audioType.GetField("timesSoundPlayed", privateFieldBindingFlags);
            var isSoundLoopingField = audioType.GetField("isSoundLooping", privateFieldBindingFlags);

            var timesSoundPlayedVariable = (int[])timesSoundPlayedField.GetValue(audio);
            var isSoundLoopingVariable = (bool[])isSoundLoopingField.GetValue(audio);

            Array.Resize(ref timesSoundPlayedVariable, audio.sounds.Length);
            Array.Resize(ref isSoundLoopingVariable, audio.sounds.Length);

            timesSoundPlayedField.SetValue(audio, timesSoundPlayedVariable);
            isSoundLoopingField.SetValue(audio, isSoundLoopingVariable);

            audio.ReInitializeSfx();

            sb.AppendLine($"Started with {startIndex} sounds, now we have {audio.sounds.Length}");
            Debug.Log(sb.ToString());
        }

        void CopyAnimateOrigin(GameObject fromObject, GameObject toObject)
        {
            var fromList = fromObject.GetComponentsInChildren<AnimateInOutUI>();
            var toList = toObject.GetComponentsInChildren<AnimateInOutUI>();

            var classType = typeof(AnimateInOutUI);
            var originalHeightField = classType.GetField("originalHeights", privateFieldBindingFlags);
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

            PatchAudio();

            var gameSessionHandler = FindObjectOfType<GameSessionHandler>();
            if (gameSessionHandler != null)
            {
                OnGameplayLevelLoad(gameSessionHandler);
                return;
            }

            if (scene.name != characterSelectSceneString || amountOfPlayers <= 4)
                return;

            var selectHandler = FindObjectOfType<CharacterSelectHandler>();
            var handlerType = typeof(CharacterSelectHandler);
            var animateOutDelaysField = handlerType.GetField("animateOutDelays", privateFieldBindingFlags);
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

        void OnGameplayLevelLoad(GameSessionHandler gameSessionHandler)
        {
            if (amountOfPlayers <= 4)
                return;

            Array.Resize(ref gameSessionHandler.teamSpawns, amountOfPlayers);

            Debug.Log($"Modifying team spawns");

            int randomSpawnOffset = UnityEngine.Random.Range(0, 4);
            for (int i = 4; i < amountOfPlayers; i++)
            {
                var spacing = gameSessionHandler.teammateSpawnSpacing;
                var offset = new Vec2(Fix.Zero, spacing * (Fix)(i / 4) * (Fix)0.5);
                gameSessionHandler.teamSpawns[i] = gameSessionHandler.teamSpawns[(i + randomSpawnOffset) % 4] + offset;
            }
        }
    }
}