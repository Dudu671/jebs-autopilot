using System;
using KSP.UI.Screens;
using UnityEngine;

namespace AutopilotCore
{
    // Stock application launcher button that shows and hides the main window.
    public sealed class ToolbarButton : IDisposable
    {
        private const int IconSize = 38;

        // 5x7 glyphs, top row first.
        private static readonly string[][] Glyphs =
        {
            new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" },
            new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        };

        private readonly Action<bool> _toggled;
        private ApplicationLauncherButton _button;
        private Texture2D _icon;

        public ToolbarButton(Action<bool> toggled)
        {
            _toggled = toggled;
            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            if (ApplicationLauncher.Ready)
            {
                OnLauncherReady();
            }
        }

        public void Dispose()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            if (_button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(_button);
            }

            _button = null;
            if (_icon != null)
            {
                UnityEngine.Object.Destroy(_icon);
                _icon = null;
            }
        }

        private void OnLauncherReady()
        {
            if (_button != null || ApplicationLauncher.Instance == null)
            {
                return;
            }

            if (_icon == null)
            {
                _icon = BuildIcon();
            }

            _button = ApplicationLauncher.Instance.AddModApplication(
                () => _toggled(true),
                () => _toggled(false),
                null,
                null,
                null,
                null,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                _icon);
        }

        // "JAP" at 2x scale, centered.
        private static Texture2D BuildIcon()
        {
            const int scale = 2;
            const int gap = 2;
            Texture2D texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[IconSize * IconSize];
            Color32 ink = new Color32(255, 255, 255, 255);
            int width = (Glyphs.Length * 5 * scale) + ((Glyphs.Length - 1) * gap);
            int left = (IconSize - width) / 2;
            int top = (IconSize + (7 * scale)) / 2;

            for (int g = 0; g < Glyphs.Length; g++)
            {
                int x0 = left + (g * ((5 * scale) + gap));
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (Glyphs[g][row][col] != '1')
                        {
                            continue;
                        }

                        for (int dy = 0; dy < scale; dy++)
                        {
                            for (int dx = 0; dx < scale; dx++)
                            {
                                // Texture rows count up from the bottom.
                                int x = x0 + (col * scale) + dx;
                                int y = top - (row * scale) - dy - 1;
                                pixels[(y * IconSize) + x] = ink;
                            }
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
