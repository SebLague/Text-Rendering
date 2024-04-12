using UnityEngine;
using SebText.Rendering;

namespace SebText.Demo
{
    public class TextRenderComponent : MonoBehaviour
    {
        [Header("Display Settings")]
        public FontExampleLibrary.TypeFace typeface;
        public FontExampleLibrary.Variant variant;

        [Multiline(6)]
        public string displayString;
        public Color colour;
        public TextRenderer.LayoutSettings layoutSettings = new (1, 1, 1, 1);
      
        TextRenderer textRenderer;
        bool settingsChangedSinceLastUpdate;

        void Awake()
        {
            string path = FontExampleLibrary.GetFontPath(typeface, variant);
            textRenderer = new TextRenderer(path, displayString, layoutSettings);
        }

        void Update()
        {
         
            // Update settings
            if (settingsChangedSinceLastUpdate)
            {
                string fontPath = FontExampleLibrary.GetFontPath(typeface, variant);
                textRenderer.Update(displayString, fontPath, layoutSettings);
                settingsChangedSinceLastUpdate = false;
            }

            // Draw
            textRenderer.Render(transform.position, colour);
        }

        private void OnDestroy()
        {
            textRenderer.Release();
        }

        private void OnValidate()
        {
            settingsChangedSinceLastUpdate = true;
        }

    }
}