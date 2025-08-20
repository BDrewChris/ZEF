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

namespace FezTreasure
{
    public class ViewpointChanger : GameComponent
    {
        [ServiceDependency]
        public IGameCameraManager Camera { private get; set; }

        [ServiceDependency]
        public IGameLevelManager LevelManager { private get; set; }

        [ServiceDependency]
        public IGameStateManager GameState { private get; set; }

        public static SortedSet<int> AllowedViewpoints { get; set; }
        public static List<Viewpoint> CurRoomAllowedViewpoints { get; set; }
        public static Dictionary<string, List<Viewpoint>> AllLevelAllowedViewpoints { get; set; }

        public bool LeftRotateAllowed { get; set; }
        public bool RightRotateAllowed { get; set; }

        public ViewpointChanger(Game game) : base(game)
        {
            On.FezGame.Services.GameLevelManager.ChangeLevel += (orig, self, levelName) =>
            {
                orig(self, levelName);
                FindAllowedViewpoints();
            };

            BindingFlags NonPublicFlags = BindingFlags.Instance | BindingFlags.NonPublic;

            Type PlayerCameraType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.PlayerCameraControl");
            /*
            Hook RotateRightDetour = new Hook(
                PlayerCameraType.GetMethod("RotateViewLeft", NonPublicFlags),
                new Action<Action<object>, object>((orig, self) =>
                {
                    if (CurRoomAllowedViewpoints.Count == 1) { return; }
                    int curViewpointIndex = CurRoomAllowedViewpoints.IndexOf(Camera.Viewpoint);
                    if (curViewpointIndex == 0 || curViewpointIndex == -1)
                    {
                        
                        Camera.ChangeViewpoint(CurRoomAllowedViewpoints[CurRoomAllowedViewpoints.Count - 1]);
                    }
                    else
                    {
                        Camera.ChangeViewpoint(CurRoomAllowedViewpoints[curViewpointIndex - 1]);
                    }
                }
            ));
            */
            /*
            Hook RotateLeftDetour = new Hook(
                PlayerCameraType.GetMethod("RotateViewRight", NonPublicFlags),
                new Action<Action<object>, object>((orig, self) =>
                {
                    if (CurRoomAllowedViewpoints.Count == 1) { return; }
                    int curViewpointIndex = CurRoomAllowedViewpoints.IndexOf(Camera.Viewpoint);
                    if (curViewpointIndex == CurRoomAllowedViewpoints.Count - 1 || curViewpointIndex == -1)
                    {
                        Camera.ChangeViewpoint(CurRoomAllowedViewpoints[0]);
                    }
                    else
                    {
                        Camera.ChangeViewpoint(CurRoomAllowedViewpoints[curViewpointIndex + 1]);
                    }
                }
            ));
            */
            AllowedViewpoints = new SortedSet<int>
            {
                1,
                2,
                3,
                4
            };

            CurRoomAllowedViewpoints = new List<Viewpoint>();

            AllLevelAllowedViewpoints = new Dictionary<string, List<Viewpoint>>();
        }

        public void FindAllowedViewpoints()
        {
            if (AllLevelAllowedViewpoints.ContainsKey(LevelManager.Name))
            {
                CurRoomAllowedViewpoints = AllLevelAllowedViewpoints[TreasureChanger.CurrentLevel.Name];
                return;
            }
            CurRoomAllowedViewpoints.Clear();
            Viewpoint curViewpoint = Camera.Viewpoint;
            for (int i = 1; i < 5; i++)
            {
                if (AllowedViewpoints.Contains(i))
                {
                    CurRoomAllowedViewpoints.Add(curViewpoint);
                }
                switch (curViewpoint)
                {
                    case Viewpoint.Front:
                        curViewpoint = Viewpoint.Right;
                        break;
                    case Viewpoint.Right:
                        curViewpoint = Viewpoint.Back;
                        break;
                    case Viewpoint.Back:
                        curViewpoint = Viewpoint.Left;
                        break;
                    case Viewpoint.Left:
                        curViewpoint = Viewpoint.Front;
                        break;
                }
            }
            AllLevelAllowedViewpoints.Add(LevelManager.Name, CurRoomAllowedViewpoints);
        }
    }
}
