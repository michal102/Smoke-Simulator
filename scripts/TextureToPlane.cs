using UnityEngine;
using UnityEngine.UI;

public class TextureToPlane : MonoBehaviour
{
    public int width = 0, height = 0;
    //Texture2D fluidTexture;
    //SpriteRenderer spriteRenderer;
    //Sprite fluidSprite;

    public ComputeShader computeShader;
    private RenderTexture renderTexture;
    //public Material material;

    RawImage rawOwo;

    private void Start()
    {
        rawOwo = GetComponent<UnityEngine.UI.RawImage>();
        //spriteRenderer = GetComponent<SpriteRenderer>();

        //fluidTexture = new Texture2D(width, height);

        renderTexture = new RenderTexture(width, height, 24);
        renderTexture.enableRandomWrite = true;
        renderTexture.filterMode = FilterMode.Point;
        renderTexture.wrapMode = TextureWrapMode.Repeat;
        renderTexture.Create();

        //material = new Material(Shader.Find("Unlit/Texture"));
        //material.mainTexture = renderTexture;
        //spriteRenderer.material = material;

        //fluidSprite = Sprite.Create(fluidTexture, new Rect(0, 0, fluidTexture.width, fluidTexture.height), new Vector2(0.5f, 0.5f));
        //spriteRenderer.sprite = fluidSprite;

        rawOwo.texture = renderTexture;

        RunComputeShader();
    }

    private void FixedUpdate()
    {
        //UpdateTexture();
        RunComputeShader();
    }

    //void UpdateTexture()
    //{
    //    Color[] pixels = new Color[width * height];

        
    //    for (int i = 0; i < pixels.Length; i++)
    //    {
    //        float value = Mathf.PerlinNoise(i % width * 0.01f, i / width * 0.01f);
    //        pixels[i] = new Color(value, value, value, 1.0f);
    //    }

        
    //    fluidTexture.SetPixels(pixels);
    //    fluidTexture.Apply();
    //}

    void RunComputeShader()
    {
        int kernelHandle = computeShader.FindKernel("CSMain");
        computeShader.SetVector("TextureSize", new Vector2(width, height));

        computeShader.SetTexture(kernelHandle, "Result", renderTexture);
        computeShader.Dispatch(kernelHandle, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

        rawOwo.texture = renderTexture;

        //UpdateTextureFromRenderTexture();

        // Apply the new texture as a sprite
        //fluidSprite = Sprite.Create(fluidTexture, new Rect(0, 0, fluidTexture.width, fluidTexture.height), new Vector2(0.5f, 0.5f));
        //spriteRenderer.sprite = fluidSprite;
    }


    //void UpdateTextureFromRenderTexture()
    //{
    //    RenderTexture.active = renderTexture;
    //    fluidTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
    //    fluidTexture.Apply();
    //    RenderTexture.active = null;
    //}
}
