using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using IL = System.Reflection.Emit;
using System.Security.Authentication.ExtendedProtection;
using UnityEngine;
using UnityEngine.UIElements;
using System.Runtime.CompilerServices;
using UnityEditor;
using SOLASCustomLevels;
//using CustomPieces;
using System.Linq;
using MonoMod.Utils;
using System.Text;
using MonoMod.Cil;
using static System.Reflection.Emit.OpCodes;
using UnityEngine.Experimental.PlayerLoop;
using Mono.Cecil.Cil;
using System.Text.RegularExpressions;
using System.CodeDom;
using static GlobalVariables;
using System.Dynamic;
#if CUSTOMPIECES
using CustomPieces;
using CustomPieces.BuiltInPieces;
#endif

namespace SOLASCustomLevels
{
    public enum Tiles
    {
        Empty = 0,
        NoPlace = 1,
        OutOfBounds = 2,
        Wall = 3,
        Blocker = 5,
        PowerNode = 7,
        FakeWall = 9,
        MiniCannon = 10
    }

    public delegate void MoveController(InteractableController controller);
    public delegate void FadeController(InteractableController controller, bool fade);

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("SOLAS 128.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> configDoChanges;
        internal static ConfigEntry<string> configFilePath;
        internal static ManualLogSource logger;
        internal static ConfigEntry<bool> configSkipIntro;
        internal static ConfigEntry<bool> configDebugMode;
        internal static ConfigEntry<string> configZoneFilePath;
        public static Dictionary<int, Location> nodeIDs = [];
        public static int[,] modifiedZones;
        public static Plugin instance;
        public static bool[,] moveableTiles = new bool[253, 253];
        public static int?[,] teleporters = new int?[253, 253];
        public static Tuple<string, List<Location>, int, int>[,] comboLocks = new Tuple<string, List<Location>, int, int>[253, 253];
        public static Dictionary<int, string> codeStorages = [];
        public static Dictionary<int, Location> doorIDs = [];
        public static Dictionary<Type, GameObject> controllerTemplates = [];
        public static Dictionary<Type, Dictionary<Location, object>> extraData = [];
        public static GameController gc;
        //public static Dictionary<int, Tuple<Location, List<Location>>> bossPhases = new();
        //public static Type AnimateTo0Enumerator = typeof(MirrorController).GetNestedType("\'<AnimateToPosition0>d__19\'", BindingFlags.NonPublic);

        public static List<Type> CustomPieces = [];

        private void Awake()
        {
            // Plugin startup logic
            FileLog.Reset();
            logger = Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
            configDoChanges = Config.Bind("LevelLoading", "doChanges", true, "Whether to modify level data upon loading a file");
            configFilePath = Config.Bind("LevelLoading", "filePath", Paths.GameRootPath + @"\level_mods.lvl", "The level to load");
            configSkipIntro = Config.Bind("GameLoading", "skipIntro", true, "Whether to skip the intro upon loading a new file.\nRequired if the area near the intro cutscene is modified.");
            configDebugMode = Config.Bind("DebugMode", "debugMode", false, "Whether a new file should be created with every room unlocked to be moved to via the map");
            configZoneFilePath = Config.Bind("LevelLoading", "zoneFilePath", Paths.GameRootPath + @"\zones.zone", "The file containing zone modifications to load");
            Harmony harmony = new("SolasLevelEditor");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            instance = this;
            //CustomPieces.Add(typeof(VerticalMirrorController));
            comboLocks[228, 8] = new("0351", [new(230, 2), new(232, 2), new(234, 2)], 0, -1);
            comboLocks[230, 8] = new("0351", [new(230, 2), new(232, 2), new(234, 2)], 1, -1);
            comboLocks[232, 8] = new("0351", [new(230, 2), new(232, 2), new(234, 2)], 2, -1);
            comboLocks[234, 8] = new("0351", [new(230, 2), new(232, 2), new(234, 2)], 3, -1);
            codeStorages[-1] = "----";
            nodeIDs[1] = new(115, 185);
            nodeIDs[2] = new(161, 227);
            nodeIDs[3] = new(231, 161);
            nodeIDs[4] = new(189, 91);
            nodeIDs[5] = new(7, 161);
            nodeIDs[6] = new(21, 88);
            nodeIDs[7] = new(133, 35);
            nodeIDs[8] = new(35, 21);
            doorIDs[1] = new(114, 137);
            doorIDs[2] = new(124, 137);
            doorIDs[3] = new(124, 135);
            doorIDs[4] = new(124, 133);
            doorIDs[5] = new(114, 135);
            doorIDs[6] = new(114, 133);
            doorIDs[7] = new(114, 10);
            doorIDs[8] = new(124, 10);
#if CUSTOMPIECES
            controllerTemplates.Add(typeof(TextController), MakeControllerBase<TextController>());
            CustomPieces.Add(typeof(TextController));
#endif
        }

        public static GameObject MakeControllerBase<T>() where T : MonoBehaviour
        {
            var go = new GameObject();
            go.AddComponent<T>();
            return go;
        }

        void Update()
        {
            if (gc == null)
                return;
            var controller = AccessTools.Field(typeof(GameController), "selectedInteractable").GetValue(gc) as InteractableController;
            var clickState = AccessTools.Field(typeof(GameController), "currentClickState").GetValue(gc) as int?;
            switch(controller)
            {
                
            }
        }

        public void switchVal<T>(T value, Dictionary<T, Action> cases, Action defaultAction = null)
        {
            if (cases.ContainsKey(value))
                cases[value]();
            else if (defaultAction is not null)
                defaultAction();
        }

        internal static int[] ModifyLevelData(int[] levelData, FileInfo srcFile)
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("Modify Level Data");
            if (!configDoChanges.Value)
            {
                logger.LogInfo("Skipping modifications due to config");
                return levelData;
            }
            logger.LogMessage("Modifying leveldata");
            try
            {
                using StreamReader reader = new(srcFile.OpenRead());
                logger.LogInfo("Found file");
                Dictionary<int, Tuple<string, List<Location>>> comboLocks = new();
                foreach (string line in reader.ReadToEnd().Split('\n'))
                {
                    if (line.Trim() != "" && line[0] != '#' && line[0] != '*')
                    {
                        var nodeID = -1;
                        var theseShouldBeMoveable = false;
                        var theseShouldHaveID = 0;
                        logger.LogInfo("Reading line '" + line + "'");
                        var coords = line.Split('=')[0].Trim().Split(',');
                        var replaceWith = line.Split('=')[1].Trim();
                        int tileID;
                        int doorID = -1;
                        int? combolockID = null;
                        var combolockPos = 0;
                        object extraData = null;
                        Type controllerType = null;
                        if (!int.TryParse(replaceWith, out _))
                        {
                            var args = replaceWith.IndexOf(' ') != -1 ? ((Func<string[], string[]>)((string[] arr) =>
                            {
                                string[] outputArr = new string[arr.Length - 1];
                                for (int i = 1; i < arr.Length; i++)
                                {
                                    outputArr[i - 1] = arr[i];
                                }
                                return outputArr;
                            }))(replaceWith.Split(' ')) : [];
                            string dir = "up";
                            bool moveable = false;
                            bool rotateable = false;
                            bool flips = false;
                            string color = "void";
                            int type = 0;
                            string state = "closed";
                            int channel = 0;
                            switch (replaceWith.Split(' ')[0])
                            {
                                case "mirror":
                                    tileID = 100;
                                    dir = args[0];
                                    moveable = bool.Parse(args[1]);
                                    rotateable = bool.Parse(args[2]);
                                    flips = bool.Parse(args[3]);
                                    if (!(moveable || rotateable))
                                        tileID += 1;
                                    else if (moveable && !rotateable)
                                        tileID += 3;
                                    else if (!moveable && rotateable)
                                        tileID += 5;
                                    else
                                        tileID += 7;
                                    if (flips)
                                        tileID += 20;
                                    if (dir == "up")
                                        tileID++;
                                    break;
                                case "prism":
                                    tileID = 110;
                                    dir = args[0];
                                    moveable = bool.Parse(args[1]);
                                    rotateable = bool.Parse(args[2]);
                                    if (!(moveable || rotateable))
                                        tileID += 1;
                                    else if (moveable && !rotateable)
                                        tileID += 3;
                                    else if (!moveable && rotateable)
                                        tileID += 5;
                                    else
                                        tileID += 7;
                                    if (dir == "up")
                                        tileID++;
                                    break;
                                case "emitter":
                                    tileID = 0;
                                    dir = args[0];
                                    color = args[1];
                                    moveable = bool.Parse(args[2]);
                                    if (moveable)
                                        theseShouldBeMoveable = true;
                                    tileID += 10 * color switch
                                    {
                                        "red" => 1,
                                        "green" => 2,
                                        "yellow" => 3,
                                        "blue" => 4,
                                        "magenta" => 5,
                                        "cyan" => 6,
                                        "white" => 7,
                                        "void" => 8,
                                        _ => 8
                                    };
                                    tileID += dir switch
                                    {
                                        "up" => 1,
                                        "right" => 2,
                                        "down" => 3,
                                        "left" => 4,
                                        _ => 1
                                    };
                                    break;
                                case "receiver":
                                    tileID = 0;
                                    dir = args[0];
                                    color = args[1];
                                    tileID += 10 * color switch
                                    {
                                        "red" => 1,
                                        "green" => 2,
                                        "yellow" => 3,
                                        "blue" => 4,
                                        "magenta" => 5,
                                        "cyan" => 6,
                                        "white" => 7,
                                        "any" => 8,
                                        _ => 8
                                    };
                                    tileID += dir switch
                                    {
                                        "up" => 5,
                                        "right" => 6,
                                        "down" => 7,
                                        "left" => 8,
                                        _ => 5
                                    };
                                    break;
                                case "noplace":
                                    tileID = 1;
                                    break;
                                case "outofbounds":
                                    tileID = 2;
                                    break;
                                case "wall":
                                    tileID = 3;
                                    break;
                                case "default":
                                    tileID = -1;
                                    break;
                                case "glitch":
                                    tileID = 6;
                                    break;
                                case "fakewall":
                                    tileID = 9;
                                    break;
                                case "glitchdestroyer":
                                    tileID = 10;
                                    break;
                                case "filter":
                                    tileID = 90;
                                    color = args[0];
                                    tileID += color switch
                                    {
                                        "red" => 1,
                                        "green" => 2,
                                        "yellow" => 3,
                                        "blue" => 4,
                                        "magenta" => 5,
                                        "cyan" => 6,
                                        "white" => 7,
                                        "void" => 8,
                                        _ => 8
                                    };
                                    break;
                                case "button":
                                    tileID = 301;
                                    type = int.Parse(args[0]);
                                    tileID += type * 3;
                                    break;
                                case "door":
                                    tileID = 302;
                                    type = int.Parse(args[0]);
                                    state = args[1];
                                    tileID += type * 3;
                                    if (state == "open")
                                        tileID++;
                                    break;
                                case "corner":
                                    tileID = 4;
                                    break;
                                case "blocker":
                                    tileID = 5;
                                    break;
                                case "teleporter":
                                    tileID = 201;
                                    channel = int.Parse(args[0]);
                                    moveable = bool.Parse(args[1]);
                                    if (moveable)
                                        theseShouldBeMoveable = true;
                                    theseShouldHaveID = channel;
                                    break;
                                case "powernode":
                                    tileID = 7;
                                    nodeID = int.Parse(args[0]);
                                    break;
                                case "nodedoor":
                                    tileID = 171;
                                    doorID = int.Parse(args[0]);
                                    break;
                                case "combolock":
                                    tileID = 200;
                                    if (comboLocks.ContainsKey(int.Parse(args[0])))
                                    {
                                        combolockID = int.Parse(args[0]);
                                        combolockPos = int.Parse(args[1]);
                                    }
                                    else
                                        throw new ArgumentException("Combolock ID " + args[0] + " has not been defined");
                                    break;
                                /*case "bossphase":
                                    tileID = 131;
                                    int phase = int.Parse(args[0].Trim());
                                    Location bossPos = new(int.Parse(args[1].Trim().Split(',')[0]), int.Parse(args[1].Trim().Split(',')[0]));
                                    List<Location> tilesToDestroy = new();
                                    foreach (var loc in args.Skip(2))
                                    {
                                        tilesToDestroy.Add(new Location(int.Parse(loc.Trim().Split(',')[0]), int.Parse(loc.Trim().Split(',')[1])));
                                    }
                                    bossPhases.Add(phase, new(bossPos, tilesToDestroy));
                                    break;*/
                                case "empty":
#if !CUSTOMPIECES
                                default:
#endif
                                    tileID = 0;
                                    break;
#if CUSTOMPIECES
                                default:
                                    Tuple<int, object> tile = (Tuple<int, object>)AccessTools.Method(GetControllerType(replaceWith.Split(' ')[0]), nameof(CustomController.Global_MakeTile)).Invoke(Activator.CreateInstance(GetControllerType(replaceWith.Split(' ')[0])), [args]);
                                    controllerType = GetControllerType(replaceWith.Split(' ')[0]);
                                    tileID = tile.Item1;
                                    extraData = tile.Item2;
                                    break;
#endif
                            }
                        }
                        else
                            tileID = int.Parse(replaceWith);
                        List<int> indices = [];
                        try
                        {
                            string[] coordsX = coords[2].Trim().Split('-');
                            string[] coordsY = coords[3].Trim().Split('-');
                            if (tileID == 7)
                            {
                                if (coordsX.Length == 2 || coordsY.Length == 2)
                                    throw new NotSupportedException("Range syntax is not supported for power nodes");
                                nodeIDs[nodeID] = new Location(int.Parse(coords[0].Trim()) * 14 + int.Parse(coordsX[0].Trim()), int.Parse(coords[1].Trim()) * 14 + int.Parse(coordsY[0].Trim()));
                            }
                            else if (tileID == 171)
                            {
                                if (coordsX.Length == 2 || coordsY.Length == 2)
                                    throw new NotSupportedException("Range syntax is not supported for power node doors");
                                doorIDs[doorID] = new Location(int.Parse(coords[0].Trim()) * 14 + int.Parse(coordsX[0].Trim()), int.Parse(coords[1].Trim()) * 14 + int.Parse(coordsY[0].Trim()));
                            }
                            else if (tileID < -1)
                            {
                                Plugin.extraData[controllerType][new Location(int.Parse(coords[0].Trim()) * 14 + int.Parse(coordsX[0].Trim()), int.Parse(coords[1].Trim()) * 14 + int.Parse(coordsY[0].Trim()))] = extraData;
                            }
                            logger.LogInfo("Replacing range '" + coordsX[0] + ", " + ((coordsX.Length == 2) ? int.Parse(coordsX[1].Trim()) : int.Parse(coordsX[0].Trim())) + "', '" + coordsY[0] + ", " + ((coordsY.Length == 2) ? int.Parse(coordsY[1].Trim()) : int.Parse(coordsY[0].Trim())) + "'");
                            for (int i = int.Parse(coordsX[0].Trim()); i <= ((coordsX.Length == 2) ? int.Parse(coordsX[1].Trim()) : int.Parse(coordsX[0].Trim())); i++)
                            {
                                for (int j = int.Parse(coordsY[0].Trim()); j <= ((coordsY.Length == 2) ? int.Parse(coordsY[1].Trim()) : int.Parse(coordsY[0].Trim())); j++)
                                {
                                    logger.LogInfo("Replacing tile " + i.ToString() + ", " + j.ToString());
                                    indices.Add(int.Parse(coords[0].Trim()) * 14 + i + (int.Parse(coords[1].Trim()) * 14 + j) * 253);
                                    moveableTiles[int.Parse(coords[0].Trim()) * 14 + i, int.Parse(coords[1].Trim()) * 14 + j] = theseShouldBeMoveable;
                                    teleporters[int.Parse(coords[0].Trim()) * 14 + i, int.Parse(coords[1].Trim()) * 14 + j] = theseShouldHaveID;
                                    if (combolockID.HasValue)
                                    {
                                        Plugin.comboLocks[int.Parse(coords[0].Trim()) * 14 + i, int.Parse(coords[1].Trim()) * 14 + j] = new(comboLocks[combolockID.Value].Item1, comboLocks[combolockID.Value].Item2, combolockPos, combolockID.Value);
                                        codeStorages[combolockID.Value] = new('-', comboLocks[combolockID.Value].Item1.Length);
                                    }
                                }
                            }
                            var level = Levels.level1;
                            int[] flattenedLevel = new int[level.GetLength(0) * level.GetLength(1)];
                            flattenedLevel = GetFlattenedArray(level);
                            foreach (int index in indices)
                                levelData[index] = tileID != -1 ? tileID : flattenedLevel[index];
                            logger.LogInfo("Successfully modified tile " + coords[2] + ", " + coords[3]);
                        }
                        catch (Exception e)
                        {
                            logger.LogInfo("Could not execute line " + line + ", exception: " + e.ToString());
                            continue;
                        }
                    }
                    else if (line[0] == '*')
                    {
                        var type = line.Remove(0, 1).Split(' ')[0];
                        var args = new List<string>(line.Remove(0, 1).Split(' ').Skip(1));

                        switch(type)
                        {
                            case "combolock":
                                comboLocks[int.Parse(args[0].Trim())] = new Tuple<string, List<Location>>(args[1].Trim(), new List<Location>(args.Skip(2).ToDictionary(str =>
                                {
                                    return new Location(int.Parse(str.Trim().Split(',')[0]), int.Parse(str.Trim().Split(',')[1]));
                                }).Keys));
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to modify level data | Exception: " + e.ToString());
            }
            return levelData;
        }

        internal static int[,] ModifyZoneData(int[,] zoneData, FileInfo srcFile)
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("Modify Zone Data");
            if (!configDoChanges.Value)
            {
                logger.LogInfo("Skipping modifications due to config");
                return zoneData;
            }
            logger.LogMessage("Modifying zone data");
            try
            {
                using StreamReader reader = new(srcFile.OpenRead());
                logger.LogInfo("Found file");
                foreach (string line in  reader.ReadToEnd().Split('\n'))
                {
                    if (line.Trim() != "" && line[0] != '#')
                    {
                        var newZone = line.Split('=')[1].Trim();
                        var coordsStr = line.Split('=')[0].Trim().Split(',');
                        var coords = Array.ConvertAll(coordsStr, new Converter<string, int>((str) => { return int.Parse(str.Trim()); }));
                        zoneData[coords[1], coords[0]] = newZone switch
                        {
                            "intro" => 1,
                            "hub" => 5,
                            "blue" => 2,
                            "red" => 3,
                            "green" => 4,
                            "boss" => 6,
                            _ => 0,
                        };
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to modify zone data | Exception: " + e.ToString());
            }
            return zoneData;
        }

#if CUSTOMPIECES
        internal static Type GetControllerType(int tileID)
        {
            foreach (var piece in CustomPieces)
            {
                if (piece.IsSubclassOf(typeof(CustomController)))
                {
                    if (((List<int>)AccessTools.Method(piece, "Global_GetTileIDs").Invoke(Activator.CreateInstance(piece), null)).Contains(tileID))
                        return piece;
                }
            }
            return null;
        }

        internal static Type GetControllerType(string name)
        {
            foreach (var piece in CustomPieces)
            {
                if (piece.IsSubclassOf(typeof(CustomController)))
                {
                    if (name == (string)AccessTools.Method(piece, "Global_GetTileName").Invoke(Activator.CreateInstance(piece), null))
                        return piece;
                }
            }
            return null;
        }
#endif

        internal static void UnlockMap()
        {
            for (int i = 0; i < 18; i++)
            {
                for (int j = 0; j < 18; j++)
                {
                    SetVisitedAt(i, j);
                }
            }
        }
    }

    /*[HarmonyPatch(typeof(TesseractController), "FindMirrorsForBoss")]
    public class Patch_FindMirrorsForBoss
    {
        public static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(TesseractController), "DestroyItems")]
    public class Patch_TesseractControllerDestroyItems
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.RemoveInstructionsInRange(0, matcher.Length - 9);
            matcher.Advance(4);
            matcher.Insert(
                new CodeInstruction(Call, AccessTools.Method(typeof(Patch_TesseractControllerDestroyItems), "Temp")),
                new CodeInstruction(Stloc_0));
            return matcher.InstructionEnumeration();
        }

        public static GameObject[] Temp()
        {
            var objects = new List<GameObject>();
            foreach (var loc in Plugin.bossPhases[CurrentBossPhase].Item2)
            {
                objects.Add(GetInteractableControllerAt(loc.x, loc.y).gameObject);
            }
            return objects.ToArray();
        }
    }*/

    [HarmonyPatch(typeof(PowerNodeController), "TurnOnIfNecessary")]
    public class Patch_TurnOnIfNecessary
    {
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception.GetType() == typeof(NullReferenceException) || __exception.GetType() == typeof(IndexOutOfRangeException))
            {
                return null;
            }
            return __exception;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions).MatchForward(false, new CodeMatch(Ldc_I4_1)).SetOpcodeAndAdvance(Ldc_I4_0).InstructionEnumeration();
        }
    }

