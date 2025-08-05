using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using FezGame;
using FezGame.Services;
using FezEngine;
using FezEngine.Services;
using FezEngine.Structure;
using FezEngine.Tools;
using FezEngine.Readers;
using MonoMod.RuntimeDetour;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

public class TreasureChange : GameComponent
{
    public static Type OpenTreasureType;

    public static Type LoadLevelType;

    public static Type MainMenuType;

    /*
     * String is level where collectible is contained (e.g. arch, owl, nature_hub, etc.)
     * List<Collectible> is every collectible in each level
     * Each collectible has info of how it is created (Trile, Chest, Spawn) and where it is stored (Emplacement, Position, Volume)
     * See collectibles.txt for reference
     */
    public Dictionary<string, List<Collectible>> AllCollectibles { get; set; }

    public Dictionary<string, List<Collectible>> TrileCollectibles { get; set; }

    public Dictionary<string, List<Collectible>> ChestCollectibles { get; set; }

    public Dictionary<string, List<Collectible>> SpawnCollectibles { get; set; }

    public bool FullRando { get; set; }

    public Level CurrentLevel { get; set; }

    [ServiceDependency]
    public IGameStateManager GameState { private get; set; }

    public static Fez Fez { get; private set; }

    public TreasureChange(Game game) : base(game)
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
        SplitCollectibles();

        Fez = (Fez)game;

        BindingFlags NonPublicFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        //hook for starting rando
        MainMenuType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.MainMenu");
        MethodBase StartGameMethod = MainMenuType.GetMethod("StartNewGame", NonPublicFlags);
        
        Hook MainMenuStartGameDetour = new Hook(StartGameMethod,
            new Action<Action<object>, object>((orig, self) =>
            StartGameHooked(orig, self)
            ));

        //hook for chests
        OpenTreasureType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.Actions.OpenTreasure");
        MethodBase ActMethod = OpenTreasureType.GetMethod("Act", NonPublicFlags);
        MethodBase BeginMethod = OpenTreasureType.GetMethod("Begin", NonPublicFlags);

        Hook OpenTreasureBeginDetour = new Hook(BeginMethod,
            new Action<Action<object>, object>((orig, self) =>
            BeginHooked(orig, self)
            ));

        //hook for loading correct triles
        On.FezGame.Services.GameLevelManager.Load += OnLMLoad;

