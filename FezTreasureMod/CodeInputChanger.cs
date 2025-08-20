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
    public class CodeInputChanger : GameComponent
    {
        public static Dictionary<string, string> ShuffledCodeInputs { get; set; }

        public static bool CodeAllowed;

        public CodeInputChanger(Game game) : base(game)
        {
            //hook for code inputting
            Type PatternTesterType = Assembly.GetAssembly(typeof(PatternTester)).GetType("FezEngine.Structure.Input.PatternTester");
            MethodBase TestCodeMethod = PatternTesterType.GetMethod("Test", new Type[] { typeof(IList<CodeInput>), typeof(CodeInput[]) });
            MethodBase TestVibrationMethod = PatternTesterType.GetMethod("Test", new Type[] { typeof(IList<VibrationMotor>), typeof(VibrationMotor[]) });

            Hook TestCodeDetour = new Hook(TestCodeMethod,
                new Func<Func<IList<CodeInput>, CodeInput[], bool>, IList<CodeInput>, CodeInput[], bool>((orig, input, pattern) =>
                {
                    List<CodeInput> newInput = new List<CodeInput>();
                    if (CodeAllowed)
                    {
                        for (int i = 0; i < input.Count; i++)
                        {
                            newInput.Add(GetNewCodeInput(input[i]));
                        }
                    }
                    return orig(newInput, pattern);
                }
            ));

            //hook for code machine
            Type CodeMachineType = Assembly.GetAssembly(typeof(Fez)).GetType("FezGame.Components.CodeMachineHost");
            MethodBase OnInputMethod = CodeMachineType.GetMethod("OnInput", BindingFlags.NonPublic | BindingFlags.Instance);

            Hook OnInputDetour = new Hook(OnInputMethod,
                new Action<Action<object, CodeInput>, object, CodeInput>((orig, self, oldInput) => { orig(self, GetNewCodeInput(oldInput)); }));

            //just for testing right now - will randomize in future
            ShuffledCodeInputs = new Dictionary<string, string>
            {
                { "Jump", "Jump" },
                { "SpinRight", "SpinRight" },
                { "SpinLeft", "SpinLeft" },
                { "Left", "Left" },
                { "Right", "Right" },
                { "Up", "Up" },
                { "Down", "Down" }
            };
        }

        private CodeInput GetNewCodeInput(CodeInput oldInput)
        {
            bool shuffledInput = ShuffledCodeInputs.ContainsKey(oldInput.ToString());
            if (shuffledInput)
            {
                return (CodeInput)Enum.Parse(typeof(CodeInput), ShuffledCodeInputs[oldInput.ToString()]);
            }
            return CodeInput.None;
        }
    }
}
