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
using UnityEngine;
using UnityEngine.UIElements;
using System.Runtime.CompilerServices;
using UnityEditor;
using SOLASCustomLevels;
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
using Microsoft.CSharp;
using System.ComponentModel;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using System.CodeDom.Compiler;
using Mono.Reflection;



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

	public class OnKeyPressEventArgs : EventArgs
	{
		public string keyCodes;
		public GameController gc;
	}

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("SOLAS 128.exe")]
    public class Plugin : BaseUnityPlugin
    {
		public const int DEFAULT_FREQUENCY = 4;
		internal static ConfigEntry<bool> configDoChanges;
        internal static ConfigEntry<string> configFilePath;
        public static ManualLogSource logger;
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
        public static Dictionary<Type, Dictionary<Location, object>> extraData = [];
        public static GameController gc;
        static bool GameStarted = false;
        public static Material GlitchMaterial;
        public static GameObject RotateQuad;
        public static GameObject MoveQuad;
		public static Material[] OrigMiniCannonColors;
		public static Dictionary<Location, GlitchController> destroyedGlitches = [];
		internal static ConfigEntry<string> configShowMapKey;
		public static event EventHandler<GameController> OnGameStart;
		public static event EventHandler<OnKeyPressEventArgs> OnKeyPress;
		static bool inMapScreen = false;
		internal static string gameSource = "";
        //public static Dictionary<int, Tuple<Location, List<Location>>> bossPhases = new();
        //public static Type AnimateTo0Enumerator = typeof(MirrorController).GetNestedType("\'<AnimateToPosition0>d__19\'", BindingFlags.NonPublic);

        public static List<Type> CustomPieces = [];

        private void Awake()
        {
            // Plugin startup logic
            FileLog.Reset();
            logger = Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
            configDoChanges = Config.Bind("LevelLoading", "doChanges", true, "Whether to modify level data upon loading a file.");
            configFilePath = Config.Bind("LevelLoading", "filePath", Paths.GameRootPath + @"\level_mods.lvl", "The level to load.");
            configSkipIntro = Config.Bind("GameLoading", "skipIntro", true, "Whether to skip the intro upon loading a new file.\nRequired if the area near the intro cutscene is modified.");
            configDebugMode = Config.Bind("DebugMode", "debugMode", false, "Whether a new file should be created with every room unlocked to be moved to via the map.");
            configZoneFilePath = Config.Bind("LevelLoading", "zoneFilePath", Paths.GameRootPath + @"\zones.zone", "The file containing zone modifications to load.");
			//configShowMapKey = Config.Bind("Keybindings", "showMapKey", "\\", "Keybinding to show the screen shown after activating a power node.");
            Harmony harmony = new("SolasLevelEditor");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            instance = this;
			OnKeyPress += OnMapKeyPressed;

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
			extraData[typeof(EmitterReceiver)] = [];
			extraData[typeof(EmitterController)] = [];

			//var decompiler = new CSharpDecompiler(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"), new DecompilerSettings());
			//gameSource = decompiler.DecompileWholeModuleAsString();

#if CUSTOMPIECES
            CustomPieces.Add(typeof(TextController));
            var modsPath = Paths.PluginPath;
            logger.LogInfo("Mods path " + modsPath + " exists: " + Directory.Exists(modsPath));
            if (!Directory.Exists(modsPath))
                Directory.CreateDirectory(modsPath);
            foreach (var file in new DirectoryInfo(modsPath).GetFiles("*.dll", SearchOption.AllDirectories))
            {
                var asm = Assembly.LoadFrom(file.FullName);
                var types = asm.GetTypes();
                var customPieceTypes = types.Where((Type type) => type.BaseType == typeof(CustomController) && type.GetCustomAttribute<CustomPieceAttribute>() is not null);
                if (!customPieceTypes.Any())
                    continue;
                foreach (var type in customPieceTypes)
                {
                    CustomPieces.Add(type);
                }
            }
#endif
        }

		void OnDisable()
		{
			OnKeyPress -= OnMapKeyPressed;
		}

        public static Mesh MakeLine(float x1, float y1, float x2, float y2, float thickness, Mesh mesh = null)
        {
			mesh = mesh != null ? mesh : new Mesh();

            float angle = 90 * Mathf.Deg2Rad + Mathf.Atan2(y2 - y1, x2 - x1);
            float xOffset = Mathf.Cos(angle) * thickness;
            float yOffset = Mathf.Sin(angle) * thickness;

            Vector3[] vertices =
            [
                new Vector3(x1 - xOffset, y1 - yOffset, 0),
                new Vector3(x1 + xOffset, y1 + yOffset, 0),
                new Vector3(x2 + xOffset, y2 + yOffset, 0),
                new Vector3(x2 - xOffset, y2 - yOffset, 0),
            ];

            mesh.vertices = vertices;
            mesh.triangles = [0, 1, 2, 2, 3, 0];
            mesh.normals = [-Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward];
            mesh.uv = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
            mesh.UploadMeshData(false);

			return mesh;
        }

        public static Mesh MakeLine(Vector2 p1, Vector2 p2, float thickness) => MakeLine(p1.x, p1.y, p2.x, p2.y, thickness);

        public static Mesh MakeBox(Vector2 bl, Vector2 tl, Vector2 tr, Vector2 br, float thickness)
        {
            Mesh mesh = new();

            mesh.CombineMeshes([new() { mesh = MakeLine(bl - new Vector2(0, thickness), tl + new Vector2(0, thickness), thickness) },
				new() { mesh = MakeLine(tl - new Vector2(thickness, 0), tr + new Vector2(thickness, 0), thickness) },
				new() { mesh = MakeLine(tr + new Vector2(0, thickness), br - new Vector2(0, thickness), thickness) },
				new() { mesh = MakeLine(br + new Vector2(thickness, 0), bl - new Vector2(thickness, 0), thickness) }], true, false);
			mesh.UploadMeshData(false);
            return mesh;
        }

        public static GameObject MakeControllerBase<T>() where T : MonoBehaviour
        {
            var go = new GameObject();
            go.AddComponent<T>();
            return go;
        }

        void Update()
		{
			if (Input.anyKeyDown)
			{
				OnKeyPress?.Invoke(this, new() { keyCodes = Input.inputString, gc = gc });
			}
			if (gc == null)
                return;
            var controller = AccessTools.Field(typeof(GameController), "selectedInteractable").GetValue(gc) as InteractableController;
            var clickState = AccessTools.Field(typeof(GameController), "currentClickState").GetValue(gc) as int?;
            switch (controller)
            {
                
            }
        }

        public void GameStart()
        {
            GameStarted = true;
            logger.LogInfo("Game has been started");
            RotateQuad = gc.levelBuilder.MirrorPiece.GetComponent<MirrorController>().RotateQuad;
            MoveQuad = gc.levelBuilder.MirrorPiece.GetComponent<MirrorController>().MoveQuad;
			{
				var miniCannon = gc.levelBuilder.MiniCannonPiece.GetComponent<MiniCannonController>();
				OrigMiniCannonColors = new Material[3];
				for (int i = 0; i < 3; i++)
				{
					OrigMiniCannonColors[i] = Instantiate(miniCannon.Rings[i].material);
				}
			}
            logger.LogInfo("Camera is at " + Camera.allCameras[0].transform.position);
			OnGameStart?.Invoke(this, gc);
        }

		public void OnMapKeyPressed(object sender, OnKeyPressEventArgs args)
		{
			//var key = args.keyCodes.ToCharArray();
			//if (!key.Contains(configShowMapKey.Value.Single()))
			//{
			//	return;
			//}
			//var gc = args.gc;
			//if (gc is null)
			//{
			//	return;
			//}
			//if (!inMapScreen)
			//{
			//	inMapScreen = true;
			//	var worldBuilder = AccessTools.Field(typeof(GameController), "worldBuilder").GetValue(gc) as MeshBuilder;
			//	var pauseController = AccessTools.Field(typeof(GameController), "pauseController").GetValue(gc) as PauseController;
			//	var pulseLocations = AccessTools.Field(typeof(GameController), "pulseLocations").GetValue(gc) as List<Location>;
			//	StartCoroutine(new RevCompPatch_GameControllerShowWorld());
			//}
		}

        public static void SwitchVal<T>(T value, Dictionary<T, Action> cases, Action defaultAction = null)
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
							int frequency = 0;
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
									frequency = int.Parse(args[3]);
									controllerType = typeof(EmitterController);
									extraData = frequency;
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
									frequency = int.Parse(args[2]);
									controllerType = typeof(EmitterController);
									extraData = frequency;
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
                            else if (extraData is not null)
                            {
                                Plugin.extraData.TryAddValue(controllerType, []);
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

	//public class RevCompPatch_GameControllerShowWorld : IEnumerator
	//{
	//	public int state;
	//	public

	//	object current;

	//	public object Current => current;

	//	public void Reset()
	//	{
	//		throw new NotImplementedException();
	//	}

	//	bool IEnumerator.MoveNext()
	//	{
	//		IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
	//		{
	//			var code = Plugin.gameSource;
	//			var showWorldIndex = code.IndexOf("private IEnumerator ShowWorld(int nodeActivated)");
	//			var startOfMethod = code.IndexOf("GlobalVariables", showWorldIndex);
	//			var endOfDesiredPortion = code.IndexOf("createNewPulses();", showWorldIndex) + "createNewPulses();".Length;
	//			var desiredPortion = code.Substring(startOfMethod, endOfDesiredPortion - startOfMethod);
	//			var provider = new CSharpCodeProvider();
	//			var newCode = $$"""
	//				public class Temp
	//				{
	//					public static int nodeActivated;
	//					public static PauseIconController pauseIconController;
	//					public static HintIconController hintIconController;
	//					public static PauseIconController helpIconController;
	//					public static TimeTravelUIController fastForwardIconController;
	//					public static TimeTravelUIController undoIconController;

	//					public static GameObject WishlistURLIconGo;
	//					public static WishlistURLController wishlistURLController;
	//					public static GameObject saveIconGO;
	//					public static SaveIconController saveIconController;
	//					public static AudioController ac;

	//					public static GameObject pauseIconGO;
	//					public static GameObject hintIconGO;
	//					public static GameObject helpIconGO;
	//					public static GameObject fastForwardIconGO;
	//					public static GameObject undoIconGO;

	//					public static GameObject LeftFog;
	//					public static GameObject RightFog;

	//					public static bool PulseGoo;

	//					public static bool gameStarted;

	//					public static MeshBuilder worldBuilder;
	//					public void Method()
	//					{
	//						{{code}}
	//					}

	//					public static void StartCoroutine(IEnumerator routine) {}
	//					public static void moveAllPulses() {}
	//					public static void createNewPulses() {}
	//				}
	//				""";
	//			var compiled = provider.CompileAssemblyFromSource(new CompilerParameters(), newCode);
	//			foreach (var error in compiled.Errors)
	//			{
	//				Plugin.logger.LogError("Error recompiling GameControllerShowWorld.MoveNext: " + error);
	//				return instrs;
	//			}
	//			var newMethod = compiled.CompiledAssembly.GetTypes()[0].GetMethod("Method");
	//			var newInstrsEarly = newMethod.GetInstructions();
	//			var newInstrs = new List<CodeInstruction>();
	//			foreach (var instr in newInstrsEarly)
	//			{
	//				newInstrs.Add(new CodeInstruction(instr.OpCode, instr.Operand));
	//			}
	//			return newInstrs.TranspileEnumerator<RevCompPatch_GameControllerShowWorld>();
	//		}
	//		_ = Transpiler(null);
	//		return false;
	//	}
	//}

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
            matcher.End();
            matcher.Advance(-3);
            matcher.Insert(
                new CodeInstruction(Ldarg_1),
                new CodeInstruction(Ldarg_2),
                new CodeInstruction(Call, AccessTools.Method(typeof(Patch_checkForPulseToPieceCollision), "Temp2")));
            return matcher.InstructionEnumeration();
        }

        public static int Temp(int x, int y)
        {
            var controller = (NodeDoorController)GetInteractableControllerAt(x, y);
            return (int)AccessTools.Field(typeof(NodeDoorController), "NodeNumber").GetValue(controller);
        }

        public static byte Temp2(byte pulse, int x, int y)
        {
            var controller = GetInteractableControllerAt(x, y);
            if (controller is IPulseInteractable modifier and CustomController)
            {
                modifier.HandlePulseCollision(ref pulse);
            }
            return pulse;
        }
    }

    [HarmonyPatch(typeof(GameController), "BuildLevel")]
    public class Patch_BuildLevel
    {
        public static void Prefix(ref int[] level, GameController __instance)
        {
            Plugin.instance.GameStart();
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
        public static void Prefix(GameController __instance)
        {
            Plugin.gc = __instance;
        }

        public static void Postfix(GameController __instance)
        {
            ((GameObject)AccessTools.Field(typeof(GameController), "undoIconGO").GetValue(__instance)).SetActive(true);
            ((GameObject)AccessTools.Field(typeof(GameController), "fastForwardIconGO").GetValue(__instance)).SetActive(true);
            ((GameObject)AccessTools.Field(typeof(GameController), "helpIconGO").GetValue(__instance)).SetActive(true);
            CurrentGamePhase = 4;
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
            EnableInteractablesAtScreen(3, 16, true);
            FadeInAllPiecesAtScreen(3, 16);
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
            /*AccessTools.Method(typeof(InteractableController), "AddPieceToFade").Invoke(__instance, [
                ((GameObject)AccessTools.Field(typeof(TeleportController), "InnerRing").GetValue(__instance)).GetComponent<MeshRenderer>()
                ]);
            AccessTools.Method(typeof(InteractableController), "AddPieceToFade").Invoke(__instance, [
                ((GameObject)AccessTools.Field(typeof(TeleportController), "OuterRing").GetValue(__instance)).GetComponent<MeshRenderer>()
                ]);*/
        }
    }

    [HarmonyPatch(typeof(EmitterController), nameof(EmitterController.SetupPiece))]
    public class Patch_EmitterControllerSetupPiece
    {
        public static void Prefix(ref bool drag, int tileX, int tileY, EmitterController __instance)
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

        public static void Postfix(EmitterController __instance, int tileX, int tileY, bool drag)
        {
            __instance.BaseLocation = new Vector3(tileX, -tileY, __instance.BaseLocation.z);
            __instance.VisibleLocation = __instance.BaseLocation;
            var moveObject = GameObject.Instantiate(Plugin.MoveQuad, __instance.transform);
            moveObject.transform.position = new(__instance.transform.position.x, __instance.transform.position.y + 0.125f, moveObject.transform.position.z);
            moveObject.SetActive(drag);
            /*AccessTools.Method(typeof(InteractableController), "AddPieceToFade").Invoke(__instance, [
                ((GameObject)AccessTools.Field(typeof(EmitterController), "EmitterQuads").GetValue(__instance)).GetComponent<MeshRenderer>()
                ]);
            AccessTools.Method(typeof(InteractableController), "AddPieceToFade").Invoke(__instance, [
                ((GameObject)AccessTools.Field(typeof(EmitterController), "ReceiverQuads").GetValue(__instance)).GetComponent<MeshRenderer>()
                ]);*/
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
				var emitter = (EmitterReceiver)typeof(EmitterController).GetPrivateField("Emitter", ec);
				emitter.X = tileX;
                emitter.Y = tileY;

				if (Plugin.extraData[typeof(EmitterController)].TryGetValue(___mouseStartLoc, out var freq))
				{
					Plugin.extraData[typeof(EmitterController)][new(tileX, tileY)] = freq;
				}

				if (Plugin.extraData[typeof(EmitterReceiver)].TryGetValue(___mouseStartLoc, out var beat))
				{
					Plugin.extraData[typeof(EmitterReceiver)][new(tileX, tileY)] = beat;
				}
			}
            else if (___selectedInteractable is TeleportController)
            {
#pragma warning disable
                var oldID = Plugin.teleporters[___mouseStartLoc.x, ___mouseStartLoc.y];
                Plugin.teleporters[___mouseStartLoc.x, ___mouseStartLoc.y] = null;
#pragma warning restore
                Plugin.teleporters[tileX, tileY] = oldID;
            }
			else
			{
				if (!Plugin.extraData.ContainsKey(___selectedInteractable.GetType()))
				{
					Plugin.extraData[___selectedInteractable.GetType()] = [];
				}
				if (Plugin.extraData[___selectedInteractable.GetType()].TryGetValue(___mouseStartLoc, out var data))
				{
					Plugin.extraData[___selectedInteractable.GetType()][new(tileX, tileY)] = data;
				}
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

#if CUSTOMPIECES
	[HarmonyPatch]
    public class RevPatch_WallControllerUpdate
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(WallController), "Update")]
        public static void Update(InteractableController instance)
        {

        }
    }