    [HarmonyPatch(typeof(GameController), "checkForPulseToPieceCollision")]
    public class Patch_checkForPulseToPieceCollision
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false, new CodeMatch(Ldsfld, AccessTools.Field(typeof(GlobalVariables), nameof(powerNodesActive))));
            matcher.Advance(1);
            matcher.RemoveInstructions(5);
            matcher.Insert(
                new CodeInstruction(Ldarg_1),
                new CodeInstruction(Ldarg_2),
                new CodeInstruction(Call, AccessTools.Method(typeof(Patch_checkForPulseToPieceCollision), "Temp")));
            return matcher.InstructionEnumeration();
        }

        public static int Temp(int x, int y)
        {
            var controller = (NodeDoorController)GetInteractableControllerAt(x, y);
            return (int)AccessTools.Field(typeof(NodeDoorController), "NodeNumber").GetValue(controller);
        }
    }

    [HarmonyPatch(typeof(GameController), "BuildLevel")]
    public class Patch_BuildLevel
    {
        public static void Prefix(ref int[] level, GameController __instance)
        {
            bool isFirstLoad = (bool)typeof(GameController).GetField("showFullIntro", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
            if (isFirstLoad)
            {
                level = Plugin.ModifyLevelData(level, new FileInfo(Plugin.configFilePath.Value));
                Plugin.modifiedZones = Plugin.ModifyZoneData((int[,])Levels.ZoneMap.Clone(), new FileInfo(Plugin.configZoneFilePath.Value));
                powerNodeLocations = new Location[Plugin.nodeIDs.Keys.Max() + 5];
                foreach (var pair in Plugin.nodeIDs)
                {
                    powerNodeLocations[pair.Key] = pair.Value;
                }
                if (Plugin.configSkipIntro.Value)
                {
                    __instance.showIntro = true;
                    typeof(GameController).GetField("showFullIntro", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, false);
                    AccessTools.Field(typeof(GameController), "introComplete").SetValue(__instance, true);
                    __instance.startScreenX = 3;
                    __instance.startScreenY = 16;
                }
            }
        }
    }

    [HarmonyPatch(typeof(GameController), "Start")]
    public class Patch_GameControllerStart
    {
        public static void Postfix(GameController __instance)
        {
            ((GameObject)AccessTools.Field(typeof(GameController), "undoIconGO").GetValue(__instance)).SetActive(true);
            ((GameObject)AccessTools.Field(typeof(GameController), "fastForwardIconGO").GetValue(__instance)).SetActive(true);
            ((GameObject)AccessTools.Field(typeof(GameController), "helpIconGO").GetValue(__instance)).SetActive(true);
            CurrentGamePhase = 4;
            Plugin.gc = __instance;
            var lb = __instance.levelBuilder;
            foreach (var component in lb.FakeWallPiece.GetComponents(typeof(Component)))
            {
                Plugin.logger.LogInfo(component.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(GlobalVariables), nameof(SetTeleportLocation))]
    public class Patch_SetTeleportLocation
    {
        public static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(GlobalVariables), nameof(GetZoneAt))]
    public class Patch_GetZoneAt
    {
        public static void Postfix(ref Zone __result, int ScreenX, int ScreenY)
        {
            __result = (Zone)Plugin.modifiedZones[ScreenY, ScreenX];
        }
    }

    [HarmonyPatch(typeof(GameController), "StartSimpleIntro")]
    public class Patch_StartSimpleIntro
    {
        public static void Postfix()
        {
            if (Plugin.configDebugMode.Value)
                Plugin.UnlockMap();
        }
    }

    [HarmonyPatch(typeof(TeleportController), nameof(TeleportController.SetupPiece))]
    public class Patch_TeleportControllerSetupPiece
    {
        public static void Prefix(ref bool drag, int tileX, int tileY)
        {
            drag = Plugin.moveableTiles[tileX, tileY];
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var found = false;
            foreach (var instr in instructions)
            {
                if (!found)
                {
                    if (instr.opcode == Ldc_I4_0)
                    {
                        found = true;
                    }
                    yield return instr;
                }
                else
                {
                    if (instr.opcode == Ldc_I4_0)
                    {
                        yield return new CodeInstruction(Ldarg_3);
                    }
                    else
                    {
                        yield return instr;
                    }
                }
            }
        }

        public static void Postfix(TeleportController __instance, int tileX, int tileY)
        {
            __instance.BaseLocation = new Vector3(tileX, -tileY, __instance.BaseLocation.z);
            __instance.VisibleLocation = __instance.BaseLocation;
        }
    }

    [HarmonyPatch(typeof(EmitterController), nameof(EmitterController.SetupPiece))]
    public class Patch_EmitterControllerSetupPiece
    {
        public static void Prefix(ref bool drag, int tileX, int tileY)
        {
            drag = Plugin.moveableTiles[tileX, tileY];
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var instrs = new List<CodeInstruction>(instructions);
            var index = instrs.FindIndex(instr => instr.opcode == Ldc_I4_0);
            instrs[index] = new CodeInstruction(Ldarg_3);
            return instrs;
        }

        public static void Postfix(EmitterController __instance, int tileX, int tileY)
        {
            __instance.BaseLocation = new Vector3(tileX, -tileY, __instance.BaseLocation.z);
            __instance.VisibleLocation = __instance.BaseLocation;
        }
    }

    [HarmonyPatch(typeof(EmitterController), nameof(EmitterController.ClickPiece))]
    public class Patch_EmitterControllerClickPiece
    {
        public static bool Prefix(EmitterController __instance, ref int __result)
        {
            if (((EmitterReceiver)typeof(EmitterController).GetPrivateField("Emitter", __instance)).Type == EmitterReceiver.EmitterType.EMIT_ONLY)
            {
                __result = __instance.TileValue;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameController), "checkMouse")]
    public class Patch_GameControllerCheckMouse
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new(instructions);
            matcher.MatchForward(false, new CodeMatch(Clt));
            matcher.Set(Call, new Func<int, GameController, bool>(Temp).GetMethodInfo());
            matcher.Advance(-1);
            matcher.Set(Ldarg_0, null);
            return matcher.InstructionEnumeration();
        }

        public static bool Temp(int n, GameController instance)
        {
            return n < 90 && ((InteractableController)AccessTools.Field(typeof(GameController), "selectedInteractable").GetValue(instance)).Click;
        }
    }

    [HarmonyPatch(typeof(GameController), "MovePiece")]
    public class Patch_MovePiece
    {
        public static void Prefix(GameController __instance, int tileX, int tileY, Location ___mouseStartLoc, InteractableController ___selectedInteractable)
        {
            if (___selectedInteractable is EmitterController ec)
            {
                ((EmitterReceiver)typeof(EmitterController).GetPrivateField("Emitter", ec)).X = tileX;
                ((EmitterReceiver)typeof(EmitterController).GetPrivateField("Emitter", ec)).Y = tileY;
            }
            else if (___selectedInteractable is TeleportController)
            {
#pragma warning disable
                var oldID = Plugin.teleporters[___mouseStartLoc.x, ___mouseStartLoc.y];
                Plugin.teleporters[___mouseStartLoc.x, ___mouseStartLoc.y] = null;
#pragma warning restore
                Plugin.teleporters[tileX, tileY] = oldID;
            }
        }
    }

    [HarmonyPatch(typeof(GlobalVariables), nameof(GetTeleportOutput))]
    public class Patch_GetTeleportOutput
    {
        public static bool Prefix(int x, int y, int pieceType, ref Location __result)
        {
            bool isCustom = false;
            int? id;
            if (Plugin.teleporters[x, y] is not null)
            {
                isCustom = true;
                id = (int)Plugin.teleporters[x, y];
            }
            else
            {
                id = pieceType;
            }
            List<int?> level;
            if (isCustom)
            {
                level = [];
                for (int i = 0; i < 253; i++)
                {
                    for (int j = 0; j < 253; j++)
                    {
                        level.Add(Plugin.teleporters[j, i]);
                    }
                }
            }
            else
            {
                level = new List<int?>(currentLevelState.Cast<int?>());
            }
            var firstIndex = level.IndexOf(id);
            var lastIndex = level.LastIndexOf(id);
            if (firstIndex % 253 == x && firstIndex / 253 == y)
            {
                __result = new Location(lastIndex % 253, lastIndex / 253);
            }
            else
            {
                __result = new Location(firstIndex % 253, firstIndex / 253);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportController), "Update")]
    public class Patch_TeleportControllerUpdate
    {
        public static void Postfix(TeleportController __instance)
        {
            new MoveController(RevPatch_WallControllerUpdate.Update)(__instance);
        }
    }

    [HarmonyPatch(typeof(EmitterController), "Update")]
    public class Patch_EmitterControllerUpdate
    {
        public static void Postfix(EmitterController __instance)
        {
            new MoveController(RevPatch_WallControllerUpdate.Update)(__instance);
            SetTileAt(__instance.TileX, __instance.TileY, __instance.TileValue);
        }
    }

    [HarmonyPatch]
    public class RevPatch_WallControllerUpdate
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(WallController), "Update")]
        public static void Update(InteractableController instance)
        {

        }
    }

