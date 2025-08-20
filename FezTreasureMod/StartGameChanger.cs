using FezEngine;
using FezEngine.Services.Scripting;
using FezEngine.Structure;
using FezEngine.Structure.Input;
using FezEngine.Tools;
using FezGame;
using FezGame.Services;
using FezGame.Services.Scripting;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static FezTreasure.TreasureChanger;

namespace FezTreasure
{
    public class StartGameChanger : GameComponent
    {
        private static Settings InputSettings;

        [ServiceDependency]
        public IGameService GameService { private get; set; }

        [ServiceDependency]
        public IGameStateManager GameState { private get; set; }

        public StartGameChanger(Game game) : base(game)
        {
            Type MainMenuType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.MainMenu");

            Hook MainMenuStartGameDetour = new Hook(
                MainMenuType.GetMethod("StartNewGame", BindingFlags.NonPublic | BindingFlags.Instance),
                new Action<Action<object>, object>( (orig, self) => StartGameHooked(orig, self) )
            );
        }

        private void StartGameHooked(Action<object> orig, object self)
        {
            string settingsFile = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\settings.txt") ?? throw new FileNotFoundException("settings.txt not found in " + Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\settings.txt");
            InputSettings = JsonConvert.DeserializeObject<Settings>(settingsFile);
            string inString = File.ReadAllText(Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\collectibles.txt") ?? throw new FileNotFoundException("collectibles.txt not found in " + Directory.GetCurrentDirectory() + "\\Mods\\FezTreasure\\settings.txt");
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\FEZ";
            string outPath = appDataFolder + "\\randomized.txt";

            SetSettings();

            TrileCollectibles = new Dictionary<string, List<Collectible>>();
            ChestCollectibles = new Dictionary<string, List<Collectible>>();
            SpawnCollectibles = new Dictionary<string, List<Collectible>>();
            AllCollectibles = JsonConvert.DeserializeObject<Dictionary<string, List<Collectible>>>(inString);
            if (InputSettings.FullLocationRando)
            {
                RandomizeCollectiblesWithLogic();
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

            GameState.SaveData.Level = "MEMORY_CORE";
            GameState.SaveData.CanOpenMap = true;
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
            Shuffle(fullListTypesAndMaps);
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

        private void RandomizeCollectiblesWithLogic()
        {
            //find and remove special collectibles
            List<String> needChestTypes = new List<String> { "NumberCube", "LetterCube", "TreasureMap", "Tome", "TriSkull" };
            Dictionary<string, List<Collectible>> needChestCollectibles = new Dictionary<string, List<Collectible>>();
            Dictionary<string, List<Collectible>> heartCollectibles = new Dictionary<string, List<Collectible>>();
            foreach (var level in AllCollectibles)
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
            RandomizeCollectibles(AllCollectibles);
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

        public static void Shuffle<T>(IList<T> list)
        {
            Random rng = new Random();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public void CombineCollectibles()
        {
            foreach (var level in TrileCollectibles)
            {
                if (!AllCollectibles.ContainsKey(level.Key))
                {
                    AllCollectibles.Add(level.Key, new List<Collectible>());
                }
                foreach (Collectible coll in level.Value)
                {
                    AllCollectibles[level.Key].Add(coll);
                }
            }
            foreach (var level in ChestCollectibles)
            {
                foreach (Collectible coll in level.Value)
                {
                    AllCollectibles[level.Key].Add(coll);
                }
            }
            foreach (var level in SpawnCollectibles)
            {
                foreach (Collectible coll in level.Value)
                {
                    AllCollectibles[level.Key].Add(coll);
                }
            }
        }

        public static void SplitCollectibles()
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
                        case "ZuPuzzle":
                        case "Fork":
                        case "QRCode":
                        case "ClockRed":
                        case "ClockBlue":
                        case "ClockGreen":
                        case "ClockWhite":
                            spawnLevelCollectibles.Add(coll);
                            break;
                    }
                }
                TrileCollectibles.Add(level.Key, trileLevelCollectibles);
                ChestCollectibles.Add(level.Key, chestLevelCollectibles);
                SpawnCollectibles.Add(level.Key, spawnLevelCollectibles);
            }
        }

        private void SetSettings()
        {
            CodeInputChanger.CodeAllowed = true;
        }

        public class Settings
        {
            public bool FullLocationRando;
        }
    }
}
