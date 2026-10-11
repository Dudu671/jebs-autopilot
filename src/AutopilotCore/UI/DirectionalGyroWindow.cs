using UnityEngine;

namespace AutopilotCore
{
    public sealed class DirectionalGyroWindow : InstrumentWindow
    {
        private const float DialSize = 240f;

        private static readonly Vector2 DialCenter = new Vector2(130f, 128f);
        private static readonly Color TargetColor = new Color(0.2f, 0.85f, 1f, 1f);

        private bool _texturesLoaded;
        private Texture2D _face;
        private Texture2D _card;
        private Texture2D _target;
        private Texture2D _overlay;

        public DirectionalGyroWindow()
            : base(0x4A454254, "Directional Gyro", 260, 286, 440f, 80f)
        {
        }

        protected override void Render(Canvas2D c, FlightData d)
        {
            if (!_texturesLoaded)
            {
                _texturesLoaded = true;
                _face = TextureLoader.Load("dg_face.png");
                _card = TextureLoader.Load("dg_card.png");
                _target = TextureLoader.Load("dg_target.png");
                _overlay = TextureLoader.Load("dg_overlay.png");
            }

            if (_card == null)
            {
                c.Text("Gyro textures not found", 130f, 130f, 14, Color.white, TextAlign.Center);
                return;
            }

            Vector2 size = new Vector2(DialSize, DialSize);
            c.DrawTexture(_face, DialCenter, size, 0f, Color.white);
            c.DrawTexture(_card, DialCenter, size, -d.Heading, Color.white);
            if (d.HasHeadingTarget)
            {
                Color tint = TargetColor;
                tint.a = d.HeadingOn ? 1f : 0.45f;
                c.DrawTexture(_target, DialCenter, size, d.HeadingTarget - d.Heading, tint);
            }

            c.DrawTexture(_overlay, DialCenter, size, 0f, Color.white);

            c.Text("HDG " + Format.Heading(d.Heading), 70f, 268f, 16, Color.white, TextAlign.Center);
            if (d.HasHeadingTarget)
            {
                c.Text("TGT " + Format.Heading(d.HeadingTarget), 190f, 268f, 16, TargetColor, TextAlign.Center);
            }
        }

        protected override void OnDispose()
        {
            TextureLoader.Destroy(ref _face);
            TextureLoader.Destroy(ref _card);
            TextureLoader.Destroy(ref _target);
            TextureLoader.Destroy(ref _overlay);
            _texturesLoaded = false;
        }
    }
}