#if CUSTOMPIECES
    [HarmonyPatch(typeof(MirrorController), "AnimateToPosition0", MethodType.Enumerator)]
    public class RevPatch_MirrorControllerAnimateToPosition0
    {
        [HarmonyReversePatch]
        public static bool AnimateToPosition0(CustomController.RotatePieceEnumerator instance, float angle)
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (var instr in new CodeMatcher(instructions)
                    .MatchForward(false, new CodeMatch(Call))
                    .Advance(-3)
                    .SetAndAdvance(Nop, null)
                    .SetAndAdvance(Nop, null)
                    .SetAndAdvance(Nop, null)
                    .SetAndAdvance(Nop, null)
                    .InstructionEnumeration())
                {
                    if (instr.opcode == Ldc_R4 && (float)instr.operand == 90f)
                    {
                        yield return new CodeInstruction(Ldarg_0);
                        yield return new CodeInstruction(Ldfld, AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.angle)));
                    }
                    else
                    {
                        var newInstr = new CodeInstruction(instr);
                        if ((instr.opcode == Ldfld || instr.opcode == Stfld) && (((FieldInfo)instr.operand).Name.Contains('<') || ((FieldInfo)instr.operand).Name == "RotateGroup" || ((FieldInfo)instr.operand).Name == "rotateTime"))
                        {
                            switch (((FieldInfo)instr.operand).Name)
                            {
                                case "<>1__state":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.state)); break;
                                case "<>2__current":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.current)); break;
                                case "<>4__this":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.instance)); break;
                                case "<rotation>5__1":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.rotation)); break;
                                case "<timeTaken>5__2":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.timeTaken)); break;
                                case "<z>5__3":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.z)); break;
                                case "RotateGroup":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController), nameof(CustomController.controllerGO)); break;
                                case "rotateTime":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController), nameof(CustomController.rotateTime)); break;
                                default:
                                    break;
                            }
                        }
                        yield return newInstr;
                    }
                }
            }

            _ = Transpiler(null);
            return false;
        }
    }
