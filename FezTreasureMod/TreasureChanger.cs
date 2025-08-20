using FezEngine;
using FezEngine.Services.Scripting;
using FezEngine.Structure;
using FezEngine.Structure.Input;
using FezEngine.Tools;
using FezGame;
using FezGame.Tools;
using FezGame.Components;
using FezGame.Services;
using FezGame.Services.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using static FezTreasure.ViewpointChanger;
using System.Linq;

namespace FezTreasure
{
    public class TreasureChanger : GameComponent
    {
        /*
         * String is level where collectible is contained (e.g. arch, owl, nature_hub, etc.)
         * List<Collectible> is every collectible in each level
         * Each collectible has info of how it is created (Trile, Chest, Spawn) and where it is stored (Emplacement, Position, Volume)
         * See collectibles.txt for reference
         */
        public static Dictionary<string, List<Collectible>> AllCollectibles { get; set; }
        public static Dictionary<string, List<Collectible>> TrileCollectibles { get; set; }
        public static Dictionary<string, List<Collectible>> ChestCollectibles { get; set; }
        public static Dictionary<string, List<Collectible>> SpawnCollectibles { get; set; }

        [ServiceDependency]
        public IGameService GameService { private get; set; }

        [ServiceDependency]
        public IGameStateManager GameState { private get; set; }

        [ServiceDependency]
        public IGameCameraManager Camera { private get; set; }

        [ServiceDependency]
        public IGameLevelManager LevelManager { private get; set; }

        public static Level CurrentLevel { get; set; }

        public string CurrentType { get; set; }

        public static Fez Fez { get; private set; }

        public TreasureChanger(Game game) : base(game)
        {
            string collFile = "";
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\FEZ";
            if (File.Exists(appDataFolder + "\\randomized.txt"))
            {
                collFile = File.ReadAllText(appDataFolder + "\\randomized.txt");
            }
            else
            {
                collFile = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\collectibles.txt") ?? throw new FileNotFoundException("collectibles.txt not found in " + Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\settings.txt");
            }
            AllCollectibles = JsonConvert.DeserializeObject<Dictionary<string, List<Collectible>>>(collFile);
            TrileCollectibles = new Dictionary<string, List<Collectible>>();
            ChestCollectibles = new Dictionary<string, List<Collectible>>();
            SpawnCollectibles = new Dictionary<string, List<Collectible>>();
            StartGameChanger.SplitCollectibles();

            Fez = (Fez)game;

            BindingFlags NonPublicFlags = BindingFlags.Instance | BindingFlags.NonPublic;

            //hooks for chests
            Type OpenTreasureType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.Actions.OpenTreasure");

            Hook OpenTreasureBeginDetour = new Hook(
                OpenTreasureType.GetMethod("Begin", NonPublicFlags), 
                new Action<Action<object>, object>((orig, self) => BeginHooked(orig, self))
            );

            Hook OpenTreasureEndDetour = new Hook(
                OpenTreasureType.GetMethod("End", NonPublicFlags),
                new Action<Action<object>, object>((orig, self) => EndHooked(orig, self))
            );

            //hook for loading correct triles
            On.FezGame.Services.GameLevelManager.Load += OnLMLoad;

            //hook for spawning triles - code inputs
            On.FezGame.Services.Scripting.VolumeService.SpawnTrileAt += (orig, self, id, actorTypeName) =>
            {
                string newActorType = "";
                foreach (var coll in SpawnCollectibles[CurrentLevel.Name])
                {
                    if (coll.Volume == id)
                    {
                        newActorType = coll.Type;
                    }
                }
                orig(self, id, newActorType);
            };

            List<string> forkLevels = new List<string>
            {
                "SEWER_FORK",
                "CMY_FORK",
                "LAVA_FORK",
                "ZU_FORK"
            };

            List<string> zuPuzzleLevels = new List<string>
            {
                "ZU_ZUISH",
                "ZU_TETRIS",
                "ZU_UNFOLD"
            };