#endif

#if CUSTOMPIECES
	[HarmonyPatch(typeof(MirrorController), "AnimateToPosition0", MethodType.Enumerator)]
    public class RevPatch_MirrorControllerAnimateToPosition0
    {
        [HarmonyReversePatch]
        public static bool AnimateToPosition0(CustomController.RotatePieceEnumerator instance)
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
				var instrs = new CodeMatcher(instructions)
					.MatchForward(false, new CodeMatch(Call))
					.Advance(-3)
					.SetAndAdvance(Nop, null)
					.SetAndAdvance(Nop, null)
					.SetAndAdvance(Nop, null)
					.SetAndAdvance(Nop, null)
					.InstructionEnumeration().ToArray();

				for (int i = 0; i < instrs.Length; i++)
                {
					var instr = instrs[i];
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
									if (((FieldInfo)instrs[i + 1].operand).Name == "RotateGroup")
									{
										newInstr.opcode = Nop;
										newInstr.operand = null;
										break;
									}
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.instance)); break;
                                case "<rotation>5__1":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.rotation)); break;
                                case "<timeTaken>5__2":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.timeTaken)); break;
                                case "<z>5__3":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.z)); break;
                                case "RotateGroup":
                                    newInstr.operand = AccessTools.Field(typeof(CustomController.RotatePieceEnumerator), nameof(CustomController.RotatePieceEnumerator.go)); break;
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

