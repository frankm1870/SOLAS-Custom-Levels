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
using SOLASCustomLevels;
using System.Linq;
using MonoMod.Utils;
using System.Text;
using static System.Reflection.Emit.OpCodes;
using System.Text.RegularExpressions;
using static GlobalVariables;
using System.ComponentModel;
using UnityEngine.SceneManagement;
using Vectrosity;
using System.Runtime.InteropServices;
using System.Collections.Specialized;
using System.Runtime.Serialization.Formatters.Binary;

#if CUSTOMPIECES
using CustomPieces;
using CustomPieces.BuiltInPieces;
#endif

namespace SOLASCustomLevels
{
	public delegate void MoveController(InteractableController controller);
	public delegate void FadeController(InteractableController controller, bool fade);

	public class OnKeyPressEventArgs : EventArgs
	{
		public string keyCodes;
		public GameController gc;
	}

	[Serializable]
	public record struct ExtendedSaveData
	{
		public Dictionary<Type, Dictionary<Location, object>> tileData;
		public bool[,] moveableTiles;
		public int?[,] teleporters;
		public Dictionary<int, string> codes;
		public Dictionary<int, Location> doorIDs;
		public Dictionary<int, Location> nodeIDs;
		public Dictionary<Location, GlitchController> destroyedGlitches;
		public int[,] zones;
		public Tuple<string, List<Location>, int, int>[,] comboLocks;
	}

