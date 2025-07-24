using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using FezGame;
using FezGame.Services;
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

    Dictionary<string, List<Collectible>> allCollectibles { get; set; }

    public static Fez Fez { get; private set; }

    public TreasureChange(Game game) : base(game)
    {
        string collFile = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\randomized.txt");
        allCollectibles = JsonConvert.DeserializeObject<Dictionary<string, List<Collectible>>>(collFile);

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

        //Hook OpenTreasureBeginDetour = new Hook(BeginMethod,
        //    new Action<Action<object>, object>((orig, self) =>
        //    BeginHooked(orig, self)
        //    ));

        //Hook LoadLevelDetour = new Hook(LoadLevelMethod,
        //    new Action<Action<object, string>, object, string>((orig, self, levelName) =>
        //        LoadLevelHooked(orig, self, levelName)
        //    ));

        On.FezGame.Services.GameLevelManager.Load += OnLMLoad;
    }

    private void StartGameHooked(Action<object> orig, object self)
    {
        string inPath = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\collectibles.txt");
        string outPath = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\randomized.txt");

        //dict of levels vs emplacements and types
        allCollectibles = JsonConvert.DeserializeObject<Dictionary<string, List<Collectible>>>(inPath);

        RandomizeCollectibles(allCollectibles);

        File.WriteAllText(outPath, JsonConvert.SerializeObject(allCollectibles, Formatting.Indented));
        orig(self);
    }

    private void BeginHooked(Action<object> orig, object self)
    {
        orig(self);
        return;
        var selfType = self.GetType();
        var chestAO = selfType.GetField("chestAO", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
        if (chestAO != null)
        {
            var actorSettings = chestAO.GetType().GetProperty("ActorSettings", BindingFlags.Public | BindingFlags.Instance).GetValue(chestAO);
            var containedTrileField = actorSettings.GetType().GetProperty("ContainedTrile", BindingFlags.Public | BindingFlags.Instance);
            containedTrileField.SetValue(actorSettings, ActorType.NumberCube);
        }
    }

    public void OnLMLoad(On.FezGame.Services.GameLevelManager.orig_Load orig, GameLevelManager self, string levelName)
    {
        orig(self, levelName);
        var selfType = self.GetType();
        Level levelData = (Level) selfType.GetField("levelData", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
        List<Collectible> curLevelCollectibles = allCollectibles[levelData.Name];

        int bitId = -1;
        int cubeId = -1;
        int antiId = -1;
        int keyId = -1;

        //finds ids
        foreach (var value in levelData.TrileSet.Triles.Values)
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
            foreach (var gameTrile in levelData.Triles)
            {
                TrileEmplacement newTrileEmplacement = new TrileEmplacement(newColl.Emplacement[0], newColl.Emplacement[1], newColl.Emplacement[2]);
                if (!newTrileEmplacement.Equals(gameTrile.Key))
                {
                    continue;
                }
                switch (newColl.Type.ToString())
                {
                    case "GoldenCube":
                        gameTrile.Value.TrileId = bitId;
                        break;
                    case "CubeShard":
                        gameTrile.Value.TrileId = cubeId;
                        break;
                    case "SecretCube":
                        gameTrile.Value.TrileId = antiId;
                        break;
                    case "SkeletonKey":
                        gameTrile.Value.TrileId = keyId;
                        break;

                }
            }
        }
    }

    public static void RandomizeCollectibles(Dictionary<string, List<Collectible>> allCollectibles)
    {
        List<string> fullListCollectibles = new List<string>();
        Dictionary<string, List<Collectible>> allCollectiblesRandom = new Dictionary<string, List<Collectible>>();

        foreach (var level in allCollectibles)
        {
            foreach (var coll in level.Value)
            {
                fullListCollectibles.Add(coll.Type);
            }
        }
        Random rand = new Random();
        int n = fullListCollectibles.Count;
        while (n > 1)
        {
            n--;
            int k = rand.Next(n + 1);
            var value = fullListCollectibles[k];
            fullListCollectibles[k] = fullListCollectibles[n];
            fullListCollectibles[n] = value;
        }
        int j = 0;
        foreach (var level in allCollectibles)
        {
            for (int i = 0; i < level.Value.Count; i++)
            {
                level.Value[i].Type = fullListCollectibles[j];
                j++;
            }
        }
    }

    public class Collectible
    {
        public int[] Emplacement { get; set; }
        public string Type { get; set; }
    }
}