#if CUSTOMPIECES
	[HarmonyPatch(typeof(PrismController), nameof(PrismController.PulseHit))]
	public class RevPatch_PrismControllerPulseHit
	{
		[HarmonyReversePatch]
		public static void ScalePiece(InteractableController instance, float shrinkBy)
		{
			IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs) => instrs.Manipulator(
				instr => instr.opcode == Ldc_R4,
				instr => { instr.opcode = Ldarg_1; instr.operand = null; }
				).Manipulator(
				instr => instr.operand is FieldInfo fi && fi == AccessTools.Field(typeof(PrismController), "startSize"),
				instr => { instr.operand = AccessTools.Field(typeof(CustomController), nameof(CustomController.startSize)); }
				);

			_ = Transpiler(null);
		}
	}
#endif

#if CUSTOMPIECES
	[HarmonyPatch(typeof(PrismController), "Update")]
	public class RevPatch_PrismControllerUpdate
	{
		[HarmonyReversePatch]
		public static void UpdateScale(InteractableController instance)
		{
			IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs) => instrs.Manipulator(
				instr => instr.operand is FieldInfo fi && fi == AccessTools.Field(typeof(PrismController), "startSize"),
				instr => { instr.operand = AccessTools.Field(typeof(CustomController), nameof(CustomController.startSize)); }
				);

			_ = Transpiler(null);
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
                .Advance(4)
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
            var template = new GameObject(controller.Name, controller);
            Plugin.logger.LogInfo("Attempting creation of pieceType " + pieceType);
            RevPatch_LevelBuilderBuildLevelASync.CreateControllerTemplate(lb, template, controller, x, y, pieceType);
#endif
        }
    }