#endif

    [HarmonyPatch(typeof(LevelBuilder), "BuildLevelASync", MethodType.Enumerator)]
    public class Patch_LevelBuilderBuildLevelASync
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return new CodeMatcher(instructions)
                .MatchForward(false,
                    new CodeMatch(Call, AccessTools.Method(typeof(LevelBuilder), "CreateTeleport", [typeof(int), typeof(int), typeof(int)])))
                .Advance(3)
                .Insert(
                    new CodeInstruction(Ldarg_0),
                    new CodeInstruction(Ldfld, AccessTools.Field(AccessTools.Method(typeof(LevelBuilder), "BuildLevelASync").GetStateMachineTarget().DeclaringType, "<>4__this")),
                    new CodeInstruction(Ldarg_0),
                    new CodeInstruction(Ldfld, AccessTools.Field(AccessTools.Method(typeof(LevelBuilder), "BuildLevelASync").GetStateMachineTarget().DeclaringType, "<i>5__8")),
                    new CodeInstruction(Ldarg_0),
                    new CodeInstruction(Ldfld, AccessTools.Field(AccessTools.Method(typeof(LevelBuilder), "BuildLevelASync").GetStateMachineTarget().DeclaringType, "<y>5__7")),
                    new CodeInstruction(Ldarg_0),
                    new CodeInstruction(Ldfld, AccessTools.Field(AccessTools.Method(typeof(LevelBuilder), "BuildLevelASync").GetStateMachineTarget().DeclaringType, "<num>5__9")),
                    new CodeInstruction(Call, AccessTools.Method(typeof(Patch_LevelBuilderBuildLevelASync), nameof(AddCustomPiece))))
                .InstructionEnumeration();
        }

        public static void AddCustomPiece(LevelBuilder lb, int x, int y, int pieceType)
        {
#if CUSTOMPIECES
            if (pieceType >= -1)
                return;
            Type controller = Plugin.GetControllerType(pieceType);
            
#endif
        }
    }

    [HarmonyPatch(typeof(GlyphLockController), nameof(GlyphLockController.SetupPiece))]
    public class Patch_GlyphLockControllerSetupPiece
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
        {
            var matcher = new CodeMatcher(instrs)
                .MatchForward(false, new CodeMatch(Nop), new CodeMatch(Ldarg_0), new CodeMatch(Call));
            matcher.RemoveInstructions(instrs.Count() - matcher.Pos);
            matcher.InsertAndAdvance(
                new CodeInstruction(Ldarg_0),
                new CodeInstruction(Ldarg_1),
                new CodeInstruction(Ldarg_2),
                new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GlyphLockControllerSetupPiece), nameof(Temp))),
                new CodeInstruction(Stfld, AccessTools.Field(typeof(GlyphLockController), "CombinationPosition")),
                new CodeInstruction(Ret));
            return matcher.InstructionEnumeration();
        }

        public static int Temp(int x, int y)
        {
            return Plugin.comboLocks[x, y].Item3;
        }
    }

    [HarmonyPatch(typeof(GlyphLockController), "ChangeGlyph")]
    public class Patch_GlyphLockControllerChangeGlyph
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs, ILGenerator ilGen)
        {
            var matcher = new CodeMatcher(instrs);
            matcher.MatchForward(false, new CodeMatch(Ldstr));
            matcher.SetInstructionAndAdvance(new(Ldarg_0));
            matcher.InsertAndAdvance(new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GlyphLockControllerChangeGlyph), "Temp")));
            matcher.Advance(3);
            var label = ilGen.DefineLabel();
            matcher.SetOperandAndAdvance(label);
            matcher.RemoveInstructionsInRange(matcher.Pos, matcher.Length - 1);
            matcher.InsertAndAdvance(
                new CodeInstruction(Ldarg_0),
                new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GlyphLockControllerChangeGlyph), "Temp2")),
                new CodeInstruction(Ret));
            matcher.Advance(-1);
            matcher.Instruction.labels.Add(label);
            matcher.Start();
            matcher.MatchForward(false, new CodeMatch(Ldsfld, AccessTools.Field(typeof(GlobalVariables), nameof(LockCombination))));
            matcher.Repeat(matcher =>
                matcher
                    .SetOperandAndAdvance(AccessTools.Field(typeof(Plugin), nameof(Plugin.codeStorages)))
                    .InsertAndAdvance(new CodeInstruction(Ldarg_0))
                    .InsertAndAdvance(new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GlyphLockControllerChangeGlyph), "Temp3"))));
            matcher.Start();
            matcher.MatchForward(false, new CodeMatch(Stsfld, AccessTools.Field(typeof(GlobalVariables), nameof(LockCombination))));
            matcher.Repeat(matcher =>
                matcher
                    .SetAndAdvance(Ldarg_0, null)
                    .InsertAndAdvance(new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GlyphLockControllerChangeGlyph), "Temp4"))));
            return matcher.InstructionEnumeration();
        }

        public static string Temp(GlyphLockController controller)
        {
            return Plugin.comboLocks[controller.TileX, controller.TileY].Item1;
        }

        public static void Temp2(GlyphLockController controller)
        {
            foreach (var loc in Plugin.comboLocks[controller.TileX, controller.TileY].Item2)
            {
                ((GlitchController)GetInteractableControllerAt(loc.x, loc.y)).DisableGlitch();
            }
        }

        public static string Temp3(Dictionary<int, string> dict, GlyphLockController controller)
        {
            return dict[Plugin.comboLocks[controller.TileX, controller.TileY].Item4];
        }

        public static void Temp4(string str, GlyphLockController controller)
        {
            Plugin.codeStorages[Plugin.comboLocks[controller.TileX, controller.TileY].Item4] = str;
        }
    }

    [HarmonyPatch(typeof(NodeDoorController), nameof(NodeDoorController.LinkToCorrectNode))]
    public class Patch_LinkToCorrectNode
    {
        public static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(NodeDoorController), "SetupPiece")]
    public class Patch_NodeDoorControllerSetupPiece
    {
        public static void Postfix(ref int ___NodeNumber, int tileX, int tileY, NodeDoorController __instance)
        {
            foreach (var pair in Plugin.doorIDs)
            {
                if (new Location(tileX, tileY).Equals(pair.Value))
                {
                    ___NodeNumber = pair.Key;
                    break;
                }
            }
            __instance.Glyph.SetActive(false);
#if DEBUG
            __instance.Click = true;
#endif
        }
    }

