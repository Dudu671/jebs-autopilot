using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AutopilotHost;
using HarmonyLib;
using UnityEngine;

namespace AutopilotKwp
{
    // Kerbal Weather Project replaces the flight integrator's aerodynamics and thermodynamics, and both
    // overrides only serve FlightGlobals.ActiveVessel: any other vessel gets no drag cubes and stock air.
    // This transpiler gives every vessel KWP's air at its own position and its own wind.
    // A separate assembly so the host still loads when Harmony is not installed.
    public static class KwpPatch
    {
        private const string HarmonyId = "MyAutopilot.KwpWeather";
        private const string Prefix = "KerbalWeatherProject.";

        private static bool _applied;

        public static bool Active { get; private set; }

        public static void Apply()
        {
            if (_applied)
            {
                return;
            }

            _applied = true;
            Type registerer = AccessTools.TypeByName(Prefix + "ModularFI_Registerer");
            if (registerer == null)
            {
                Debug.Log("[AutopilotKwp] Kerbal Weather Project not found; weather patch not needed.");
                return;
            }

            try
            {
                // Last, so it runs after Hey Buddy's patch and takes over its calls.
                HarmonyMethod transpiler = new HarmonyMethod(typeof(KwpPatch).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static));
                transpiler.priority = Priority.Last;
                Harmony harmony = new Harmony(HarmonyId);
                int patched = 0;
                foreach (string name in new[] { "UpdateAerodynamicsWx", "UpdateThermodynamicsPreWx" })
                {
                    MethodInfo original = AccessTools.Method(registerer, name);
                    if (original == null)
                    {
                        Debug.LogWarning("[AutopilotKwp] KWP method " + name + " not found; it stays active-vessel only.");
                        continue;
                    }

                    harmony.Patch(original, transpiler: transpiler);
                    patched++;
                }

                Active = patched > 0;
                Debug.Log("[AutopilotKwp] KWP weather applied to every vessel (" + patched + " of 2 methods, per-vessel wind " +
                          (KwpWeather.Bound ? "on" : "off") + ").");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AutopilotKwp] KWP weather patch failed: " + ex);
            }
        }

        // Every ActiveVessel read becomes Subject(fi), and every KWP wind read becomes Wind(fi).
        // The integrator is argument 1 in both methods. Hey Buddy's own Subject(fi) call, if its
        // patch ran first, is taken over too, since this one already covers its formation members.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo activeVessel = AccessTools.PropertyGetter(typeof(FlightGlobals), nameof(FlightGlobals.ActiveVessel));
            MethodInfo subject = AccessTools.Method(typeof(KwpWeather), nameof(KwpWeather.Subject));
            MethodInfo wind = AccessTools.Method(typeof(KwpWeather), nameof(KwpWeather.Wind));
            List<CodeInstruction> code = new List<CodeInstruction>();
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo called = instruction.operand as MethodInfo;
                bool isCall = called != null && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt);
                if (isCall && called == activeVessel)
                {
                    code.Add(WithLabels(new CodeInstruction(OpCodes.Ldarg_1), instruction));
                    code.Add(new CodeInstruction(OpCodes.Call, subject));
                    replaced++;
                    continue;
                }

                if (isCall && IsOtherSubject(called))
                {
                    code.Add(WithLabels(new CodeInstruction(OpCodes.Call, subject), instruction));
                    replaced++;
                    continue;
                }

                if (isCall && IsKwpWind(called))
                {
                    code.Add(WithLabels(new CodeInstruction(OpCodes.Ldarg_1), instruction));
                    code.Add(new CodeInstruction(OpCodes.Call, wind));
                    replaced++;
                    continue;
                }

                code.Add(instruction);
            }

            if (replaced == 0)
            {
                Debug.LogWarning("[AutopilotKwp] " + original.Name + " no longer reads ActiveVessel; KWP may have changed.");
            }

            if (original.Name == "UpdateAerodynamicsWx")
            {
                KwpWeather.LiftInFlight = KeepLiftWind(code);
                if (!KwpWeather.LiftInFlight)
                {
                    Debug.LogWarning("[AutopilotKwp] KWP's lift gate not found; above 10 m the wind still reaches only the drag.");
                }
            }

            return code;
        }

        // KWP corrects the lift of wings and fins for the wind only within 10 m of the terrain:
        //     bool lift = true; if (vessel.heightFromTerrain >= 10) lift = false;
        // Above it the wind reaches only the drag, so the fin keeps the nose on the ground track and a crosswind
        // pushes the aircraft sideways instead of carrying it. The second store becomes true, so the aircraft
        // flies in the moving air at every height and crabs into the wind by itself, as a real one does.
        private static bool KeepLiftWind(List<CodeInstruction> code)
        {
            FieldInfo height = AccessTools.Field(typeof(Vessel), nameof(Vessel.heightFromTerrain));
            const int Window = 24;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldfld || !Equals(code[i].operand, height))
                {
                    continue;
                }

                int flag = -1;
                bool compared = false;
                int end = Math.Min(code.Count - 1, i + Window);
                for (int j = i + 1; j < end; j++)
                {
                    int local;
                    if (flag < 0 && ConstantOf(code[j]) == 1 && StoredLocal(code[j + 1], out local))
                    {
                        flag = local;
                    }
                    else if (code[j].opcode == OpCodes.Ldc_R8 && Equals(code[j].operand, 10.0))
                    {
                        compared = true;
                    }
                    else if (flag >= 0 && compared && ConstantOf(code[j]) == 0 && StoredLocal(code[j + 1], out local) && local == flag)
                    {
                        code[j] = WithLabels(new CodeInstruction(OpCodes.Ldc_I4_1), code[j]);
                        return true;
                    }
                }
            }

            return false;
        }

        private static int ConstantOf(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_0)
            {
                return 0;
            }

            return instruction.opcode == OpCodes.Ldc_I4_1 ? 1 : -1;
        }

        private static bool StoredLocal(CodeInstruction instruction, out int index)
        {
            index = -1;
            if (instruction.opcode == OpCodes.Stloc_0)
            {
                index = 0;
            }
            else if (instruction.opcode == OpCodes.Stloc_1)
            {
                index = 1;
            }
            else if (instruction.opcode == OpCodes.Stloc_2)
            {
                index = 2;
            }
            else if (instruction.opcode == OpCodes.Stloc_3)
            {
                index = 3;
            }
            else if (instruction.opcode == OpCodes.Stloc_S || instruction.opcode == OpCodes.Stloc)
            {
                LocalBuilder builder = instruction.operand as LocalBuilder;
                if (builder != null)
                {
                    index = builder.LocalIndex;
                }
                else if (instruction.operand is byte)
                {
                    index = (byte)instruction.operand;
                }
                else if (instruction.operand is int)
                {
                    index = (int)instruction.operand;
                }
            }

            return index >= 0;
        }

        private static bool IsOtherSubject(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == "Subject" && method.DeclaringType != typeof(KwpWeather) &&
                   method.ReturnType == typeof(Vessel) && parameters.Length == 1 &&
                   typeof(FlightIntegrator).IsAssignableFrom(parameters[0].ParameterType);
        }

        private static bool IsKwpWind(MethodInfo method)
        {
            Type owner = method.DeclaringType;
            return method.Name == "getWSWind" && method.GetParameters().Length == 0 && owner != null &&
                   (owner.FullName == Prefix + "KerbalWxClimo" || owner.FullName == Prefix + "KerbalWxPoint");
        }

        private static CodeInstruction WithLabels(CodeInstruction replacement, CodeInstruction original)
        {
            replacement.labels.AddRange(original.labels);
            replacement.blocks.AddRange(original.blocks);
            return replacement;
        }
    }
}