#if CUSTOMPIECES
    [HarmonyPatch(typeof(LevelBuilder), "BuildLevelASync", MethodType.Enumerator)]
    public class RevPatch_LevelBuilderBuildLevelASync
    {
        [HarmonyReversePatch]
        public static void CreateControllerTemplate(LevelBuilder lb, GameObject template, Type controller, int x, int y, int pieceType)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var goLoc = generator.DeclareLocal(typeof(GameObject));
                var ccLoc = generator.DeclareLocal(typeof(CustomController));
                var matcher = new CodeMatcher(instructions.MethodReplacer(AccessTools.Method(typeof(GameObject), nameof(GameObject.GetComponent), [], [typeof(SquiggleController)]),
                    AccessTools.Method(typeof(GameObject), nameof(GameObject.GetComponent), [typeof(Type)])));
                matcher.MatchForward(true, new CodeMatch(Nop), new CodeMatch(Ldarg_0), new CodeMatch(Ldarg_0));
                matcher.MatchForward(true, new CodeMatch(Nop), new CodeMatch(Ldarg_0), new CodeMatch(Ldarg_0));
                matcher.MatchForward(true, new CodeMatch(Nop), new CodeMatch(Ldarg_0), new CodeMatch(Ldarg_0));
                matcher.MatchForward(false, new CodeMatch(Nop), new CodeMatch(Ldarg_0), new CodeMatch(Ldarg_0));
                matcher.RemoveInstructionsInRange(0, matcher.Pos - 1);
                matcher = new CodeMatcher(matcher.InstructionEnumeration().Take(51));
                matcher.Start();
                matcher.MatchForward(false, new CodeMatch(Ldarg_0));
                matcher.Repeat((matcher) => { matcher.SetOpcodeAndAdvance(Nop); matcher.Advance(-1); matcher.SetOperandAndAdvance(null); });
                matcher.Start();
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.SetAndAdvance(Nop, null).Set(Ldarg_1, null);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.SetAndAdvance(Ldarg_3, null).Advance(2).Set(Ldarg_S, 4);
                matcher.MatchForward(false, new CodeMatch(Stfld));
                matcher.Set(Stloc_S, goLoc);
                matcher.Advance(2);
                matcher.Set(Ldloc_S, goLoc);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldarg_0, null).Advance(2);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldloc_S, goLoc);
                matcher.Advance(1);
                matcher.InsertAndAdvance(new CodeInstruction(Ldarg_2));
                matcher.Advance(1);
                matcher.InsertAndAdvance(new CodeInstruction(Castclass, typeof(CustomController)));
                matcher.Set(Stloc_S, ccLoc);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldloc_S, ccLoc);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldarg_3, null);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldarg_S, 4);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldarg_S, 5);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldarg_3, null);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldarg_S, 4);
                matcher.MatchForward(false, new CodeMatch(Ldfld));
                matcher.Set(Ldloc_S, ccLoc);
                matcher.End();
                matcher.Advance(1);
                matcher.Insert(new CodeInstruction(Ret));

                var instrs = matcher.InstructionEnumeration();
                return instrs;
            }

            _ = Transpiler(null, null);
        }
    }