#if DEBUG
    [HarmonyPatch(typeof(NodeDoorController), "ClickPiece")]
    public class Patch_NodeDoorControllerClickPiece
    {
        public static void Prefix(NodeDoorController __instance)
        {
            Plugin.logger.LogInfo("Clicked NodeDoor " + AccessTools.Field(typeof(NodeDoorController), "NodeNumber").GetValue(__instance) + " at " + __instance.TileX + "," + __instance.TileY);
        }
    }
#endif

    [HarmonyPatch(typeof(PowerNodeController), "SetupPiece")]
    public class Patch_PowerNodeControllerSetupPiece
    {
        public static void Postfix(PowerNodeController __instance)
        {
#if DEBUG
            __instance.Click = true;
#endif
            __instance.PulseHit();
        }
    }

#if DEBUG
    [HarmonyPatch(typeof(PowerNodeController), "ClickPiece")]
    public class Patch_PowerNodeControllerClickPiece
    {
        public static void Prefix(PowerNodeController __instance)
        {
            Plugin.logger.LogInfo("Clicked Powernode " + AccessTools.Method(typeof(PowerNodeController), "GetNodeNumber").Invoke(__instance, null));
        }
    }
#endif

    [HarmonyPatch]
    public class RevPatch_DisableRingEnum : IEnumerator
    {
        object current;

        int state;

        public MiniCannonController instance;

        public MeshRenderer ring;

        public float totalTime;

        public float time;

        public Color initColour;

        public float redDiff;
        public float greenDiff;
        public float blueDiff;

        public float t;

        public object Current => current;

        public bool MoveNext()
        {
            return MoveNext(this);
        }

        public RevPatch_DisableRingEnum(int state)
        {
            this.state = state;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(MiniCannonController), "DisableRing", MethodType.Enumerator)]
        public static bool MoveNext(object instance)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var matcher = new CodeMatcher(instructions.TranspileEnumerator<RevPatch_DisableRingEnum>(AccessTools.GetDeclaredFields(typeof(RevPatch_DisableRingEnum))));
                matcher.MatchForward(false, new CodeMatch(Sub)).SetInstruction(new(Call, AccessTools.Method(typeof(RevPatch_DisableRingEnum), nameof(RSub))));
                matcher.MatchForward(false, new CodeMatch(Sub)).SetInstruction(new(Call, AccessTools.Method(typeof(RevPatch_DisableRingEnum), nameof(RSub))));
                matcher.MatchForward(false, new CodeMatch(Sub)).SetInstruction(new(Call, AccessTools.Method(typeof(RevPatch_DisableRingEnum), nameof(RSub))));
                return matcher.InstructionEnumeration();
            }

            _ = Transpiler(null);
            return false;
        }

        public static float RSub(float i, float j)
        {
            return j - i;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }
    }

    [HarmonyPatch(typeof(MiniCannonController), "SetupPiece")]
    public class Patch_MiniCannonControllerSetupPiece
    {
        public static void Postfix(MiniCannonController __instance)
        {
            __instance.Click = true;
        }
    }

    [HarmonyPatch(typeof(MiniCannonController), "ClickPiece")]
    public class Patch_MiniCannonControllerClickPiece
    {
        public static bool Prefix(MiniCannonController __instance, ref int __result)
        {
            __result = __instance.TileValue;
            var shotNumber = AccessTools.Field(typeof(MiniCannonController), "shotNumber").GetValue(__instance) as int?;
            if (shotNumber <= 0)
                return false;
            Plugin.logger.LogInfo("Resetting mini glitch cannon at " + __instance.TileX + ", " + __instance.TileY + " to shot number " + (shotNumber - 1));
            shotNumber--;
            AccessTools.Field(typeof(MiniCannonController), "shotNumber").SetValue(__instance, shotNumber.Value);
            __instance.StartCoroutine(new RevPatch_DisableRingEnum(0) { instance = __instance, ring = __instance.Rings[shotNumber.Value], totalTime = 1f });
            return false;
        }
    }

    public static class Extensions
    {
        public static object GetPrivateField(this Type type, string fieldName, object instance = null)
        {
            if (instance == null)
            {
                return type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            }
            else
            {
                return type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
            }
        }

        public static void SetPrivateField(this Type type, string fieldName, object value, object instance = null)
        {
            if (instance == null)
            {
                type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
            }
            else
            {
                type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value);
            }
        }

        public static object CallPrivateMethod(this Type type, string methodName, object[] args, object instance = null)
        {
            if (instance == null)
            {
                return type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
            }
            else
            {
                return type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, args);
            }
        }

        public static int IndexOf(this List<CodeInstruction> instrs, IL.OpCode opcode, object argument = null)
        {
            return instrs.FindIndex(a => a.opcode == opcode && a.operand == argument);
        }

        public static void Prepend<T>(this List<T> list, T value)
        {
            list.Reverse();
            list.Add(value);
            list.Reverse();
        }

        /// <summary>
        /// A transpiler that helps with reverse patching enumerator methods
        /// </summary>
        /// <typeparam name="TEnumerator">The new enumerator type to use. This type's fields should match in name and type with the original method's locals and arguments.</typeparam>
        /// <param name="instrs">An enumerable of the instructions of the enumerator's MoveNext method.</param>
        /// <param name="locals">A list of FieldInfo objects representing both the local variables and parameters of the method. Use the readable name of local variables rather than the compiler generated name. The field '<>#__this' should be matched with a field called 'instance'</param>
        /// <returns>An enumerable containing the instructions of the MoveNext method, with field access instructions replaced to access the new enumerator's fields.</returns>
        public static IEnumerable<CodeInstruction> TranspileEnumerator<TEnumerator>(this IEnumerable<CodeInstruction> instrs, IEnumerable<FieldInfo> locals) where TEnumerator : IEnumerator
        {
            foreach (var instr in instrs)
            {
                var newInstr = new CodeInstruction(instr);
                if (instr.operand is null || instr.operand is not FieldInfo)
                {
                    yield return instr;
                    continue;
                }
                switch (((FieldInfo)instr.operand).Name)
                {
                    case "<>1__state":
                        newInstr.operand = AccessTools.Field(typeof(TEnumerator), "state"); break;
                    case "<>2__current":
                        newInstr.operand = AccessTools.Field(typeof(TEnumerator), "current"); break;
                    case "<>4__this":
                        newInstr.operand = AccessTools.Field(typeof(TEnumerator), "instance"); break;
                    default:
                        foreach (var local in locals)
                        {
                            var pattern = new Regex(@"(?s-m)^<" + local.Name + @">5__.+");
                            var fieldName = ((FieldInfo)instr.operand).Name;
                            if (pattern.IsMatch(fieldName))
                            {
                                newInstr.operand = AccessTools.Field(typeof(TEnumerator), local.Name);
                            }
                            else if (fieldName == local.Name)
                            {
                                newInstr.operand = AccessTools.Field(typeof(TEnumerator), local.Name);
                            }
                        }
                        break;
                }
                yield return newInstr;
            }
        }
    }
}