            //hook for spawning triles - assorted puzzles - still wip
            /*
            On.FezEngine.Services.LevelManager.ActorTriles += (orig, self, type) =>
            {
                if (forkLevels.Contains(CurrentLevel.Name) && type == ActorType.SecretCube)
                {
                    foreach (var coll in SpawnCollectibles[CurrentLevel.Name])
                    {
                        return orig(self, (ActorType)Enum.Parse(typeof(ActorType), coll.Type));
                    }
                }
                else if (zuPuzzleLevels.Contains(CurrentLevel.Name) && type == ActorType.SecretCube)
                {
                    foreach (var coll in SpawnCollectibles[CurrentLevel.Name])
                    {
                        return orig(self, (ActorType)Enum.Parse(typeof(ActorType), coll.Type));
                    }
                }
                return orig(self, type);
            };
            */

            //hook for artobjects - in testing
            /*
            On.FezEngine.Readers.LevelReader.Read += (orig, self, input, existingInstance) =>
            {
                Level level = orig(self, input, existingInstance);
                if (level.Name == "BOILEROOM")
                {
                    ArtObjectInstance aoInstance = new ArtObjectInstance("treasure_mapao");
                    aoInstance.Position = new Vector3(10, 11, 14);
                    aoInstance.Scale = new Vector3(1f, 1f, 1f);
                    aoInstance.Rotation = Quaternion.Identity;
                    ArtObjectActorSettings aoActorSettings = new ArtObjectActorSettings();
                    aoActorSettings.Inactive = false;
                    aoActorSettings.ContainedTrile = ActorType.None;
                    aoActorSettings.AttachedGroup = null;
                    aoActorSettings.SpinView = Viewpoint.None;
                    aoActorSettings.SpinEvery = 0;
                    aoActorSettings.SpinOffset = 0;
                    aoActorSettings.OffCenter = false;
                    aoActorSettings.RotationCenter = new Vector3(0, 0, 0);
                    aoActorSettings.VibrationPattern = new VibrationMotor[0];
                    aoActorSettings.CodePattern = new CodeInput[0];
                    aoActorSettings.Segment = new PathSegment();
                    aoActorSettings.NextNode = null;
                    aoActorSettings.DestinationLevel = "";
                    aoActorSettings.TreasureMapName = "MAP_CRYPT_D";
                    aoActorSettings.InvisibleSides = new HashSet<FaceOrientation>();
                    aoActorSettings.TimeswitchWindBackSpeed = 0;
                    aoInstance.ActorSettings = aoActorSettings;
                    level.ArtObjects.Add(level.ArtObjects.Count + 1, aoInstance);
                }
                return level;
            };
            */
        }

        private void OnLMLoad(On.FezGame.Services.GameLevelManager.orig_Load orig, GameLevelManager self, string levelName)
        {
            orig(self, levelName);

            var selfType = self.GetType();
            CurrentLevel = (Level)selfType.GetField("levelData", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);

            List<Collectible> curLevelCollectibles = TrileCollectibles[CurrentLevel.Name];

            int bitId, cubeId, antiId, keyId;
            bitId = cubeId = antiId = keyId = -1;

            //finds trile ids
            foreach (var value in CurrentLevel.TrileSet.Triles.Values)
            {
                switch (value.ActorSettings?.Type ?? null)
                {
                    case ActorType.GoldenCube:
                        bitId = value.Id;
                        break;
                    case ActorType.CubeShard:
                        cubeId = value.Id;
                        break;
                    case ActorType.SecretCube:
                        antiId = value.Id;
                        break;
                    case ActorType.SkeletonKey:
                        keyId = value.Id;
                        break;
                }
            }

            foreach (var newColl in curLevelCollectibles)
            {
                if (newColl.Emplacement == null)
                {
                    continue;
                }
                TrileEmplacement newTrileEmplacement = new TrileEmplacement(newColl.Emplacement[0], newColl.Emplacement[1], newColl.Emplacement[2]);
                TrileInstance gameTrile = CurrentLevel.Triles[newTrileEmplacement];
                switch (newColl.Type.ToString())
                {
                    case "GoldenCube":
                        if (bitId != -1)
                        {
                            gameTrile.TrileId = bitId;
                        }
                        break;
                    case "CubeShard":
                        gameTrile.TrileId = cubeId;
                        break;
                    case "SecretCube":
                        if (antiId != -1)
                        {
                            gameTrile.TrileId = antiId;
                        }
                        break;
                    case "SkeletonKey":
                        if (keyId != -1)
                        {
                            gameTrile.TrileId = keyId;
                        }
                        break;
                }
            }
        }