#endif

#if CUSTOMPIECES
	[HarmonyPatch(typeof(MirrorController), nameof(MirrorController.ClickPiece))]
	public class RevPatch_MirrorControllerClickPiece
	{
		[HarmonyReversePatch]
		public static void StopCoroutineSafe(CustomController instance, Coroutine coroutine)
		{
			IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var matcher = new CodeMatcher(instructions);
				matcher.MatchForward(false, [new(Call)]);
				matcher.RemoveInstructionsInRange(matcher.Pos + 2, matcher.Length - 1);
				matcher.Start();
				matcher.MatchForward(false, [new(Ldnull)]);
				matcher.RemoveInstructions(4);
				var label = (IL.Label)matcher.Instruction.operand;
				matcher.End();
				matcher.SetInstruction(new(Nop, null) { labels = [label] });
				matcher.Start();
				matcher.MatchForward(false, [new(null, AccessTools.Field(typeof(MirrorController), "rotateCoroutine"))]);
				matcher.Repeat((matcher) =>
				{
					matcher.Advance(-1);
					matcher.RemoveInstruction();
					matcher.SetInstruction(new(Ldarg_1));
				});
				matcher.End();
				matcher.Insert([new(Ret)]);
				return matcher.InstructionEnumeration();
			}

			_ = Transpiler(null);
		}
	}