        //hook for spawning triles
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
    }

    private void StartGameHooked(Action<object> orig, object self)
    {
        string settingsFile = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\settings.txt") ?? throw new FileNotFoundException("settings.txt not found in " + Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\settings.txt");
        FullRando = JsonConvert.DeserializeObject<Settings>(settingsFile).FullRando;
        string inString = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\collectibles.txt") ?? throw new FileNotFoundException("collectibles.txt not found in " + Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\settings.txt");
        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\FEZ";
        string outPath = appDataFolder + "\\randomized.txt";

        AllCollectibles = JsonConvert.DeserializeObject<Dictionary<string, List<Collectible>>>(inString);
        if (FullRando)
        {
            RandomizeCollectiblesWithLogic(AllCollectibles);
        }
        else
        {
            SplitCollectibles();
            RandomizeCollectibles(TrileCollectibles);
            RandomizeCollectibles(ChestCollectibles);
            RandomizeCollectibles(SpawnCollectibles);
        }

        File.WriteAllText(outPath, JsonConvert.SerializeObject(AllCollectibles, Formatting.Indented));
        orig(self);

        GameState.SaveData.Level = "GOMEZ_HOUSE";
    }

    private void BeginHooked(Action<object> orig, object self)
    {
        Collectible newTreasure = new Collectible();
        var selfType = self.GetType();
        var chestAO = (ArtObjectInstance) selfType.GetField("chestAO", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
        if (chestAO != null)
        {
            foreach (var coll in ChestCollectibles[CurrentLevel.Name])
            {
                if (chestAO.Position.Equals(new Vector3(coll.ArtObjectPosition[0], coll.ArtObjectPosition[1], coll.ArtObjectPosition[2])))
                {
                    newTreasure.Type = coll.Type;
                    newTreasure.TreasureMapName = coll.TreasureMapName;
                }
            }
            if (newTreasure.Type != null)
            {
                var actorSettings = chestAO.GetType().GetProperty("ActorSettings", BindingFlags.Public | BindingFlags.Instance).GetValue(chestAO);
                var containedTrileField = actorSettings.GetType().GetProperty("ContainedTrile", BindingFlags.Public | BindingFlags.Instance);
                var treasureMapNameField = actorSettings.GetType().GetProperty("TreasureMapName", BindingFlags.Public | BindingFlags.Instance);
                containedTrileField.SetValue(actorSettings, Enum.Parse(typeof(ActorType), newTreasure.Type));
                treasureMapNameField.SetValue(actorSettings, newTreasure.TreasureMapName);
            }
        }
        orig(self);
    }

    private void OnLMLoad(On.FezGame.Services.GameLevelManager.orig_Load orig, GameLevelManager self, string levelName)
    {
        orig(self, levelName);

        var selfType = self.GetType();
        CurrentLevel = (Level) selfType.GetField("levelData", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
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

        //overwrite levelData trileIds to spawn diff triles
        foreach (var newColl in curLevelCollectibles)
        {
            foreach (var gameTrile in CurrentLevel.Triles)
            {
                TrileEmplacement newTrileEmplacement = new TrileEmplacement(newColl.Emplacement[0], newColl.Emplacement[1], newColl.Emplacement[2]);
                if (!newTrileEmplacement.Equals(gameTrile.Key))
                {
                    continue;
                }
                switch (newColl.Type.ToString())
                {
                    case "GoldenCube":
                        if (bitId != -1)
                        {
                            gameTrile.Value.TrileId = bitId;
                        }
                        break;
                    case "CubeShard":
                        gameTrile.Value.TrileId = cubeId;
                        break;
                    case "SecretCube":
                        if (antiId != -1)
                        {
                            gameTrile.Value.TrileId = antiId;
                        }
                        break;
                    case "SkeletonKey":
                        if (keyId != -1) {
                            gameTrile.Value.TrileId = keyId;
                        }
                        break;
                }
            }
        }
    }

    private void RandomizeCollectibles(Dictionary<string, List<Collectible>> collectibles)
    {
        var fullListTypesAndMaps = new List<(string Type, string TreasureMapName)>();
        
        foreach (var level in collectibles)
        {
            foreach (var coll in level.Value)
            {
                fullListTypesAndMaps.Add((coll.Type, coll.TreasureMapName));
            }
        }
        Random rand = new Random();
        int n = fullListTypesAndMaps.Count;
        while (n > 1)
        {
            n--;
            int k = rand.Next(n + 1);
            var value = fullListTypesAndMaps[k];
            fullListTypesAndMaps[k] = fullListTypesAndMaps[n];
            fullListTypesAndMaps[n] = value;
        }
        int j = 0;
        foreach (var level in collectibles)
        {
            for (int i = 0; i < level.Value.Count; i++)
            {
                level.Value[i].Type = fullListTypesAndMaps[j].Type;
                level.Value[i].TreasureMapName = fullListTypesAndMaps[j].TreasureMapName;
                j++;
            }
        }
    }

    private void RandomizeCollectiblesWithLogic(Dictionary<string, List<Collectible>> collectibles)
    {
        //find and remove special collectibles
        List<String> needChestTypes = new List<String>{ "NumberCube", "LetterCube", "TreasureMap", "Tome", "TriSkull" };
        Dictionary<string, List<Collectible>> needChestCollectibles = new Dictionary<string, List<Collectible>>();
        Dictionary<string, List<Collectible>> heartCollectibles = new Dictionary<string, List<Collectible>>();
        foreach (var level in collectibles)
        {
            var needChestLevelCollectibles = new List<Collectible>();
            var heartLevelCollectibles = new List<Collectible>();
            foreach (var coll in level.Value)
            {
                //find collectibles
                if (needChestTypes.Contains(coll.Type))
                {
                    needChestLevelCollectibles.Add(coll);
                }
                if (coll.Type == "PieceOfHeart")
                {
                    heartLevelCollectibles.Add(coll);
                }
            }
            //remove from original collection
            foreach (var coll in heartLevelCollectibles)
            {
                level.Value.Remove(coll);
            }
            foreach (var coll in needChestLevelCollectibles)
            {
                level.Value.Remove(coll);
            }
            needChestCollectibles.Add(level.Key, needChestLevelCollectibles);
            heartCollectibles.Add(level.Key, heartLevelCollectibles);
        }
        //randomize all leftover collectibles
        RandomizeCollectibles(collectibles);
        //clear collectible collections
        TrileCollectibles = new Dictionary<string, List<Collectible>>();
        ChestCollectibles = new Dictionary<string, List<Collectible>>();
        SpawnCollectibles = new Dictionary<string, List<Collectible>>();
        //split collectibles to separate collections
        SplitCollectibles();
        //place special collectibles
        foreach (var level in needChestCollectibles)
        {
            foreach (var coll in level.Value)
            {
                ChestCollectibles[level.Key].Add(coll);
            }
        }
        foreach (var level in heartCollectibles)
        {
            foreach (var coll in level.Value)
            {
                SpawnCollectibles[level.Key].Add(coll);
            }
        }
        //randomize chest and spawned collectibles again
        RandomizeCollectibles(ChestCollectibles);
        RandomizeCollectibles(SpawnCollectibles);
    }

    private void SplitCollectibles()
    {
        foreach (var level in AllCollectibles)
        {
            var trileLevelCollectibles = new List<Collectible>();
            var chestLevelCollectibles = new List<Collectible>();
            var spawnLevelCollectibles = new List<Collectible>();
            foreach (Collectible coll in level.Value)
            {
                switch (coll.LocationType)
                {
                    case "Trile":
                        trileLevelCollectibles.Add(coll);
                        break;
                    case "Chest":
                        chestLevelCollectibles.Add(coll);
                        break;
                    case "Spawn":
                        spawnLevelCollectibles.Add(coll);
                        break;
                }
            }
            TrileCollectibles.Add(level.Key, trileLevelCollectibles);
            ChestCollectibles.Add(level.Key, chestLevelCollectibles);
            SpawnCollectibles.Add(level.Key, spawnLevelCollectibles);
        }
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

    public class Settings
    {
        public bool FullRando;
    }
}