	[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
	[BepInProcess("SOLAS 128.exe")]
	public class Plugin : BaseUnityPlugin
	{
		public const int DEFAULT_FREQUENCY = 4;
		public static readonly string ASSETS_PATH = Path.Combine(Paths.GameRootPath, "Assets");

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
		internal static GameObject editorButton;
		public static LoadController loadController;
		public static bool inEditor;
		public static bool editorLoaded;

		public static ExtendedSaveData?[] extraSaveData = [null, null, null];

		static int[,] backupMenuMap;

		//public static Dictionary<int, Tuple<Location, List<Location>>> bossPhases = new();
		//public static Type AnimateTo0Enumerator = typeof(MirrorController).GetNestedType("\'<AnimateToPosition0>d__19\'", BindingFlags.NonPublic);

		public static Dictionary<Type, dynamic> CustomPieces = [];

		private void Awake()
		{
			// Plugin startup logic
			backupMenuMap = (int[,])Levels.MenuMap.Clone();

			Levels.MenuMap[8, 12] = -2;

			FileLog.Reset();
			logger = Logger;
			Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
			AssetBundle.LoadFromFile(Path.Combine(ASSETS_PATH, "editor scene"));

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
			CustomPieces.Add(typeof(TextController), null);
			var modsPath = Paths.PluginPath;
			logger.LogInfo("Mods path " + modsPath + " exists: " + Directory.Exists(modsPath));
			if (!Directory.Exists(modsPath))
				Directory.CreateDirectory(modsPath);
			foreach (var file in new DirectoryInfo(modsPath).GetFiles("*.dll", SearchOption.AllDirectories))
			{
				if (File.Exists(Path.GetFileNameWithoutExtension(file.Name) + ".json"))
				{

				}
				var asm = Assembly.LoadFrom(file.FullName);
				var types = asm.GetTypes();
				var customPieceTypes = types.Where(type => type.BaseType == typeof(CustomController) && type.GetCustomAttribute<CustomPieceAttribute>() is not null);
				if (!customPieceTypes.Any())
					continue;
				foreach (var type in customPieceTypes)
				{
					CustomPieces.Add(type, null);
				}
			}
#endif
		}

		void OnDisable()
		{
			Logger.LogInfo("Plugin disabled");
			OnKeyPress -= OnMapKeyPressed;
			Levels.MenuMap = (int[,])backupMenuMap.Clone();
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

		public static Mesh MakeQuad(Vector2 bl, Vector2 tl, Vector2 tr, Vector2 br)
		{
			Mesh mesh = new();
			mesh.vertices = [bl, tl, tr, br];
			mesh.triangles = [0, 1, 2, 2, 3, 0];
			mesh.normals = [-Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward];
			mesh.uv = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];
			mesh.UploadMeshData(false);

			return mesh;
		}

		public static Mesh MakeTri(Vector2 p1, Vector2 p2, Vector2 p3)
		{
			Mesh mesh = new();
			mesh.vertices = [p1, p2, p3];
			mesh.triangles = [0, 1, 2];
			mesh.normals = [-Vector3.forward, -Vector3.forward, -Vector3.forward];
			mesh.uv = [new(0, 0), new(1, 0), new(0, 1)];
			mesh.UploadMeshData(false);

			return mesh;
		}

		public static Mesh MakePolyline(float thickness, params Vector2[] vertices)
		{
			List<CombineInstance> meshes = [];
			for (int i = 0; i < vertices.Length - 1; i++)
			{
				meshes.Add(new() { mesh = MakeLine(vertices[i], vertices[i + 1], thickness) });
			}
			var mesh = new Mesh();
			mesh.CombineMeshes(meshes.ToArray(), true, false);
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
			if (inEditor)
			{
				var scene = SceneManager.GetActiveScene();
				if (scene.name != "Editor")
				{
					return;
				}
				if (editorLoaded == false)
				{
					editorLoaded = true;
					logger.LogInfo("Editor loaded");
					var loader = new GameObject("Editor Controller");
					loader.AddComponent<EditorController>();
				}
				return;
			}
			editorLoaded = false;
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
			if (!(bool)AccessTools.Field(typeof(GameController), "showFullIntro").GetValue(gc))
			{
				var data = extraSaveData[STATE_NUMBER].Value;
				teleporters = data.teleporters;
				modifiedZones = data.zones;
				destroyedGlitches = data.destroyedGlitches;
				moveableTiles = data.moveableTiles;
				extraData = data.tileData;
				codeStorages = data.codes;
				nodeIDs = data.nodeIDs;
				doorIDs = data.doorIDs;
				comboLocks = data.comboLocks;
			}
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
				Dictionary<int, Tuple<string, List<Location>>> comboLocks = new();
				foreach (string line in reader.ReadToEnd().Split('\n'))
				{
					if (line.Trim() != "" && line[0] != '#' && line[0] != '*')
					{
						var nodeID = -1;
						var theseShouldBeMoveable = false;
						var theseShouldHaveID = 0;
						var coords = line.Split('=')[0].Trim().Split(',');
						var replaceWith = line.Split('=')[1].Trim().ToLower();
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
									tileID += (10 * (type / 3)) + (type % 3);
									break;
								case "door":
									tileID = 302;
									type = int.Parse(args[0]);
									state = args[1];
									tileID += (10 * (type / 3)) + (type % 3);
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
							if (combolockID.HasValue)
							{
								Tuple<string, List<Location>, int, int> comboLock = new(comboLocks[combolockID.Value].Item1, comboLocks[combolockID.Value].Item2, combolockPos, combolockID.Value);
								string startingCode = new('-', comboLocks[combolockID.Value].Item1.Length);
								codeStorages[combolockID.Value] = startingCode;
								for (int i = int.Parse(coordsX[0].Trim()); i <= ((coordsX.Length == 2) ? int.Parse(coordsX[1].Trim()) : int.Parse(coordsX[0].Trim())); i++)
								{
									for (int j = int.Parse(coordsY[0].Trim()); j <= ((coordsY.Length == 2) ? int.Parse(coordsY[1].Trim()) : int.Parse(coordsY[0].Trim())); j++)
									{
										var x = int.Parse(coords[0].Trim()) * 14 + i;
										var y = int.Parse(coords[1].Trim()) * 14 + j;
										indices.Add(x + y * 253);
										moveableTiles[x, y] = theseShouldBeMoveable;
										teleporters[x, y] = theseShouldHaveID;
										Plugin.comboLocks[x, y] = comboLock;
									}
								}
							}
							else
							{
								for (int i = int.Parse(coordsX[0].Trim()); i <= ((coordsX.Length == 2) ? int.Parse(coordsX[1].Trim()) : int.Parse(coordsX[0].Trim())); i++)
								{
									for (int j = int.Parse(coordsY[0].Trim()); j <= ((coordsY.Length == 2) ? int.Parse(coordsY[1].Trim()) : int.Parse(coordsY[0].Trim())); j++)
									{
										var x = int.Parse(coords[0].Trim()) * 14 + i;
										var y = int.Parse(coords[1].Trim()) * 14 + j;
										indices.Add(x + y * 253);
										moveableTiles[x, y] = theseShouldBeMoveable;
										teleporters[x, y] = theseShouldHaveID;
									}
								}
							}
							var level = Levels.level1;
							int[] flattenedLevel = new int[level.GetLength(0) * level.GetLength(1)];
							flattenedLevel = GetFlattenedArray(level);
							foreach (int index in indices)
								levelData[index] = tileID != -1 ? tileID : flattenedLevel[index];
						}
						catch (Exception e)
						{
							logger.LogInfo("Could not execute line " + line + ", exception: " + e.ToString());
							continue;
						}
					}
					else if (line.Trim() != "" && line[0] == '*')
					{
						var type = line.Remove(0, 1).Split(' ')[0];
						var args = new List<string>(line.Remove(0, 1).Split(' ').Skip(1));

						switch (type)
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
				foreach (string line in reader.ReadToEnd().Split('\n'))
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
				if (piece.Key.IsSubclassOf(typeof(CustomController)))
				{
					if (((List<int>)AccessTools.Method(piece.Key, "Global_GetTileIDs").Invoke(Activator.CreateInstance(piece.Key), null)).Contains(tileID))
						return piece.Key;
				}
			}
			return null;
		}

		internal static Type GetControllerType(string name)
		{
			foreach (var piece in CustomPieces)
			{
				if (piece.Key.IsSubclassOf(typeof(CustomController)))
				{
					if (name == (string)AccessTools.Method(piece.Key, "Global_GetTileName").Invoke(Activator.CreateInstance(piece.Key), null))
						return piece.Key;
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

	[HarmonyPatch]
	public class EditorController : MonoBehaviour
	{
		public enum ColorEmit
		{
			Red = 1,
			Green = 2,
			Yellow = Red | Green,
			Blue = 4,
			Magenta = Red | Blue,
			Cyan = Green | Blue,
			White = Red | Green | Blue,
			Void = 0
		}
		public enum ColorReceive
		{
			Red = 1,
			Green = 2,
			Yellow = Red | Green,
			Blue = 4,
			Magenta = Red | Blue,
			Cyan = Green | Blue,
			White = Red | Green | Blue,
			Any = 0
		}
		public enum ColorFilter
		{
			Red = 1,
			Green = 2,
			Yellow = Red | Green,
			Blue = 4,
			Magenta = Red | Blue,
			Cyan = Green | Blue,
			White = Red | Green | Blue
		}
		public enum DoorState
		{
			Open,
			Closed
		}
		public Location currentScreen;
		public GameObject cursor;
		public GameObject mouse;
		public VectorLine grid;
		public GameObject gridTransform;
		public GameObject screenLabel;
		public GameObject placingLabel;
		public List<Tuple<GameObject, OrderedDictionary>> placedObjects = [];
		public Dictionary<int, Tuple<string, List<Location>>> comboLocks = [];
		public Material redMaterial = colourMaterials[1];
		public Material greenMaterial = colourMaterials[2];
		public Material blueMaterial = colourMaterials[4];
		public Material moveableMaterial;
		public Material rotateableMaterial;
		public bool inSelectionUI;
		public bool inEditUI;
		public bool inConfirmUnsavedUI;
		public static readonly Dictionary<string, string> basePieces = new()
		{
			["mirror"] = "Mirror",
			["prism"] = "Prism",
			["emitter"] = "Emitter",
			["receiver"] = "Receiver",
			["noplace"] = "Piece blocker",
			["outofbounds"] = "Out of bounds",
			["wall"] = "Wall",
			["glitch"] = "Glitch",
			["fakewall"] = "Fake wall",
			["glitchdestroyer"] = "Glitch cannon",
			["filter"] = "Filter",
			["button"] = "Button",
			["door"] = "Door",
			["corner"] = "Corner",
			["blocker"] = "Blocker",
			["teleporter"] = "Teleporter",
			["powernode"] = "Powernode",
			["nodedoor"] = "Node door",
			["combolock"] = "Combo lock",
			["id"] = "Piece by ID",
#if SELECTION
			["select"] = "Select"
#endif
		};
		public Tuple<GameObject, OrderedDictionary> selected;
		public string currentlyPlacing = "";
		public GameObject selectRoot;
		public GameObject editRoot;
		public bool saved = true;
		public bool keyboardUsed;
		public Vector3 lastMousePos;
#if SELECTION
		public Vector3? selectionStart;
		public Vector3? selectionEnd;
		public GameObject selectionBox = new();
		public List<GameObject> selectedObjects = [];
#endif

		#region UIState
		Vector2 selectionScrollVector = Vector2.zero;
		Vector2 editScrollVector = Vector2.zero;
		Dictionary<int, object> extra = [];
		#endregion

		public void Start()
		{
			var bundle = AssetBundle.LoadFromFile(Path.Combine(Plugin.ASSETS_PATH, "materials"));
			moveableMaterial = (Material)bundle.LoadAsset("assets/materials/moveable.mat");
			rotateableMaterial = (Material)bundle.LoadAsset("assets/materials/rotateable.mat");
			UnityEngine.Cursor.visible = true;
			UnityEngine.Cursor.lockState = CursorLockMode.None;
			UnityEngine.Cursor.SetCursor(null, new(0, 0), CursorMode.Auto);
			Camera.main.backgroundColor = Color.black;
			Camera.main.orthographicSize = 10f;
			screenLabel = new GameObject();
			screenLabel.transform.parent = Camera.main.transform;
			screenLabel.transform.localPosition = new(-7.5f, 8, 10);
			screenLabel.transform.localScale = new(0.2f, 0.2f);
			screenLabel.AddComponent<MeshRenderer>();
			var text = screenLabel.AddComponent<TextMesh>();
			text.text = "Current screen: 0, 0";
			text.color = Color.white;
			text.fontSize = 30;
			text.anchor = TextAnchor.MiddleLeft;
			text.alignment = TextAlignment.Left;
			screenLabel.SetActive(true);
			placingLabel = new();
			placingLabel.transform.parent = Camera.main.transform;
			placingLabel.transform.localPosition = new(-7.5f, -8, 10);
			placingLabel.transform.localScale = new(0.2f, 0.2f);
			placingLabel.AddComponent<MeshRenderer>();
			text = placingLabel.AddComponent<TextMesh>();
			text.text = "Currently placing: None";
			text.color = Color.white;
			text.fontSize = 30;
			text.anchor = TextAnchor.MiddleLeft;
			text.alignment = TextAlignment.Left;
			placingLabel.SetActive(true);
			cursor = new GameObject();
			var mr = cursor.AddComponent<MeshRenderer>();
			var filter = cursor.AddComponent<MeshFilter>();
			filter.mesh = Plugin.MakeBox(new(-0.5f, -0.5f), new(-0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, -0.5f), 0.02f);
			cursor.transform.parent = Camera.main.transform;
			mr.material = colourMaterials[(int)COLOUR.NONE];
			currentScreen = new(0, 0);
			MakeGrid(this);
			grid.color = Color.white;
			grid.drawTransform = gridTransform.transform;
			gridTransform.transform.position = new(0, 0, 15);
			grid.Draw3D();
#if SELECTION
			var selectionMR = selectionBox.AddComponent<MeshRenderer>();
			selectionBox.AddComponent<MeshFilter>();
			selectionMR.material = colourMaterials[(int)COLOUR.WHITE];
#endif
			mouse = new();
			var mouseMR = mouse.AddComponent<MeshRenderer>();
			mouseMR.material = glyphMaterial;
			var mouseMF = mouse.AddComponent<MeshFilter>();
			mouseMF.mesh = new();
			mouseMF.mesh.CombineMeshes([new() { mesh = Plugin.MakeLine(0, 0.1f, 0, -0.1f, 0.02f) }, new() { mesh = Plugin.MakeLine(-0.1f, 0, 0.1f, 0, 0.02f) }], true, false);
		}

		public void OnGUI()
		{
			if (inSelectionUI)
			{
				GUI.Box(new((Screen.width - 400) / 2, (Screen.height - 500) / 2, 400, 500), "Select piece");
				selectionScrollVector = GUI.BeginScrollView(new((Screen.width - 350) / 2, (Screen.height - 450) / 2, 350, 450), selectionScrollVector, new(0, 0, 100, 60 * (Plugin.CustomPieces.Count + 19)));

				foreach (var piece in basePieces)
				{
					if (GUILayout.Button(piece.Value))
					{
						currentlyPlacing = piece.Key;
						inSelectionUI = false;
						return;
					}
				}

				GUI.EndScrollView();
			}
			else if (inEditUI)
			{
				GUI.Box(new((Screen.width - 400) / 2, (Screen.height - 500) / 2, 400, 500), $"Edit {basePieces[selected.Item1.name]}");

				selectionScrollVector = GUI.BeginScrollView(new((Screen.width - 350) / 2, (Screen.height - 350) / 2, 350, 350), selectionScrollVector, new(0, 0, 300, 90 * selected.Item2.Count));

				var enumerator = selected.Item2.GetEnumerator();
				DictionaryEntry pair;
				for (int i = 0; i < selected.Item2.Count; i++)
				{
					enumerator.MoveNext();
					pair = (DictionaryEntry)enumerator.Current;
					if ((string)pair.Key == "Direction")
					{
						continue;
					}
					GUILayout.Label((string)pair.Key);
					switch (pair.Value)
					{
						case string:
							extra[i] = GUILayout.TextField((string)(extra.GetValueSafe(i) ?? pair.Value));
							break;
						case bool:
							extra[i] = GUILayout.Toggle((bool)(extra.GetValueSafe(i) ?? pair.Value), "");
							break;
						case int:
							var temp = GUILayout.TextField((extra.GetValueSafe(i) ?? pair.Value).ToString());
							if (temp == "" || int.TryParse(temp, out _))
							{
								extra[i] = temp;
							}
							break;
						case Enum:
							var enumType = pair.Value.GetType();
							extra[i] = Enum.Parse(enumType, Enum.GetNames(enumType)[GUILayout.SelectionGrid(new List<string>(Enum.GetNames(enumType)).IndexOf(Enum.GetName(enumType, extra.GetValueSafe(i) ?? pair.Value)), Enum.GetNames(enumType), 4, GUILayout.Width(300))]);
							break;
					}
				}

				GUI.EndScrollView();

				if (GUI.Button(new((Screen.width - 350) / 2, (Screen.height - 100) / 2 + 350, 350, 100), "Save") || Input.GetKeyDown(KeyCode.Return))
				{
					for (int i = 0; i < selected.Item2.Count; i++)
					{
						if ((string)selected.Item2.Cast<DictionaryEntry>().ElementAt(i).Key == "Direction")
						{
							continue;
						}
						var value = extra[i];
						extra.Remove(i);
						if (value.ToString() == "" && selected.Item2[i] is int)
						{
							continue;
						}
						selected.Item2[i] = value;
					}
					UpdateMesh(selected.Item1, selected.Item2);
					inEditUI = false;
					saved = false;
					return;
				}
			}
			else if (inConfirmUnsavedUI)
			{
				GUI.Box(new((Screen.width - 400) / 2, (Screen.height - 200) / 2, 400, 200), "Unsaved Changes");
				GUI.Label(new((Screen.width - 400) / 2 + 50, (Screen.height - 200) / 2 + 50, 300, 100), "You have unsaved changes, do you still want to exit?");
				if (GUI.Button(new((Screen.width - 400) / 2 + 50, (Screen.height - 200) / 2 + 160, 300, 30), "Exit"))
				{
					inConfirmUnsavedUI = false;
					SceneManager.LoadScene(0);
					Plugin.inEditor = false;
				}
			}
		}

		public void Update()
		{
			mouse.transform.position = Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 10);
			if (Input.mousePosition != lastMousePos || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
			{
				keyboardUsed = false;
			}
			lastMousePos = Input.mousePosition;
#if SELECTION
			if ((selectedObjects?.Count ?? 0) == 0 && (Input.GetKeyDown(KeyCode.LeftArrow) ||
				Input.GetKeyDown(KeyCode.RightArrow) ||
				Input.GetKeyDown(KeyCode.UpArrow) ||
				Input.GetKeyDown(KeyCode.DownArrow)))
#else
			if (Input.GetKeyDown(KeyCode.LeftArrow) ||
				Input.GetKeyDown(KeyCode.RightArrow) ||
				Input.GetKeyDown(KeyCode.UpArrow) ||
				Input.GetKeyDown(KeyCode.DownArrow))
#endif
			{
				keyboardUsed = true;
#if SELECTION
				selectionStart = null;
				selectionEnd = null;
#endif
			}
			if (keyboardUsed)
			{
				mouse.SetActive(false);
			}
			else
			{
				mouse.SetActive(true);
			}
			if (inEditUI || inSelectionUI || inConfirmUnsavedUI)
			{
				if (Input.GetKeyDown(KeyCode.Escape))
				{
					inEditUI = false;
					inSelectionUI = false;
					inConfirmUnsavedUI = false;
				}
				return;
			}

			if (Input.GetKeyDown(KeyCode.Escape))
			{
				if (saved)
				{
					SceneManager.LoadScene(0);
					Plugin.inEditor = false;
				}
				else
				{
					inConfirmUnsavedUI = true;
				}
				return;
			}

			if (Input.GetKeyDown(KeyCode.Tab))
			{
				inSelectionUI = true;
				goto nothingSelected;
			}

			if (keyboardUsed)
			{
				if (Input.GetKeyDown(KeyCode.UpArrow))
				{
					cursor.transform.localPosition += new Vector3(0, 1, 0);
					if (cursor.transform.localPosition.y > 7)
					{
						cursor.transform.localPosition = new(cursor.transform.localPosition.x, 7, cursor.transform.localPosition.z);
					}
				}
				if (Input.GetKeyDown(KeyCode.DownArrow))
				{
					cursor.transform.localPosition += new Vector3(0, -1, 0);
					if (cursor.transform.localPosition.y < -7)
					{
						cursor.transform.localPosition = new(cursor.transform.localPosition.x, -7, cursor.transform.localPosition.z);
					}
				}
				if (Input.GetKeyDown(KeyCode.LeftArrow))
				{
					cursor.transform.localPosition += new Vector3(-1, 0, 0);
					if (cursor.transform.localPosition.x < -7)
					{
						cursor.transform.localPosition = new(-7, cursor.transform.localPosition.y, cursor.transform.localPosition.z);
					}
				}
				if (Input.GetKeyDown(KeyCode.RightArrow))
				{
					cursor.transform.localPosition += new Vector3(1, 0, 0);
					if (cursor.transform.localPosition.x > 7)
					{
						cursor.transform.localPosition = new(7, cursor.transform.localPosition.y, cursor.transform.localPosition.z);
					}
				}
			}
			else
			{
				cursor.transform.localPosition = new(Mathf.Round(Camera.main.ScreenToWorldPoint(Input.mousePosition).x), Mathf.Round(Camera.main.ScreenToWorldPoint(Input.mousePosition).y), cursor.transform.localPosition.z);
				if (cursor.transform.localPosition.x > 7)
				{
					cursor.transform.localPosition = new(7, cursor.transform.localPosition.y, cursor.transform.localPosition.z);
				}
				if (cursor.transform.localPosition.x < -7)
				{
					cursor.transform.localPosition = new(-7, cursor.transform.localPosition.y, cursor.transform.localPosition.z);
				}
				if (cursor.transform.localPosition.y < -7)
				{
					cursor.transform.localPosition = new(cursor.transform.localPosition.x, -7, cursor.transform.localPosition.z);
				}
				if (cursor.transform.localPosition.y > 7)
				{
					cursor.transform.localPosition = new(cursor.transform.localPosition.x, 7, cursor.transform.localPosition.z);
				}
#if SELECTION
				if ((selectedObjects?.Count ?? 0) == 0)
				{
					goto afterArrowKeyCheck;
				}

				if (Input.GetKeyDown(KeyCode.UpArrow))
				{
					selectedObjects.Try((obj) =>
					{
						obj.transform.position += new Vector3(0, 1, 0);
						selectionStart += new Vector3(0, 1, 0);
						selectionEnd += new Vector3(0, 1, 0);
						return (obj, (undoObj) =>
						{
							undoObj.transform.position -= new Vector3(0, 1, 0);
							selectionStart -= new Vector3(0, 1, 0);
							selectionEnd -= new Vector3(0, 1, 0);
							return undoObj;
						}
						);
					}, (obj) => obj.transform.position.y > 7);
				}
				if (Input.GetKeyDown(KeyCode.DownArrow))
				{
					selectedObjects.Try((obj) =>
					{
						obj.transform.position += new Vector3(0, -1, 0);
						selectionStart += new Vector3(0, -1, 0);
						selectionEnd += new Vector3(0, -1, 0);
						return (obj, (undoObj) =>
						{
							undoObj.transform.position -= new Vector3(0, -1, 0);
							selectionStart -= new Vector3(0, -1, 0);
							selectionEnd -= new Vector3(0, -1, 0);
							return undoObj;
						}
						);
					}, (obj) => obj.transform.position.y < -7);
				}
				if (Input.GetKeyDown(KeyCode.LeftArrow))
				{
					selectedObjects.Try((obj) =>
					{
						obj.transform.position += new Vector3(-1, 0, 0);
						selectionStart += new Vector3(-1, 0, 0);
						selectionEnd += new Vector3(-1, 0, 0);
						return (obj, (undoObj) =>
						{
							undoObj.transform.position -= new Vector3(-1, 0, 0);
							selectionStart -= new Vector3(-1, 0, 0);
							selectionEnd -= new Vector3(-1, 0, 0);
							return undoObj;
						}
						);
					}, (obj) => obj.transform.position.x < -7);
				}
				if (Input.GetKeyDown(KeyCode.RightArrow))
				{
					selectedObjects.Try((obj) =>
					{
						obj.transform.position += new Vector3(1, 0, 0);
						selectionStart += new Vector3(1, 0, 0);
						selectionEnd += new Vector3(1, 0, 0);
						return (obj, (undoObj) =>
						{
							undoObj.transform.position -= new Vector3(1, 0, 0);
							selectionStart -= new Vector3(1, 0, 0);
							selectionEnd -= new Vector3(1, 0, 0);
							return undoObj;
						}
						);
					}, (obj) => obj.transform.position.x > 7);
				}
#endif
			}

		afterArrowKeyCheck:

			if (Input.GetKeyDown(KeyCode.W))
			{
				currentScreen.y -= 1;
				if (currentScreen.y < 0)
				{
					currentScreen.y = 0;
				}
			}
			if (Input.GetKeyDown(KeyCode.S))
			{
				currentScreen.y += 1;
				if (currentScreen.y > 17)
				{
					currentScreen.y = 17;
				}
			}
			if (Input.GetKeyDown(KeyCode.A))
			{
				currentScreen.x -= 1;
				if (currentScreen.x < 0)
				{
					currentScreen.x = 0;
				}
			}
			if (Input.GetKeyDown(KeyCode.D))
			{
				currentScreen.x += 1;
				if (currentScreen.x > 17)
				{
					currentScreen.x = 17;
				}
			}

			if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.O))
			{
				var ofn = new Unmanaged.OpenFileNameData();

				ofn.structSize = Marshal.SizeOf(ofn);

				ofn.filter = "SOLAS Level files\0*.lvl\0";

				ofn.file = new string(new char[256]);
				ofn.maxFile = ofn.file.Length;

				ofn.fileTitle = new string(new char[64]);
				ofn.maxFileTitle = ofn.fileTitle.Length;

				ofn.initialDir = Paths.GameRootPath;
				ofn.title = "Open level";
				ofn.defExt = "lvl";
				ofn.flags = 0x02000000 | 0x00001000;
				if (!Unmanaged.GetOpenFileName(ofn))
				{
					Plugin.logger.LogError($"Error opening file: {Unmanaged.CommDlgExtendedError()}");
					goto afterCheckingOpenFile;
				}
				placedObjects = LoadFromFile(new(ofn.file));
				placedObjects.ForEach(tuple => UpdateMesh(tuple.Item1, tuple.Item2));
				saved = true;
			}
		afterCheckingOpenFile:
			if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.S))
			{
				var ofn = new Unmanaged.OpenFileNameData();

				ofn.structSize = Marshal.SizeOf(ofn);

				ofn.filter = "SOLAS Level files\0*.lvl\0";

				ofn.file = new string(new char[256]);
				ofn.maxFile = ofn.file.Length;

				ofn.fileTitle = new string(new char[64]);
				ofn.maxFileTitle = ofn.fileTitle.Length;

				ofn.initialDir = Paths.GameRootPath;
				ofn.title = "Save level";
				ofn.defExt = "lvl";
				ofn.flags = 0x02000000;
				if (!Unmanaged.GetSaveFileName(ofn))
				{
					Plugin.logger.LogError($"Error getting file to save: {Unmanaged.CommDlgExtendedError()}");
					goto afterCheckingSaveFile;
				}
				var output = new StringBuilder();
				output.AppendLine("0, 0, 0-252, 0-252 = empty");
				foreach (var obj in placedObjects)
				{
					output.Append($"0, 0, {obj.Item1.transform.position.x + 7}, {-obj.Item1.transform.position.y + 7} = {obj.Item1.name} ");
					foreach (var argument in obj.Item2.Values)
					{
						output.Append(argument.ToString() + " ");
					}
					output.AppendLine();
				}
				using (FileStream file = File.Open(ofn.file, FileMode.OpenOrCreate))
				{
					using StreamWriter stream = new(file);
					file.SetLength(0);
					file.Flush();
					stream.Write(output.ToString());
				}
				saved = true;
			}
		afterCheckingSaveFile:

			var selected = (from obj in placedObjects
							where obj.Item1.transform.position == cursor.transform.position
							select obj).SingleOrDefault();
			if (selected is null)
			{
				if ((Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)) && !(currentlyPlacing == "" || currentlyPlacing == "select"))
				{
					var obj = InitializeFromString(currentlyPlacing);
					var go = obj.Item1;
					go.transform.position = cursor.transform.position;
					placedObjects.Add(obj);
					saved = false;
				}
				goto nothingSelected;
			}

			if (Input.GetKeyDown(KeyCode.R))
			{
				if (selected.Item2.Contains("Direction"))
				{
					if (selected.Item1.name is "prism" or "mirror")
					{
						selected.Item2["Direction"] = (string)selected.Item2["Direction"] == "up" ? "right" : "up";
						UpdateMesh(selected.Item1, selected.Item2);
					}
					else
					{
						switch ((string)selected.Item2["Direction"])
						{
							case "up":
								selected.Item2["Direction"] = "right";
								break;
							case "right":
								selected.Item2["Direction"] = "down";
								break;
							case "down":
								selected.Item2["Direction"] = "left";
								break;
							case "left":
								selected.Item2["Direction"] = "up";
								break;
						}
						UpdateMesh(selected.Item1, selected.Item2);
					}
					saved = false;
				}
			}
			if (Input.GetKey(KeyCode.Delete) || Input.GetMouseButton(1))
			{
				placedObjects.Remove(selected);
				Destroy(selected.Item1);
				saved = false;
			}
			if (Input.GetKeyDown(KeyCode.E))
			{
				this.selected = selected;
				inEditUI = true;
			}

		nothingSelected:
#if SELECTION
			if (currentlyPlacing != "select")
			{
				foreach (var obj in selectedObjects)
				{
					if (placedObjects.Any((tuple) => tuple.Item1.transform.position == obj.transform.position))
					{

					}
				}
				selectionStart = null;
				selectionEnd = null;
			}
			if (selectionStart.HasValue && selectionEnd.HasValue)
			{
				var bl = Vector2.Min(selectionStart.Value, selectionEnd.Value);
				var tr = Vector2.Max(selectionStart.Value, selectionEnd.Value);
				var tl = new Vector2(bl.x, tr.y);
				var br = new Vector2(tr.x, bl.y);
				selectionBox.transform.position = (Vector3)tl + new Vector3(0, 0, 10);
				var selectionFilter = selectionBox.GetComponent<MeshFilter>();
				selectionFilter.mesh = Plugin.MakeBox(bl - tl + new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f), tr - tl + new Vector2(0.5f, 0.5f), br - tl + new Vector2(0.5f, -0.5f), 0.08f);
				selectionBox.SetActive(true);
			}
			else
			{
				selectionBox.SetActive(false);
			}

			if (keyboardUsed)
			{
				goto skipSelection;
			}
			
			if (Input.GetMouseButtonDown(0) && currentlyPlacing == "select")
			{
				selectionStart = cursor.transform.position;
			}
			if (Input.GetMouseButton(0) && currentlyPlacing == "select")
			{
				selectionEnd = cursor.transform.position;
			}

			if (selectionStart.HasValue && Input.GetMouseButtonUp(0))
			{
				selectionEnd = cursor.transform.position;
				selectedObjects = [.. from Tuple<GameObject, OrderedDictionary> objTuple in placedObjects
								  let obj = objTuple.Item1
								  where Mathf.Sqrt((obj.transform.position.x - selectionStart.Value.x) * (selectionEnd.Value.x - obj.transform.position.x)) * Mathf.Sqrt((obj.transform.position.y - selectionStart.Value.y) * (selectionEnd.Value.y - obj.transform.position.y)) >= 0
								  select obj];
				if (selectedObjects.Count == 0)
				{
					selectionStart = null;
					selectionEnd = null;
				}
				else
				{
					var bl = Vector2.Min(selectionStart.Value, selectionEnd.Value);
					var tr = Vector2.Max(selectionStart.Value, selectionEnd.Value);
					var tl = new Vector2(bl.x, tr.y);
					var br = new Vector2(tr.x, bl.y);
					var byX = from obj in selectedObjects
							  orderby obj.transform.position.x
							  select obj.transform.position.x;
					var byY = from obj in selectedObjects
							  orderby obj.transform.position.y
							  select obj.transform.position.y;
					bl.x = tl.x = byX.Min();
					bl.y = br.y = byY.Min();
					tl.y = tr.y = byY.Max();
					br.x = tr.x = byX.Max();
					selectionStart = tl;
					selectionEnd = br;
				}
			}

		skipSelection:
#endif
			Camera.main.transform.position = 14 * new Vector3(currentScreen.x, -currentScreen.y);
			var text = screenLabel.GetComponent<TextMesh>();
			text.text = $"Current screen: {currentScreen.x}, {currentScreen.y}";
			text = placingLabel.GetComponent<TextMesh>();
			text.text = currentlyPlacing == "select" ? "Selecting pieces" : $"Currently placing: {(currentlyPlacing == "" ? "None" : basePieces[currentlyPlacing])}";
			grid.Draw3D();
		}