#endif

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
                ((GlitchController)GetInteractableControllerAt(loc.x, loc.y))?.DisableGlitch();
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

		public Color origColor;

		public Material origMat;

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
				var matcher = new CodeMatcher(instructions.TranspileEnumerator<RevPatch_DisableRingEnum>());
                matcher.MatchForward(false, new CodeMatch(Sub)).SetInstruction(new(Call, AccessTools.Method(typeof(RevPatch_DisableRingEnum), nameof(RSub))));
                matcher.MatchForward(false, new CodeMatch(Sub)).SetInstruction(new(Call, AccessTools.Method(typeof(RevPatch_DisableRingEnum), nameof(RSub))));
                matcher.MatchForward(false, new CodeMatch(Sub)).SetInstruction(new(Call, AccessTools.Method(typeof(RevPatch_DisableRingEnum), nameof(RSub))));
				matcher.MatchForward(false, new CodeMatch(Ldflda, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "initColour")));
				matcher.InsertAndAdvance([
					new(Ldfld, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "instance")),
					new(Ldfld, AccessTools.Field(typeof(MiniCannonController), "RingTargetMaterial")),
					new(Callvirt, AccessTools.PropertyGetter(typeof(Material), nameof(Material.color)))
					]);
				matcher.SetInstruction(new(Nop));
				matcher.MatchForward(false, new CodeMatch(Ldflda, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "initColour")));
				matcher.InsertAndAdvance([
					new(Ldfld, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "instance")),
					new(Ldfld, AccessTools.Field(typeof(MiniCannonController), "RingTargetMaterial")),
					new(Callvirt, AccessTools.PropertyGetter(typeof(Material), nameof(Material.color)))
					]);
				matcher.SetInstruction(new(Nop));
				matcher.MatchForward(false, new CodeMatch(Ldflda, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "initColour")));
				matcher.InsertAndAdvance([
					new(Ldfld, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "instance")),
					new(Ldfld, AccessTools.Field(typeof(MiniCannonController), "RingTargetMaterial")),
					new(Callvirt, AccessTools.PropertyGetter(typeof(Material), nameof(Material.color)))
					]);
				matcher.SetInstruction(new(Nop));
				matcher.MatchForward(false, new CodeMatch(Ldfld, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "instance")));
				matcher.SetInstructionAndAdvance(new(Nop));
				matcher.SetInstruction(new(Ldfld, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "origMat")));
				matcher.MatchForward(false, new CodeMatch(Ldfld, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "instance")));
				matcher.SetInstructionAndAdvance(new(Nop));
				matcher.SetInstruction(new(Ldfld, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "origMat")));
				matcher.Start();
				matcher.MatchForward(false, new CodeMatch(Ldflda, AccessTools.Field(typeof(RevPatch_DisableRingEnum), "initColour")));
				matcher.Repeat((matcher) =>
				{
					matcher.SetOperandAndAdvance(AccessTools.Field(typeof(RevPatch_DisableRingEnum), "origColor"));
				});
				foreach (var instr in matcher.InstructionEnumeration())
				{
					FileLog.Log(instr.ToString());
				}
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
        public static bool Prefix(MiniCannonController __instance, ref int __result, ref int ___shotNumber, List<Location> ___foundLocations)
        {
            __result = __instance.TileValue;
            if (___shotNumber <= 0)
                return false;
            Plugin.logger.LogInfo("Resetting mini glitch cannon at " + __instance.TileX + ", " + __instance.TileY + " to shot number " + (___shotNumber - 1));
            ___shotNumber--;
            __instance.StartCoroutine(new RevPatch_DisableRingEnum(0)
			{
				instance = __instance,
				ring = __instance.Rings[___shotNumber],
				totalTime = 1f, origColor = Plugin.OrigMiniCannonColors[___shotNumber].color,
				origMat = Plugin.OrigMiniCannonColors[___shotNumber] });
			var lastGlitchLocation = ___foundLocations.Last();
			___foundLocations.Remove(lastGlitchLocation);
			RevPatch_LevelBuilderBuildLevelASync.CreateControllerTemplate(Plugin.gc.levelBuilder, Plugin.gc.levelBuilder.GlitchPiece, typeof(GlitchController), lastGlitchLocation.x, lastGlitchLocation.y, 6);
            return false;
        }
    }

	[HarmonyPatch(typeof(GameController), "Update")]
	public class Patch_GameControllerUpdate
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator gen)
		{
			var matcher = new CodeMatcher(instructions);

			matcher.MatchForward(false, new CodeMatch(null, AccessTools.Field(typeof(GameController), "isQuitting")));
			matcher.Advance(8);

			matcher.MatchForward(false, new CodeMatch(null, AccessTools.Field(typeof(GameController), "timeSinceLastBar")));
			matcher.Advance(1);
			matcher.InsertAndAdvance([
				new(Ldarg_0),
				new(Call, AccessTools.Method(typeof(GameController), "createNewPulses"))
				]);
			matcher.Advance(9);
			matcher.SetInstructionAndAdvance(new(Nop));
			matcher.SetInstructionAndAdvance(new(Nop));
			return matcher.InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(GameController), "createNewPulses")]
	public class Patch_GameControllerCreateNewPulses
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var matcher = new CodeMatcher(instructions);
			matcher.MatchForward(false, new CodeMatch(Callvirt, AccessTools.PropertyGetter(typeof(EmitterReceiver), nameof(EmitterReceiver.CurrentPower))));
			matcher.Advance(4);
			matcher.SetInstructionAndAdvance(new(Ldloc_2));
			matcher.SetInstructionAndAdvance(new(Call, AccessTools.Method(typeof(Patch_GameControllerCreateNewPulses), nameof(Temp))));
			matcher.InsertAndAdvance([
				new(And)
				]);
			return matcher.InstructionEnumeration();
		}

		public static bool Temp(EmitterReceiver emitter)
		{
			bool result = false;
			if (!Plugin.extraData[typeof(EmitterReceiver)].TryGetValue(new(emitter.X, emitter.Y), out _))
			{
				Plugin.extraData[typeof(EmitterReceiver)][new(emitter.X, emitter.Y)] = 0;
			}
			if (Plugin.extraData[typeof(EmitterReceiver)][new(emitter.X, emitter.Y)] as int? == (Plugin.extraData[typeof(EmitterController)].TryGetValue(new(emitter.X, emitter.Y), out var temp) ? (temp as int?) - 1 : Plugin.DEFAULT_FREQUENCY - 1))
			{
				result = true;
			}
			Plugin.extraData[typeof(EmitterReceiver)][new(emitter.X, emitter.Y)] = (Plugin.extraData[typeof(EmitterReceiver)][new(emitter.X, emitter.Y)] as int?) + 1;
			Plugin.extraData[typeof(EmitterReceiver)][new(emitter.X, emitter.Y)] = (Plugin.extraData[typeof(EmitterReceiver)][new(emitter.X, emitter.Y)] as int?) % ((Plugin.extraData[typeof(EmitterController)].TryGetValue(new(emitter.X, emitter.Y), out var temp2) ? temp2 : Plugin.DEFAULT_FREQUENCY) as int?);
			return result;
		}
	}

