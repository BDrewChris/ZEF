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
     * This property looks a little nasty, but is useful for handling each type of collectible in one file
     * Top Layer is collectible location type (e.g. chests, triles, etc.)
     * Second Layer down is level where collectible is contained (arch, owl, nature_hub, etc.)
     * Third Layer is a list of all collectibles that adhere to those criteria
     * See collectibles.txt for reference
     */
    public Dictionary<string, Dictionary<string, List<Collectible>>> AllCollectibles { get; set; }

    public Dictionary<string, List<Collectible>> TrileCollectibles { get; set; }

    public Dictionary<string, List<Collectible>> ChestCollectibles { get; set; }

    public Level CurrentLevel { get; set; }

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
            collFile = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\collectibles.txt");
        }
        AllCollectibles = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<Collectible>>>>(collFile);
        TrileCollectibles = AllCollectibles["Triles"];
        ChestCollectibles = AllCollectibles["Chests"];

        Fez = (Fez)game;

        BindingFlags NonPublicFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        MainMenuType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.MainMenu");
        MethodBase StartGameMethod = MainMenuType.GetMethod("StartNewGame", NonPublicFlags);
        
        Hook MainMenuStartGameDetour = new Hook(StartGameMethod,
            new Action<Action<object>, object>((orig, self) =>
            StartGameHooked(orig, self)
            ));

        OpenTreasureType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.Actions.OpenTreasure");

        MethodBase ActMethod = OpenTreasureType.GetMethod("Act", NonPublicFlags);
        MethodBase BeginMethod = OpenTreasureType.GetMethod("Begin", NonPublicFlags);

        Hook OpenTreasureBeginDetour = new Hook(BeginMethod,
            new Action<Action<object>, object>((orig, self) =>
            BeginHooked(orig, self)
            ));

        //Hook LoadLevelDetour = new Hook(LoadLevelMethod,
        //    new Action<Action<object, string>, object, string>((orig, self, levelName) =>
        //        LoadLevelHooked(orig, self, levelName)
        //    ));

        On.FezGame.Services.GameLevelManager.Load += OnLMLoad;
    }

    private void StartGameHooked(Action<object> orig, object self)
    {
        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\FEZ";
        string inString = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\collectibles.txt");
        string outPath = appDataFolder + "\\randomized.txt";

        AllCollectibles = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<Collectible>>>>(inString);
        Dictionary<string, List<Collectible>> trileCollectibles = AllCollectibles["Triles"];
        Dictionary<string, List<Collectible>> chestCollectibles = AllCollectibles["Chests"];

        RandomizeCollectibles(trileCollectibles);
        RandomizeCollectibles(chestCollectibles);

        AllCollectibles["Triles"] = trileCollectibles;
        AllCollectibles["Chests"] = chestCollectibles;

        File.WriteAllText(outPath, JsonConvert.SerializeObject(AllCollectibles, Formatting.Indented));
        orig(self);
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

    private static void RandomizeCollectibles(Dictionary<string, List<Collectible>> collectibles)
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

    public class Collectible
    {
        public int[] Emplacement;

        public float[] ArtObjectPosition;

        public string Type;

        public string TreasureMapName = string.Empty;
    }
}