#if CUSTOMPIECES
namespace CustomPieces
{
    public interface IPulseInteractable
    {
        void HandlePulseCollision(ref byte pulse);
    }

    public abstract class CustomController : InteractableController
    {
        protected abstract void Update();
        public float rotateTime;

        public abstract List<int> Global_GetTileIDs();
        public GameObject controllerGO = null;

        public override void CheatComplete() { }
        public override void ActivatePiece()
        {
            StartCoroutine(ActivatePieceCR());
        }
        public override void DeactivatePiece()
        {
            StartCoroutine(DeactivatePieceCR());
        }

        public IEnumerator Rotate(float angle)
        {
            var enumerator = new RotatePieceEnumerator(angle, 0);
            enumerator.instance = this;
            return enumerator;
        }

        public CustomController()
        {

        }

        public class RotatePieceEnumerator : IEnumerator<object>
        {
            public object Current => current;
            public object current;
            public int state;
            public float rotation;
            public float timeTaken;
            public float z;
            public float angle;
            public CustomController instance;

            public RotatePieceEnumerator(float angle, int state)
            {
                this.state = state;
                this.angle = angle;
            }

            public void Dispose()
            {
                return;
            }

            public bool MoveNext()
            {
                return RevPatch_MirrorControllerAnimateToPosition0.AnimateToPosition0(this, angle);
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        public abstract Tuple<int, object> Global_MakeTile(string[] args);
        public abstract string Global_GetTileName();
    }