#if false
	[HarmonyPatch(typeof(InteractableController), nameof(InteractableController.ActivateInstant))]
	public class Patch_InteractableControllerActivateInstant
	{
		public static void Prefix(InteractableController __instance)
		{
			Plugin.logger.LogInfo(__instance.GetType());
		}
	}
#endif

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
        /// A transpiler that helps with reverse patching enumerator methods.
        /// </summary>
        /// <typeparam name="TEnumerator">The new enumerator type to use. This type's fields should match in name and type with the original method's locals and arguments, except that the field <code>&lt;&gt;4__this</code> should be matched with a field named <code>instance</code>.</typeparam>
        /// <param name="instrs">An enumerable of the instructions of the enumerator's MoveNext method.</param>
        /// <returns>An enumerable containing the instructions of the MoveNext method, with field access instructions replaced to access the new enumerator's fields.</returns>
        public static IEnumerable<CodeInstruction> TranspileEnumerator<TEnumerator>(this IEnumerable<CodeInstruction> instrs) where TEnumerator : IEnumerator
        {
            foreach (var instr in instrs)
            {
                var newInstr = new CodeInstruction(instr);
				var locals = AccessTools.GetDeclaredFields(typeof(TEnumerator));
                if (instr.operand is null or not FieldInfo)
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
                                newInstr.operand = local;
                            }
                            else if (fieldName == local.Name)
                            {
                                newInstr.operand = local;
                            }
                        }
                        break;
                }
                yield return newInstr;
            }
        }

        public static bool TryAddValue<T1, T2>(this Dictionary<T1, T2> dict, T1 key, T2 value)
        {
            bool result;
            if (result = !dict.TryGetValue(key, out _))
                dict.Add(key, value);
            return result;
        }
    }
}

