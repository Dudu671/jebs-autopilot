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
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo called = instruction.operand as MethodInfo;
                bool isCall = called != null && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt);
                if (isCall && called == activeVessel)
                {
                    yield return WithLabels(new CodeInstruction(OpCodes.Ldarg_1), instruction);
                    yield return new CodeInstruction(OpCodes.Call, subject);
                    replaced++;
                    continue;
                }

                if (isCall && IsOtherSubject(called))
                {
                    yield return WithLabels(new CodeInstruction(OpCodes.Call, subject), instruction);
                    replaced++;
                    continue;
                }

                if (isCall && IsKwpWind(called))
                {
                    yield return WithLabels(new CodeInstruction(OpCodes.Ldarg_1), instruction);
                    yield return new CodeInstruction(OpCodes.Call, wind);
                    replaced++;
                    continue;
                }

                yield return instruction;
            }

            if (replaced == 0)
            {
                Debug.LogWarning("[AutopilotKwp] " + original.Name + " no longer reads ActiveVessel; KWP may have changed.");
            }
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