    namespace BuiltInPieces
    {
        public class TextController : CustomController
        {
            string Text;

            public static GameObject go = new();

            static TextController()
            {
                go.AddComponent<MeshRenderer>().material = Resources.Load<Material>("fontMaterial");
                var text = go.AddComponent<TextMesh>();
                text.font = Font.GetDefault();
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.characterSize = 1;
                text.lineSpacing = 1;
                go.AddComponent<TextController>();
            }

            public override int ClickPiece()
            {
                return -1;
            }

            public override void EnablePiece(bool enable)
            {
                
            }

            public override void FadePiece(bool fade)
            {
                
            }

            public override List<int> Global_GetTileIDs()
            {
                return [-2];
            }

            public override void InvalidAdjacent()
            {
                
            }

            public override void PulseHit(byte pulse = 0)
            {
                
            }

            public override void SetupPiece(int tileX, int tileY, bool drag, bool click, int tileValue, PieceTypes pieceType)
            {
                TileX = tileX;
                TileY = tileY;
                Drag = false;
                Click = false;
                TileValue = tileValue;
                PieceType = pieceType;
                BaseLocation = transform.position;
                VisibleLocation = BaseLocation;
                controllerGO = Instantiate(go);
                Text = (string)Plugin.extraData[typeof(TextController)][new(TileX, TileY)];
                AddPieceToFade(controllerGO.GetComponent<MeshRenderer>());
                DeactivateInstant();
                controllerGO.SetActive(true);
            }

            protected override void Update()
            {
                ((MoveController)RevPatch_WallControllerUpdate.Update)(this);
                if (controllerGO is not null)
                    controllerGO.GetComponent<TextMesh>().text = Text;
            }

            public void SetText(string text)
            {
                Text = text;
            }

            public override Tuple<int, object> Global_MakeTile(string[] args)
            {
                return new(-2, args[0]);
            }

            public override string Global_GetTileName()
            {
                return "text";
            }
        }
    }
}
#endif