#region FixRecords
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit { }
}
#endregion

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
        public GameObject rotateObject;
        public GameObject moveObject;
		public Vector3 startSize = Vector3.one;

        public override void CheatComplete() { }
        public override void ActivatePiece()
        {
            StartCoroutine(ActivatePieceCR());
        }
        public override void DeactivatePiece()
        {
            StartCoroutine(DeactivatePieceCR());
        }

		public void StopCoroutineSafe(Coroutine coroutine)
		{
			RevPatch_MirrorControllerClickPiece.StopCoroutineSafe(this, coroutine);
		}

        public void Rotate(float angle, GameObject[] GOs)
        {
            foreach (var obj in GOs)
            {
				var enumerator = new RotatePieceEnumerator(angle, 0)
				{
					instance = this,
					go = obj
				};
				StartCoroutine(enumerator);
            }
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
			public GameObject go;

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
                return RevPatch_MirrorControllerAnimateToPosition0.AnimateToPosition0(this);
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }

        public abstract Tuple<int, dynamic> Global_MakeTile(string[] args);
        public abstract string Global_GetTileName();

        public void SetupObjects()
        {
            rotateObject = Instantiate(Plugin.RotateQuad, transform);
            moveObject = Instantiate(Plugin.MoveQuad, transform);
        }

		public override int ClickPiece()
		{
			return -1;
		}
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CustomPieceAttribute : Attribute
    {

    }

    namespace BuiltInPieces
    {
        public class TextController : CustomController
        {
            public record Data(string Text, string Font, int Size);
            string text;
			string font;
			int size;

            public GameObject go;

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
                Plugin.logger.LogInfo("Text setup piece called");
                SetupObjects();
                go = new();
                if (!go.TryGetComponent<MeshRenderer>(out _))
                    go.AddComponent<MeshRenderer>();
                var mr = go.GetComponent<MeshRenderer>();
                mr.enabled = true;
                var text = go.AddComponent<TextMesh>() ?? go.GetComponent<TextMesh>();
                text.font = Font.GetDefault();
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.fontSize = 12;
                text.color = Color.white;
                text.characterSize = 1;
                text.lineSpacing = 1;
                rotateTime = 0.5f;
                //mr.material = text.font.material;

                TileX = tileX;
                TileY = tileY;
                Drag = false;
                Click = false;
                TileValue = tileValue;
                PieceType = pieceType;
                {
                    var temp = transform.position;
                    temp.z = -1;
                    transform.position = temp;
                }
                BaseLocation = transform.position;
                VisibleLocation = BaseLocation;
                this.text = ((Data)Plugin.extraData[typeof(TextController)][new(TileX, TileY)]).Text;
				font = ((Data)Plugin.extraData[typeof(TextController)][new(TileX, TileY)]).Font;
				size = ((Data)Plugin.extraData[typeof(TextController)][new(TileX, TileY)]).Size;
				DeactivateInstant();
                go.SetActive(true);
                moveObject.SetActive(false);
                rotateObject.SetActive(false);
            }

            protected override void Update()
            {
                ((MoveController)RevPatch_WallControllerUpdate.Update)(this);
                if (go is not null)
                {
					var textMesh = go.GetComponent<TextMesh>();
                    textMesh.text = text;
					textMesh.fontSize = size;
					textMesh.font = new Font(font);
                    go.transform.position = transform.position;
                }
            }

            public void SetText(string text)
            {
                this.text = text;
            }

            public override Tuple<int, object> Global_MakeTile(string[] args)
            {
                return new(-2, new Data(args[0], args[1], int.Parse(args[2])));
            }

            public override string Global_GetTileName()
            {
                return "text";
            }
        }
    }
}
#endif