		[HarmonyPatch(typeof(GameController), "SetupGrid")]
		[HarmonyReversePatch]
		public static void MakeGrid(EditorController editorController)
		{
			IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				var matcher = new CodeMatcher(instructions);
				matcher.MatchForward(false, new CodeMatch(null, AccessTools.Field(typeof(GameController), "gridLines")));
				matcher.Repeat((matcher) =>
				{
					matcher.SetOperandAndAdvance(AccessTools.Field(typeof(EditorController), nameof(grid)));
				});
				matcher.Start();
				matcher.MatchForward(false, new CodeMatch(null, AccessTools.Field(typeof(GameController), "gridLineTransform")));
				matcher.Repeat((matcher) =>
				{
					matcher.SetOperandAndAdvance(AccessTools.Field(typeof(EditorController), nameof(gridTransform)));
				});
				return matcher.InstructionEnumeration();
			}
			_ = Transpiler(null);
		}

		internal List<Tuple<GameObject, OrderedDictionary>> LoadFromFile(FileInfo srcFile)
		{
			List<Tuple<GameObject, OrderedDictionary>> flattenedLevel = [];
			try
			{
				using StreamReader reader = new(srcFile.OpenRead());
				foreach (string line in reader.ReadToEnd().Split('\n'))
				{
					if (line.Trim() != "" && line[0] != '#' && line[0] != '*')
					{
						var data = new OrderedDictionary();
						var coords = line.Split('=')[0].Trim().Split(',');
						var replaceWith = line.Split('=')[1].Trim();
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
									dir = args[0];
									moveable = bool.Parse(args[1]);
									rotateable = bool.Parse(args[2]);
									flips = bool.Parse(args[3]);
									data.Add("Direction", dir);
									data.Add("Moveable", moveable);
									data.Add("Rotateable", rotateable);
									data.Add("Flips", flips);
									break;
								case "prism":
									dir = args[0];
									moveable = bool.Parse(args[1]);
									rotateable = bool.Parse(args[2]);
									data.Add("Direction", dir);
									data.Add("Moveable", moveable);
									data.Add("Rotateable", rotateable);
									break;
								case "emitter":
									dir = args[0];
									color = args[1];
									moveable = bool.Parse(args[2]);
									frequency = int.Parse(args[3]);
									data.Add("Direction", dir);
									data.Add("Color", Enum.Parse(typeof(ColorEmit), color));
									data.Add("Moveable", moveable);
									data.Add("Frequency", frequency);
									break;
								case "receiver":
									dir = args[0];
									color = args[1];
									frequency = int.Parse(args[2]);
									data.Add("Direction", dir);
									data.Add("Color", Enum.Parse(typeof(ColorReceive), color));
									data.Add("Frequency", frequency);
									break;
								case "filter":
									color = args[0];
									data.Add("Color", Enum.Parse(typeof(ColorFilter), color));
									break;
								case "button":
									type = int.Parse(args[0]);
									data.Add("Channel", type);
									break;
								case "door":
									type = int.Parse(args[0]);
									state = args[1];
									data.Add("Channel", type);
									data.Add("State", Enum.Parse(typeof(DoorState), state));
									break;
								case "teleporter":
									channel = int.Parse(args[0]);
									moveable = bool.Parse(args[1]);
									data.Add("Channel", channel);
									data.Add("Moveable", moveable);
									break;
								case "powernode":
									var nodeID = int.Parse(args[0]);
									data.Add("ID", nodeID);
									break;
								case "nodedoor":
									var doorID = int.Parse(args[0]);
									data.Add("ID", doorID);
									break;
								case "combolock":
									if (comboLocks.ContainsKey(int.Parse(args[0])))
									{
										var combolockID = int.Parse(args[0]);
										var combolockPos = int.Parse(args[1]);
										data.Add("Group ID", combolockID);
										data.Add("Code position", combolockPos);
									}
									else
										throw new ArgumentException("Combolock ID " + args[0] + " has not been defined");
									break;
								case "noplace":
									break;
								case "outofbounds":
									break;
								case "wall":
									break;
								case "default":
									break;
								case "glitch":
									break;
								case "fakewall":
									break;
								case "glitchdestroyer":
									break;
								case "corner":
									break;
								case "blocker":
									break;
								case "empty":
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
#if CUSTOMPIECES
								default:
									Tuple<int, object> tile = (Tuple<int, object>)AccessTools.Method(Plugin.GetControllerType(replaceWith.Split(' ')[0]), nameof(CustomController.Global_MakeTile)).Invoke(Activator.CreateInstance(Plugin.GetControllerType(replaceWith.Split(' ')[0])), [args]);
									data.Add("Custom piece data", tile.Item2);
									break;
#endif
							}
						}
						else
						{
							var temp = FromTileID(int.Parse(replaceWith));
							replaceWith = temp.Item1;
							data = temp.Item2;
						}
						if (replaceWith.Split(' ')[0] == "empty")
						{
							continue;
						}
						List<int> indices = [];
						try
						{
							string[] coordsX = coords[2].Trim().Split('-');
							string[] coordsY = coords[3].Trim().Split('-');
							if (replaceWith.Split(' ')[0] == "powernode")
							{
								if (coordsX.Length == 2 || coordsY.Length == 2)
									throw new NotSupportedException("Range syntax is not supported for power nodes");
							}
							else if (replaceWith.Split(' ')[0] == "nodedoor")
							{
								if (coordsX.Length == 2 || coordsY.Length == 2)
									throw new NotSupportedException("Range syntax is not supported for power node doors");
							}
							for (int i = int.Parse(coordsX[0].Trim()); i <= ((coordsX.Length == 2) ? int.Parse(coordsX[1].Trim()) : int.Parse(coordsX[0].Trim())); i++)
							{
								for (int j = int.Parse(coordsY[0].Trim()); j <= ((coordsY.Length == 2) ? int.Parse(coordsY[1].Trim()) : int.Parse(coordsY[0].Trim())); j++)
								{
									indices.Add(int.Parse(coords[0].Trim()) * 14 + i + (int.Parse(coords[1].Trim()) * 14 + j) * 253);
								}
							}

							foreach (int index in indices)
							{
								var position = UnflattenIndex(index);
								var go = new GameObject(replaceWith.Split(' ')[0]);
								go.transform.position = new(position.x - 7, -position.y + 7, 10);
								flattenedLevel.Add(new(go, data));
							}
						}
						catch
						{
							continue;
						}
					}
					else if (line.Trim() != "" && line[0] == '*')
					{
						var type = line.Remove(0, 1).Split(' ')[0];
						var args = new List<string>(line.Remove(0, 1).Split(' ').Skip(1));

						switch (type)
						{
							case "combolock":
								comboLocks[int.Parse(args[0].Trim())] = new Tuple<string, List<Location>>(args[1].Trim(), [.. args.Skip(2).ToDictionary(str =>
								{
									return new Location(int.Parse(str.Trim().Split(',')[0]), int.Parse(str.Trim().Split(',')[1]));
								}).Keys]);
								break;
						}
					}
				}
			}
			catch (Exception e)
			{
				Plugin.logger.LogError("Failed to load level " + srcFile.Name + ": " + e.ToString());
			}
			return flattenedLevel;
		}

		public static Location UnflattenIndex(int index)
		{
			return new(index % 253, index / 253);
		}

		public Tuple<GameObject, OrderedDictionary> InitializeFromString(string name)
		{
			var go = new GameObject(name);
			var dict = new OrderedDictionary();
			switch (name)
			{
				case "mirror":
					dict["Direction"] = "up";
					dict["Moveable"] = false;
					dict["Rotateable"] = false;
					dict["Flips"] = false;
					break;
				case "prism":
					dict["Direction"] = "right";
					dict["Moveable"] = false;
					dict["Rotateable"] = false;
					break;
				case "emitter":
					dict["Direction"] = "up";
					dict["Color"] = ColorEmit.Red;
					dict["Moveable"] = false;
					dict["Frequency"] = Plugin.DEFAULT_FREQUENCY;
					break;
				case "receiver":
					dict["Direction"] = "up";
					dict["Color"] = ColorReceive.Red;
					dict["Frequency"] = Plugin.DEFAULT_FREQUENCY;
					break;
				case "noplace":
				case "outofbounds":
				case "wall":
				case "glitch":
				case "fakewall":
				case "glitchdestroyer":
				case "corner":
				case "blocker":
					break;
				case "filter":
					dict["Color"] = ColorFilter.Red;
					break;
				case "button":
					dict["Channel"] = 0;
					break;
				case "door":
					dict["Channel"] = 0;
					dict["State"] = DoorState.Open;
					break;
				case "teleporter":
					dict["Channel"] = 0;
					dict["Moveable"] = false;
					break;
				case "powernode":
					dict["ID"] = 0;
					break;
				case "nodedoor":
					dict["ID"] = 0;
					break;
				case "combolock":
					dict["Group ID"] = 0;
					dict["Code position"] = 0;
					break;
				case "id":
					dict["ID"] = 0;
					break;
				default:
					break;
			}
			UpdateMesh(go, dict);
			return new(go, dict);
		}

		public Tuple<string, OrderedDictionary> FromTileID(int tileID)
		{
			var name = "";
			var data = new OrderedDictionary();
			switch (tileID)
			{
				case 0:
					name = "empty";
					break;
				case 1:
					name = "noplace";
					break;
				case 2:
					name = "outofbounds";
					break;
				case 3:
					name = "wall";
					break;
				case 4:
					name = "corner";
					break;
				case 5:
					name = "blocker";
					break;
				case 6 or 8:
					name = "glitch";
					break;
				case 7:
					name = "powernode";
					data["ID"] = 0;
					break;
				case 9:
					name = "fakewall";
					break;
				case 10:
					name = "glitchdestroyer";
					break;
				case >= 11 and <= 89:
					data["Direction"] = ((tileID - 1) % 4) switch
					{
						0 => "up",
						1 => "right",
						2 => "down",
						3 => "left"
					};
					if (tileID % 10 >= 5)
					{
						name = "receiver";
						data["Color"] = (tileID / 10) switch
						{
							1 => ColorReceive.Red,
							2 => ColorReceive.Green,
							3 => ColorReceive.Yellow,
							4 => ColorReceive.Blue,
							5 => ColorReceive.Magenta,
							6 => ColorReceive.Cyan,
							7 => ColorReceive.White,
							8 => ColorReceive.Any
						};
					}
					else
					{
						name = "emitter";
						data["Color"] = (tileID / 10) switch
						{
							1 => ColorEmit.Red,
							2 => ColorEmit.Green,
							3 => ColorEmit.Yellow,
							4 => ColorEmit.Blue,
							5 => ColorEmit.Magenta,
							6 => ColorEmit.Cyan,
							7 => ColorEmit.White,
							8 => ColorEmit.Void
						};
						data["Moveable"] = false;
					}
					data["Frequency"] = Plugin.DEFAULT_FREQUENCY;
					break;
				case > 90 and < 100:
					name = "filter";
					data["Color"] = (tileID % 10) switch
					{
						1 => ColorFilter.Red,
						2 => ColorFilter.Green,
						3 => ColorFilter.Yellow,
						4 => ColorFilter.Blue,
						5 => ColorFilter.Magenta,
						6 => ColorFilter.Cyan,
						7 => ColorFilter.White
					};
					break;
				case (> 100 and < 110) or (> 120 and < 130):
					name = "mirror";
					if (tileID % 2 == 1)
					{
						data["Direction"] = "right";
					}
					else
					{
						data["Direction"] = "up";
					}
					switch (tileID % 10)
					{
						case < 3:
							data["Moveable"] = false;
							data["Rotateable"] = false;
							break;
						case < 5:
							data["Moveable"] = true;
							data["Rotaeable"] = false;
							break;
						case < 7:
							data["Moveable"] = false;
							data["Rotateable"] = true;
							break;
						case < 9:
							data["Moveable"] = true;
							data["Rotateable"] = true;
							break;
					}
					if (tileID > 120)
					{
						data["Flips"] = true;
					}
					else
					{
						data["Flips"] = false;
					}
					break;
				case > 110 and < 120:
					name = "prism";
					if (tileID % 2 == 0)
					{
						data["Direction"] = "up";
					}
					else
					{
						data["Direction"] = "right";
					}
					switch (tileID % 10)
					{
						case < 3:
							data["Moveable"] = false;
							data["Rotateable"] = false;
							break;
						case < 5:
							data["Moveable"] = true;
							data["Rotaeable"] = false;
							break;
						case < 7:
							data["Moveable"] = false;
							data["Rotateable"] = true;
							break;
						case < 9:
							data["Moveable"] = true;
							data["Rotateable"] = true;
							break;
					}
					break;
				case > 170 and < 179:
					name = "nodedoor";
					data["ID"] = tileID % 10;
					break;
				case 200:
					name = "combolock";
					data["Group ID"] = 0;
					data["Code position"] = 0;
					break;
				case > 300:
					if ((tileID - 1) % 10 % 3 == 0)
					{
						name = "button";
						data["Channel"] = (tileID / 10 % 10) + (tileID % 10 switch { 1 => 1, 4 => 2, 7 => 3 });
					}
					else
					{
						name = "door";
						data["Channel"] = (tileID / 10 % 10) + (tileID % 10 switch { 2 or 3 => 1, 5 or 6 => 2, 8 or 9 => 3 });
						data["State"] = (tileID % 10) switch
						{
							2 or 5 or 8 => DoorState.Closed,
							3 or 6 or 9 => DoorState.Open
						};
					}
					break;
				case > 200:
					name = "teleporter";
					data["Channel"] = tileID % 100;
					break;
				default:
					name = "id";
					data["ID"] = tileID;
					break;
			}
			return new(name, data);
		}

		public void UpdateMesh(GameObject go, OrderedDictionary args)
		{
			void MakeMoveObject(GameObject parent, Vector3 offset)
			{
				var move = new GameObject();
				var moveMR = move.AddComponent<MeshRenderer>();
				moveMR.material = moveableMaterial;
				var moveMF = move.AddComponent<MeshFilter>();
				moveMF.mesh = Plugin.MakeQuad(new(-0.2f, 0), new(0, 0.2f), new(0.2f, 0), new(0, -0.2f));
				move.transform.parent = parent.transform;
				move.transform.localPosition = offset;
			}

			void MakeRotateObject(GameObject parent, Vector3 offset)
			{
				var rotate = new GameObject();
				var rotateMR = rotate.AddComponent<MeshRenderer>();
				rotateMR.material = rotateableMaterial;
				var rotateMF = rotate.AddComponent<MeshFilter>();
				rotateMF.mesh = Plugin.MakeQuad(new(-0.2f, 0), new(0, 0.2f), new(0.2f, 0), new(0, -0.2f));
				rotate.transform.parent = parent.transform;
				rotate.transform.localPosition = offset;
			}

			Mesh MakeRedMesh()
			{
				return Plugin.MakeQuad(new(-0.15f, 0), new(0, 0.15f), new(0.15f, 0), new(0, -0.15f));
			}

			Mesh MakeGreenMesh()
			{
				var mesh = new Mesh();
				var topLeft = Plugin.MakeQuad(new(-0.15f, 0), new(-0.15f, 0.15f), new(0, 0.15f), new(0, 0));
				var bottomRight = Plugin.MakeQuad(new(0, -0.15f), new(0, 0), new(0.15f, 0), new(0.15f, -0.15f));
				mesh.CombineMeshes([new() { mesh = topLeft }, new() { mesh = bottomRight }], true, false);
				return mesh;
			}

			Mesh MakeBlueMesh()
			{
				var mesh = new Mesh();
				var right = Plugin.MakeLine(new(0.16f, 0), new(0.16f, 0.16f), 0.02f);
				var top = Plugin.MakeLine(new(0.16f, 0.16f), new(0, 0.16f), 0.02f);
				var bottom = Plugin.MakeLine(new(0, -0.16f), new(-0.16f, -0.16f), 0.02f);
				var left = Plugin.MakeLine(new(-0.16f, -0.16f), new(-0.16f, 0), 0.02f);
				mesh.CombineMeshes([new() { mesh = right }, new() { mesh = top }, new() { mesh = bottom }, new() { mesh = left }], true, false);
				return mesh;
			}

			Mesh MakeVoidMesh()
			{
				var mesh = new Mesh();
				var negative = Plugin.MakeLine(new(-0.15f, 0.15f), new(0.15f, -0.15f), 0.02f);
				var positive = Plugin.MakeLine(new(-0.15f, -0.15f), new(0.15f, 0.15f), 0.02f);
				mesh.CombineMeshes([new() { mesh = negative }, new() { mesh = positive }], true, false);
				return mesh;
			}

			void MakeColorObject(string color, GameObject parent, Vector2 offset)
			{
				if (color.ToLower() == "void")
				{
					var voidGo = new GameObject();
					var voidMr = voidGo.AddComponent<MeshRenderer>();
					voidMr.material = colourMaterials[0];
					var voidMf = voidGo.AddComponent<MeshFilter>();
					voidMf.mesh = MakeVoidMesh();
					voidGo.transform.parent = parent.transform;
					voidGo.transform.localPosition = offset;
					return;
				}

				List<CombineInstance> meshes = [];
				var colorByte = (COLOUR)Enum.Parse(typeof(COLOUR), color.ToUpper());
				if ((colorByte & COLOUR.RED) != 0)
				{
					meshes.Add(new() { mesh = MakeRedMesh() });
				}
				if ((colorByte & COLOUR.GREEN) != 0)
				{
					meshes.Add(new() { mesh = MakeGreenMesh() });
				}
				if ((colorByte & COLOUR.BLUE) != 0)
				{
					meshes.Add(new() { mesh = MakeBlueMesh() });
				}
				var go = new GameObject();
				var mr = go.AddComponent<MeshRenderer>();
				mr.material = colourMaterials[(int)colorByte];
				var mf = go.AddComponent<MeshFilter>();
				mf.mesh = new();
				mf.mesh.CombineMeshes([.. meshes], true, false);
				go.transform.parent = parent.transform;
				go.transform.localPosition = offset;
			}

			void MakeNumberObject(GameObject go, int number) => MakeTextObject(go, number.ToString());

			void MakeTextObject(GameObject go, string display)
			{
				var obj = new GameObject();
				var mr = obj.AddComponent<MeshRenderer>();
				var text = obj.AddComponent<TextMesh>();
				obj.transform.localScale = new(0.1f, 0.1f);
				text.text = display;
				text.color = Color.white;
				text.font = Font.GetDefault();
				text.fontSize = 30;
				text.alignment = TextAlignment.Center;
				text.anchor = TextAnchor.MiddleCenter;
				obj.transform.parent = go.transform;
				obj.transform.localPosition = Vector3.zero;
			}

			foreach (var child in go.GetComponentsInChildren<Transform>())
			{
				if (ReferenceEquals(child.gameObject, go))
				{
					continue;
				}
				Destroy(child.gameObject);
			}

			var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
			mr.material = glyphMaterial;
			var filter = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
			filter.mesh = new();
			switch (go.name)
			{
				case "outofbounds":
					var negativeLine = Plugin.MakeLine(new(-0.5f, 0.5f), new(0.5f, -0.5f), 0.03f);
					var positiveLine = Plugin.MakeLine(new(-0.5f, -0.5f), new(0.5f, 0.5f), 0.03f);
					filter.mesh.CombineMeshes([new() { mesh = negativeLine }, new() { mesh = positiveLine }], true, false);
					break;
				case "noplace":
					var first = Plugin.MakeLine(new(0, 0.5f), new(0.5f, 0), 0.02f);
					var second = Plugin.MakeLine(new(-0.5f, 0.5f), new(0.5f, -0.5f), 0.02f);
					var third = Plugin.MakeLine(new(-0.5f, 0), new(0, -0.5f), 0.02f);
					filter.mesh.CombineMeshes([new() { mesh = first }, new() { mesh = second }, new() { mesh = third }], true, false);
					break;
				case "corner":
					var TtoR = Plugin.MakeLine(new(0, 0.5f), new(0.5f, 0), 0.03f);
					var RtoB = Plugin.MakeLine(new(0.5f, 0), new(0, -0.5f), 0.03f);
					var BtoL = Plugin.MakeLine(new(0, -0.5f), new(-0.5f, 0), 0.03f);
					var LtoT = Plugin.MakeLine(new(-0.5f, 0), new(0, 0.5f), 0.03f);
					filter.mesh.CombineMeshes([new() { mesh = TtoR }, new() { mesh = RtoB }, new() { mesh = BtoL }, new() { mesh = LtoT }], true, false);
					break;
				case "mirror":
					var mirror = new GameObject();
					var mirrorMR = mirror.AddComponent<MeshRenderer>();
					mirrorMR.material = glyphMaterial;
					var mirrorMF = mirror.AddComponent<MeshFilter>();
					mirrorMF.mesh = new();
					var main = Plugin.MakeLine(new(-0.5f, -0.5f), new(0.5f, 0.5f), 0.03f);
					if ((string)args["Direction"] != "up")
					{
						mirror.transform.localRotation *= Quaternion.AngleAxis(-90, new(0, 0, 1));
					}
					if ((bool)args["Flips"])
					{
						var secondary = Plugin.MakeLine(new(-0.5f, 0.5f), new(0.5f, -0.5f), 0.01f);
						mirrorMF.mesh.CombineMeshes([new() { mesh = main }, new() { mesh = secondary }], true, false);
					}
					else
					{
						mirrorMF.mesh = main;
					}
					mirror.transform.parent = go.transform;
					mirror.transform.localPosition = Vector3.zero;

					if ((bool)args["Moveable"])
					{
						MakeMoveObject(go, new(-0.1f, 0, -1f));
					}
					if ((bool)args["Rotateable"])
					{
						MakeRotateObject(go, new(0.1f, 0, -1f));
					}
					break;
				case "glitch":
					var redSlice = new GameObject();
					var redMR = redSlice.AddComponent<MeshRenderer>();
					redMR.material = redMaterial;
					var redMF = redSlice.AddComponent<MeshFilter>();
					redMF.mesh = Plugin.MakeQuad(new(-0.166f, -0.5f), new(-0.166f, 0.5f), new(0.166f, 0.5f), new(0.166f, -0.5f));
					redSlice.transform.parent = go.transform;
					redSlice.transform.localPosition = new(-0.333f, 0);

					var greenSlice = new GameObject();
					var greenMR = greenSlice.AddComponent<MeshRenderer>();
					greenMR.material = greenMaterial;
					var greenMF = greenSlice.AddComponent<MeshFilter>();
					greenMF.mesh = Plugin.MakeQuad(new(-0.166f, -0.5f), new(-0.166f, 0.5f), new(0.166f, 0.5f), new(0.166f, -0.5f));
					greenSlice.transform.parent = go.transform;
					greenSlice.transform.localPosition = new(0, 0);

					var blueSlice = new GameObject();
					var blueMR = blueSlice.AddComponent<MeshRenderer>();
					blueMR.material = blueMaterial;
					var blueMF = blueSlice.AddComponent<MeshFilter>();
					blueMF.mesh = Plugin.MakeQuad(new(-0.166f, -0.5f), new(-0.166f, 0.5f), new(0.166f, 0.5f), new(0.166f, -0.5f));
					blueSlice.transform.parent = go.transform;
					blueSlice.transform.localPosition = new(0.333f, 0);

					break;
				case "wall":
					filter.mesh = Plugin.MakeQuad(new(-0.5f, -0.5f), new(-0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, -0.5f));
					break;
				case "fakewall":
					filter.mesh = Plugin.MakeBox(new(-0.5f, -0.5f), new(-0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, -0.5f), 0.03f);
					break;
				case "glitchdestroyer":
					var outer = new GameObject();
					var outerMR = outer.AddComponent<MeshRenderer>();
					outerMR.material = blueMaterial;
					var outerMF = outer.AddComponent<MeshFilter>();
					outerMF.mesh = Plugin.MakeBox(new(-0.5f, -0.5f), new(-0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, -0.5f), 0.03f);

					var middle = new GameObject();
					var middleMR = middle.AddComponent<MeshRenderer>();
					middleMR.material = greenMaterial;
					var middleMF = middle.AddComponent<MeshFilter>();
					middleMF.mesh = Plugin.MakeBox(new(-0.3f, -0.3f), new(-0.3f, 0.3f), new(0.3f, 0.3f), new(0.3f, -0.3f), 0.03f);

					var inner = new GameObject();
					var innerMR = inner.AddComponent<MeshRenderer>();
					innerMR.material = redMaterial;
					var innerMF = inner.AddComponent<MeshFilter>();
					innerMF.mesh = Plugin.MakeBox(new(-0.1f, -0.1f), new(-0.1f, 0.1f), new(0.1f, 0.1f), new(0.1f, -0.1f), 0.03f);

					outer.transform.parent = go.transform;
					outer.transform.localPosition = new(0, 0);
					middle.transform.parent = go.transform;
					middle.transform.localPosition = new(0, 0);
					inner.transform.parent = go.transform;
					inner.transform.localPosition = new(0, 0);
					break;
				case "blocker":
					TtoR = Plugin.MakeLine(new(0, 0.5f), new(0.5f, 0), 0.03f);
					RtoB = Plugin.MakeLine(new(0.5f, 0), new(0, -0.5f), 0.03f);
					BtoL = Plugin.MakeLine(new(0, -0.5f), new(-0.5f, 0), 0.03f);
					LtoT = Plugin.MakeLine(new(-0.5f, 0), new(0, 0.5f), 0.03f);
					filter.mesh.CombineMeshes([new() { mesh = TtoR }, new() { mesh = RtoB }, new() { mesh = BtoL }, new() { mesh = LtoT }], true, false);
					MakeMoveObject(go, new(0, 0));
					break;
				case "prism":
					var actual = new GameObject();
					var actualMR = actual.AddComponent<MeshRenderer>();
					actualMR.material = glyphMaterial;
					var actualMF = actual.AddComponent<MeshFilter>();
					actualMF.mesh = new();
					var top = Plugin.MakeTri(new(0, 0.5f), new(0.25f, 0.25f), new(-0.25f, 0.25f));
					var right = Plugin.MakeTri(new(0.25f, 0.25f), new(0.5f, 0), new(0.25f, -0.25f));
					var bottom = Plugin.MakeTri(new(-0.25f, -0.25f), new(0.25f, -0.25f), new(0, -0.5f));
					var left = Plugin.MakeTri(new(-0.25f, -0.25f), new(-0.5f, 0), new(-0.25f, 0.25f));
					actualMF.mesh.CombineMeshes([new() { mesh = top }, new() { mesh = right }, new() { mesh = bottom }, new() { mesh = left }], true, false);
					if ((string)args["Direction"] == "up")
					{
						actual.transform.localRotation *= Quaternion.AngleAxis(-45, new(0, 0, 1));
					}
					if ((bool)args["Moveable"])
					{
						MakeMoveObject(go, new(-0.1f, 0, -1f));
					}
					if ((bool)args["Rotateable"])
					{
						MakeRotateObject(go, new(0.1f, 0, -1f));
					}

					actual.transform.parent = go.transform;
					actual.transform.localPosition = Vector3.zero;
					break;
				case "emitter":
					var arrow = new GameObject();
					var arrowMR = arrow.AddComponent<MeshRenderer>();
					arrowMR.material = glyphMaterial;
					var arrowMF = arrow.AddComponent<MeshFilter>();
					arrowMF.mesh = new();

					left = Plugin.MakeLine(new(-0.5f, 0), new(0, 0.5f), 0.03f);
					right = Plugin.MakeLine(new(0, 0.5f), new(0.5f, 0), 0.03f);
					arrowMF.mesh.CombineMeshes([new() { mesh = left }, new() { mesh = right }], true, false);
					arrow.transform.parent = go.transform;
					arrow.transform.localPosition = new(0, 0);

					switch ((string)args["Direction"])
					{
						case "up":
							break;
						case "right":
							go.transform.localRotation *= Quaternion.AngleAxis(-90, new(0, 0, 1));
							break;
						case "down":
							go.transform.localRotation *= Quaternion.AngleAxis(-180, new(0, 0, 1));
							break;
						case "left":
							go.transform.localRotation *= Quaternion.AngleAxis(90, new(0, 0, 1));
							break;
					}

					MakeColorObject(Enum.GetName(typeof(ColorEmit), args["Color"]), go, new(0, 0));

					if ((bool)args["Moveable"])
					{
						MakeMoveObject(go, new(0, 0.25f, -1f));
					}
					break;
				case "receiver":
					arrow = new GameObject();
					arrowMR = arrow.AddComponent<MeshRenderer>();
					arrowMR.material = glyphMaterial;
					arrowMF = arrow.AddComponent<MeshFilter>();
					arrowMF.mesh = new();

					left = Plugin.MakeLine(new(-0.5f, 0), new(0, 0.5f), 0.03f);
					right = Plugin.MakeLine(new(0, 0.5f), new(0.5f, 0), 0.03f);
					var bottomLeft = Plugin.MakeLine(new(-0.5f, -0.5f), new(-0.2f, -0.2f), 0.03f);
					var bottomRight = Plugin.MakeLine(new(0.5f, -0.5f), new(0.2f, -0.2f), 0.03f);
					arrowMF.mesh.CombineMeshes([new() { mesh = left }, new() { mesh = right }, new() { mesh = bottomLeft }, new() { mesh = bottomRight }], true, false);
					arrow.transform.parent = go.transform;
					arrow.transform.localPosition = new(0, 0);

					switch ((string)args["Direction"])
					{
						case "up":
							break;
						case "right":
							go.transform.localRotation *= Quaternion.AngleAxis(-90, new(0, 0, 1));
							break;
						case "down":
							go.transform.localRotation *= Quaternion.AngleAxis(-180, new(0, 0, 1));
							break;
						case "left":
							go.transform.localRotation *= Quaternion.AngleAxis(90, new(0, 0, 1));
							break;
					}

					if ((ColorReceive)args["Color"] != ColorReceive.Any)
					{
						MakeColorObject(Enum.GetName(typeof(ColorReceive), args["Color"]), go, new(0, 0));
					}
					else
					{
						var box = new GameObject();
						var boxMR = box.AddComponent<MeshRenderer>();
						boxMR.material = glyphMaterial;
						var boxMF = box.AddComponent<MeshFilter>();
						boxMF.mesh = Plugin.MakeBox(new(-0.1f, -0.1f), new(-0.1f, 0.1f), new(0.1f, 0.1f), new(0.1f, -0.1f), 0.03f);
						box.transform.parent = go.transform;
						box.transform.localPosition = Vector3.zero;
					}
					break;
				case "filter":
					filter.mesh = Plugin.MakePolyline(0.03f, new Vector2(-0.5f, 0.5f), new(-0.2f, 0.5f), new(-0.2f, 0.3f), new(0.2f, 0.3f), new(0.2f, 0.5f), new(0.5f, 0.5f),
						new(0.5f, 0.2f), new(0.3f, 0.2f), new(0.3f, -0.2f), new(0.5f, -0.2f), new(0.5f, -0.5f), new(0.2f, -0.5f), new(0.2f, -0.3f), new(-0.2f, -0.3f),
						new(-0.2f, -0.5f), new(-0.5f, -0.5f), new(-0.5f, -0.2f), new(-0.3f, -0.2f), new(-0.3f, 0.2f), new(-0.5f, 0.2f), new(-0.5f, 0.5f));
					MakeColorObject(Enum.GetName(typeof(ColorFilter), args["Color"]), go, new(0, 0));
					break;
				case "door":
					TtoR = Plugin.MakeLine(new(0, 0.5f), new(0.5f, 0), 0.03f);
					RtoB = Plugin.MakeLine(new(0.5f, 0), new(0, -0.5f), 0.03f);
					BtoL = Plugin.MakeLine(new(0, -0.5f), new(-0.5f, 0), 0.03f);
					LtoT = Plugin.MakeLine(new(-0.5f, 0), new(0, 0.5f), 0.03f);
					if ((DoorState)args["State"] == DoorState.Open)
					{
						mr.material = greenMaterial;
					}
					else
					{
						mr.material = redMaterial;
					}
					filter.mesh.CombineMeshes([new() { mesh = TtoR }, new() { mesh = RtoB }, new() { mesh = BtoL }, new() { mesh = LtoT }], true, false);
					MakeNumberObject(go, int.Parse(args["Channel"].ToString()));
					break;
				case "teleporter":
					filter.mesh = Plugin.MakePolyline(0.03f, new Vector2(-0.45f, 0), new(-0.3f, 0.15f), new(-0.3f, 0.3f), new(-0.15f, 0.3f), new(0, 0.45f), new(0.15f, 0.3f),
						new(0.3f, 0.3f), new(0.3f, 0.15f), new(0.45f, 0), new(0.3f, -0.15f), new(0.3f, -0.3f), new(0.15f, -0.3f), new(0, -0.45f), new(-0.15f, -0.3f),
						new(-0.3f, -0.3f), new(-0.3f, -0.15f), new(-0.45f, 0));
					MakeNumberObject(go, int.Parse(args["Channel"].ToString()));
					if ((bool)args["Moveable"])
					{
						MakeMoveObject(go, new(0, 0));
					}
					break;
				case "button":
					var topLeft = Plugin.MakePolyline(0.03f, new Vector2(-0.5f, 0.3f), new(-0.5f, 0.4f), new(-0.4f, 0.5f), new(-0.3f, 0.5f));
					var topRight = Plugin.MakePolyline(0.03f, new Vector2(0.5f, 0.3f), new(0.5f, 0.4f), new(0.4f, 0.5f), new(0.3f, 0.5f));
					bottomRight = Plugin.MakePolyline(0.03f, new Vector2(0.5f, -0.3f), new(0.5f, -0.4f), new(0.4f, -0.5f), new(0.3f, -0.5f));
					bottomLeft = Plugin.MakePolyline(0.03f, new Vector2(-0.5f, -0.3f), new(-0.5f, -0.4f), new(-0.4f, -0.5f), new(-0.3f, -0.5f));
					MakeNumberObject(go, int.Parse(args["Channel"].ToString()));
					filter.mesh.CombineMeshes([new() { mesh = topLeft }, new() { mesh = topRight }, new() { mesh = bottomLeft }, new() { mesh = bottomRight }], true, false);
					break;
				case "nodedoor":
					filter.mesh = Plugin.MakePolyline(0.03f, new Vector2(-0.5f, 0.3f), new(-0.5f, -0.3f), new(-0.5f, 0), new(0.5f, 0), new(0.5f, -0.3f), new(0.5f, 0.3f));
					MakeNumberObject(go, int.Parse(args["ID"].ToString()));
					break;
				case "id":
					MakeTextObject(go, $"ID: {args["ID"]}");
					break;
				default:
					MakeTextObject(go, go.name);
					break;
			}
			filter.mesh.UploadMeshData(false);
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
	public class Patch_PowerNodeControllerTurnOnIfNecessary
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

	[HarmonyPatch(typeof(GlobalVariables), nameof(GetFilterAt))]
	public class Patch_GlobalVariablesGetFilterAt
	{
		public static bool Prefix(int x, int y, ref Filter __result)
		{
			if (x >= 253 || y >= 253)
			{
				__result = null;
				return false;
			}
			return true;
		}

		//public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
		//{
		//	var matcher = new CodeMatcher(instrs);
		//	matcher.MatchForward(false, new CodeMatch(Bgt));
		//	matcher.SetOpcodeAndAdvance(Bge_S);
		//	matcher.MatchForward(false, new CodeMatch(Cgt));
		//	matcher.SetOpcodeAndAdvance(Clt);
		//	matcher.Insert(new CodeInstruction(Not));
		//	foreach (var instr in matcher.InstructionEnumeration())
		//	{
		//		FileLog.Log(instr.ToString());
		//	}
		//	return matcher.InstructionEnumeration();
		//}

		//public static void Postfix(Filter __result, int x, int y)
		//{
		//	Plugin.logger.LogInfo(currentFilterState.Length);
		//	Plugin.logger.LogInfo(__result is null);
		//}
	}

	[HarmonyPatch(typeof(GameController), "checkForPulseToPieceCollision")]
	public class Patch_GameControllercheckForPulseToPieceCollision
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
				new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GameControllercheckForPulseToPieceCollision), "Temp")));
			matcher.End();
			matcher.Advance(-3);
			matcher.Insert(
				new CodeInstruction(Ldarg_1),
				new CodeInstruction(Ldarg_2),
				new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GameControllercheckForPulseToPieceCollision), "Temp2")));
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
	public class Patch_GameControllerBuildLevel
	{
		public static void Prefix(ref int[] level, GameController __instance)
		{
			Plugin.instance.GameStart();
			bool isFirstLoad = (bool)AccessTools.Field(typeof(GameController), "showFullIntro").GetValue(__instance);
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
	public class Patch_TeleportControllerSetupPieceAndUpdate
	{
		static Dictionary<TeleportController, GameObject> moveObjects = [];

		[HarmonyPatch(typeof(TeleportController), "Update")]
		public static void Postfix(TeleportController __instance)
		{
			new MoveController(RevPatch_WallControllerUpdate.Update)(__instance);
			var moveObject = moveObjects[__instance];
			moveObject.transform.position = new(__instance.transform.position.x, __instance.transform.position.y, moveObject.transform.position.z + 2);
		}

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

		public static void Postfix(TeleportController __instance, int tileX, int tileY, bool drag)
		{
			__instance.BaseLocation = new Vector3(tileX, -tileY, __instance.BaseLocation.z);
			__instance.VisibleLocation = __instance.BaseLocation;
			var moveObject = GameObject.Instantiate(Plugin.MoveQuad);
			moveObjects[__instance] = moveObject;
			moveObject.transform.position = new(__instance.transform.position.x, __instance.transform.position.y, moveObject.transform.position.z + 2);
			moveObject.SetActive(drag);
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
			Plugin.moveableTiles[___mouseStartLoc.x, ___mouseStartLoc.y] ^= Plugin.moveableTiles[tileX, tileY];
			Plugin.moveableTiles[tileX, tileY] ^= Plugin.moveableTiles[___mouseStartLoc.x, ___mouseStartLoc.y];
			Plugin.moveableTiles[___mouseStartLoc.x, ___mouseStartLoc.y] ^= Plugin.moveableTiles[tileX, tileY];
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
			int id;
			if (Plugin.teleporters[x, y] is not null)
			{
				isCustom = true;
				id = (int)Plugin.teleporters[x, y];
			}
			else
			{
				id = (pieceType - 201) * 2;
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
				level = [.. currentLevelState.Cast<int?>()];
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
			Plugin.logger.LogInfo($"{__result.x}, {__result.y}");
			return false;
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

	[HarmonyPatch(typeof(LoadController), "PositionPieces")]
	public class Patch_LoadControllerPositionPieces
	{
		public static void Prefix()
		{
			Plugin.editorButton = new("Editor Button")
			{
				tag = "FadeIn"
			};

			Plugin.editorButton.AddComponent<MeshRenderer>();

			var filter = Plugin.editorButton.AddComponent<MeshFilter>();

			filter.mesh = Plugin.MakeBox(new(-0.5f, -0.5f), new(-0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, -0.5f), 0.03f);
			for (int i = 0; i < Levels.MenuMap.GetLength(0); i++)
			{
				for (int j = 0; j < Levels.MenuMap.GetLength(1); j++)
				{
					switch (Levels.MenuMap[i, j])
					{
						case -2:
							Plugin.editorButton.transform.position = new(j, -i, 0f);
							Plugin.editorButton.GetComponent<MeshRenderer>().material = glyphMaterial;
							break;
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(LoadController), "Awake")]
	public class Patch_LoadControllerAwake
	{
		public static void Prefix(LoadController __instance)
		{
			Plugin.loadController = __instance;
		}
	}

	[HarmonyPatch(typeof(LoadController), "UpdateCursor")]
	public class Patch_LoadControllerUpdateCursor
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var matcher = new CodeMatcher(instructions);
			matcher.MatchForward(false, new CodeMatch(Ldc_I4_6));
			matcher.Advance(1);
			matcher.MatchForward(false, new CodeMatch(Ldc_I4_6));
			matcher.SetInstructionAndAdvance(new(Nop));
			matcher.SetInstruction(new(Call, AccessTools.Method(typeof(Patch_LoadControllerUpdateCursor), nameof(Temp))));
			return matcher.InstructionEnumeration();
		}

		public static bool Temp(int tileValue)
		{
			return tileValue is 6 or -2;
		}
	}

	[HarmonyPatch(typeof(LoadController), "ClickOnMainMenu")]
	public class Patch_LoadControllerClickOnMainMenu
	{
		public static void Prefix(int ___curTileValue)
		{
			if (Input.GetButtonDown("ClickM") || Input.GetButtonDown("ClickK") || Input.GetButtonDown("ClickC"))
			{
				switch (___curTileValue)
				{
					case -2:
						SceneManager.LoadScene("Editor", new LoadSceneParameters(LoadSceneMode.Single, LocalPhysicsMode.None));
						Plugin.inEditor = true;
						break;
				}
			}
		}
	}

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
				origMat = Plugin.OrigMiniCannonColors[___shotNumber]
			});
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

	[HarmonyPatch(typeof(GlobalVariables), "SaveStandAlone")]
	public class Patch_GlobalVariablesSaveStandAlone
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var matcher = new CodeMatcher(instructions);
			matcher.MatchForward(false, new CodeMatch(Call, AccessTools.Method(typeof(BinaryFormatter), nameof(BinaryFormatter.Serialize), [typeof(Stream), typeof(object)])));
			matcher.Insert(new CodeInstruction(Call, AccessTools.Method(typeof(Patch_GlobalVariablesSaveStandAlone), "Temp")));
			matcher.Start();
			matcher.MatchForward(false, new CodeMatch(null, AccessTools.Field(typeof(GlobalVariables), "FILE_NAME")));
			matcher.Repeat(matcher =>
			{
				matcher.SetInstruction(new(Ldstr, "/DorchModded.sav"));
			});
			return matcher.InstructionEnumeration();
		}

		public static Tuple<LevelStateHolder[], ExtendedSaveData?[]> Temp(LevelStateHolder[] state)
		{
			return new(state, Plugin.extraSaveData);
		}
	}

	[HarmonyPatch(typeof(GlobalVariables), "LoadStandAlone")]
	public class Patch_GlobalVariablesLoadStandAlone
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var matcher = new CodeMatcher(instructions);
			matcher.MatchForward(false, new CodeMatch(null, AccessTools.Field(typeof(GlobalVariables), "FILE_NAME")));
			matcher.Repeat(matcher =>
			{
				matcher.SetInstruction(new(Ldstr, "/DorchModded.sav"));
			});
			matcher.Start();
			matcher.MatchForward(false, new CodeMatch(Castclass));
			matcher.SetOperandAndAdvance(typeof(Tuple<LevelStateHolder[], ExtendedSaveData?[]>));
			matcher.InsertAndAdvance(new CodeInstruction(Dup), new(Call, AccessTools.PropertyGetter(typeof(Tuple<LevelStateHolder[], ExtendedSaveData?[]>), "Item1")));
			matcher.Advance(1);
			matcher.Insert(new CodeInstruction(Call, AccessTools.PropertyGetter(typeof(Tuple<LevelStateHolder[], ExtendedSaveData?[]>), "Item2")),
				new(Call, AccessTools.Method(typeof(Patch_GlobalVariablesLoadStandAlone), "Temp")));
			return matcher.InstructionEnumeration();
		}

		public static void Temp(ExtendedSaveData?[] data)
		{
			Plugin.extraSaveData = data;
		}
	}

	[HarmonyPatch(typeof(GameController), "SaveCurrentState")]
	public class Patch_GameControllerSaveCurrentState
	{
		public static void Prefix()
		{
			Plugin.extraSaveData[STATE_NUMBER] = new()
			{
				destroyedGlitches = Plugin.destroyedGlitches,
				nodeIDs = Plugin.nodeIDs,
				doorIDs = Plugin.doorIDs,
				teleporters = Plugin.teleporters,
				moveableTiles = Plugin.moveableTiles,
				zones = Plugin.modifiedZones,
				codes = Plugin.codeStorages,
				tileData = Plugin.extraData,
				comboLocks = Plugin.comboLocks
			};
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

		public static void Try<T>(this List<T> list, Func<T, (T changed, Func<T, T> undo)> perform, Predicate<T> rollbackIf)
		{
			for (int i = 0; i < list.Count; i++)
			{
				(list[i], var undoFunction) = perform(list[i]);
				if (rollbackIf(list[i]))
				{
					for (int j = i; j >= 0; j--)
					{
						list[i] = undoFunction(list[i]);
					}
					return;
				}
			}
		}
	}

	public static class Unmanaged
	{
		[DllImport("Comdlg32.dll", CharSet = CharSet.Auto)]
		public static extern bool GetOpenFileName([In, Out] OpenFileNameData fileNameData);

		[DllImport("Comdlg32.dll", CharSet = CharSet.Auto)]
		public static extern bool GetSaveFileName([In, Out] OpenFileNameData fileNameData);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public class OpenFileNameData
		{
			public int structSize = 0;
			public IntPtr dlgOwner = IntPtr.Zero;
			public IntPtr instance = IntPtr.Zero;

			public string filter = null;
			public string customFilter = null;
			public int maxCustFilter = 0;
			public int filterIndex = 0;

			public string file = null;
			public int maxFile = 0;

			public string fileTitle = null;
			public int maxFileTitle = 0;

			public string initialDir = null;

			public string title = null;

			public int flags = 0;
			public short fileOffset = 0;
			public short fileExtension = 0;

			public string defExt = null;

			public IntPtr custData = IntPtr.Zero;
			public IntPtr hook = IntPtr.Zero;

			public string templateName = null;

			public IntPtr reservedPtr = IntPtr.Zero;
			public int reservedInt = 0;
			public int flagsEx = 0;
		}

		[DllImport("Comdlg32.dll")]
		public static extern uint CommDlgExtendedError();
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