        private void BeginHooked(Action<object> orig, object self)
        {
            Collectible newTreasure = new Collectible();
            var selfType = self.GetType();
            var chestAO = (ArtObjectInstance)selfType.GetField("chestAO", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
            if (chestAO != null)
            {
                foreach (var coll in ChestCollectibles[CurrentLevel.Name])
                {
                    if (chestAO.Position.Equals(new Vector3(coll.ArtObjectPosition[0], coll.ArtObjectPosition[1], coll.ArtObjectPosition[2])))
                    {
                        newTreasure.Type = coll.Type;
                        newTreasure.TreasureMapName = coll.TreasureMapName;
                        CurrentType = coll.Type;
                    }
                }
                if (newTreasure.Type != null)
                {
                    var actorSettings = chestAO.GetType().GetProperty("ActorSettings", BindingFlags.Public | BindingFlags.Instance).GetValue(chestAO);
                    var containedTrileField = actorSettings.GetType().GetProperty("ContainedTrile", BindingFlags.Public | BindingFlags.Instance);
                    var treasureMapNameField = actorSettings.GetType().GetProperty("TreasureMapName", BindingFlags.Public | BindingFlags.Instance);
                    containedTrileField.SetValue(actorSettings, Enum.Parse(typeof(ActorType), newTreasure.Type));
                    treasureMapNameField.SetValue(actorSettings, newTreasure.TreasureMapName);
                    if (newTreasure.Type == ActorType.GoldenCube.ToString())
                    {
                        GameState.SaveData.CollectedParts++;
                    }
                }
            }
            orig(self);
            var treasureActorType = (ActorType)selfType.GetField("treasureActorType", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
            CurrentType = treasureActorType.ToString();
        }

        private void EndHooked(Action<object> orig, object self)
        {
            switch (CurrentType)
            {
                case "Tome":
                    if (!AllowedViewpoints.Contains(2))
                    {
                        AllowedViewpoints.Add(2);
                        GameState.ShowScroll("Your control over the dimensions has increased!", 5, false);
                    }
                    break;
                case "LetterCube":
                    if (!AllowedViewpoints.Contains(3))
                    {
                        AllowedViewpoints.Add(3);
                        GameState.ShowScroll("Your control over the dimensions has increased!", 5, false);
                    }
                    break;
                case "NumberCube":
                    if (!AllowedViewpoints.Contains(4))
                    {
                        AllowedViewpoints.Add(4);
                        GameState.ShowScroll("Your control over the dimensions has increased!", 5, false);
                    }
                    break;
                case "TriSkull":
                    if (!CodeInputChanger.CodeAllowed)
                    {
                        CodeInputChanger.CodeAllowed = true;
                        ScrollOpenAndClose("You have released the power of tetronimos!", 3000, true);
                    }
                    break;
            }
            orig(self);
        }

        public static async void ScrollOpenAndClose(string text, int ms, bool onTop)
        {
            TextScroll scroll = new TextScroll(Fez, text, true)
            {
                Key = text
            };
            FezEngine.Tools.ServiceHelper.AddComponent(scroll);
            await Task.Delay(ms);
            scroll.Close();
        }

        public class Collectible
        {
            public string LocationType;

            public int[] Emplacement;

            public float[] ArtObjectPosition;

            public int Volume;

            public string Type;

            public string TreasureMapName = string.Empty;
        }
    }
}
