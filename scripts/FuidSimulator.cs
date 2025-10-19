using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class FluidSimulator : MonoBehaviour
{

    [Header("Resolution [match with rect transform]")]
    public int resolution = 1000;

    [Header("Simulation Bounds [per pixel]")]
    public Vector2 boundsPos = Vector2.zero;
    public Vector2 boundsSize = new Vector2(1000, 1000);
    private Rect simRect;

    //public AspectDriven aspectDriven; //TODO - actually address the issue by clampiing the simulation area in the texture
    //public enum AspectDriven { width, height }

    [Header("Simulation Settings")]
    public float force = 100f;
    public float radius = 2f;
    private float injRadius = 0.02f;   // injection brush radius (in UV space)
    public float densityAmount = 1;
    public int jacobiIterations = 20;
    public float velocityDissipation = 0.99f;
    public float densityDissipation = 0.999f;
    public float diffusionRate = 0.0001f;

    [Header("Color Settings")]
    public Gradient densityGradient;
    private Texture2D gradientTex;              // internal lookup texture
    public Color backgroundColor = Color.black; // background, can be transparent
    public float absConst = 2f;


    [Header("Compute Shader")]
    public ComputeShader computeShader;

    //[Header("Display")]
    private RawImage rawOwo;           // UI element to display sim
    private RenderTexture renderTexture; // main display RT (optional if you blit density directly)

    [Header("Debug View")]
    public DebugTextureType debugTextureType;
    public enum DebugTextureType { Dye, Density, Velocity, Pressure, Divergence }
    public bool useKeyboardShortcuts = false;

    // --- Internal RenderTextures ---
    [HideInInspector] public RenderTexture velocity;
    [HideInInspector] public RenderTexture velocityTemp;
    [HideInInspector] public RenderTexture velocityDebug;
    [HideInInspector] public RenderTexture density;
    [HideInInspector] public RenderTexture densityTemp;
    [HideInInspector] public RenderTexture pressure;
    [HideInInspector] public RenderTexture pressureTemp;
    [HideInInspector] public RenderTexture divergence;

    [HideInInspector] public RenderTexture dyeTexture;

    // Track current resolution so we can recreate RTs if it changes
    private int currentWidth;
    private int currentHeight;

    // for mouse tracking
    private Vector2 lastUV;
    private bool hasLastUV = false;


    void OnDisable() => ReleaseAll();
    void OnDestroy() => ReleaseAll();


    [ContextMenu("Reset Simulation")]
    public void ResetSimulation()
    {
        // Clear density
        ClearRenderTexture(density);
        ClearRenderTexture(densityTemp);

        // Clear velocity
        ClearRenderTexture(velocity);
        ClearRenderTexture(velocityTemp);

        // Clear pressure
        ClearRenderTexture(pressure);
        ClearRenderTexture(pressureTemp);

        // Clear divergence
        ClearRenderTexture(divergence);
    }

    void ClearRenderTexture(RenderTexture rt)
    {
        if (rt == null) return;
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = active;
    }



    private void Start()
    {
        CreateOrUpdateRTs();

        rawOwo = GetComponent<UnityEngine.UI.RawImage>();

        renderTexture = new RenderTexture(resolution, resolution, 24);
        renderTexture.enableRandomWrite = true;
        renderTexture.filterMode = FilterMode.Point;
        renderTexture.wrapMode = TextureWrapMode.Repeat;
        renderTexture.Create();

        //rawOwo.texture = renderTexture;
        ChangeDisplayTexture();

        //RunComputeShader();
        simRect = new Rect(boundsPos, boundsSize);
    }

    private void FixedUpdate()
    {
        // if you change resolution in inspector while playing, recreate RTs
        if (resolution != currentWidth || resolution != currentHeight)
            CreateOrUpdateRTs();

        //UpdateTexture();
        //RunComputeShader();


        if(debugTextureType == DebugTextureType.Velocity) {
            int kernel = computeShader.FindKernel("VisualizeVelocity");
            computeShader.SetTexture(kernel, "VelocityRead", velocity);
            computeShader.SetTexture(kernel, "VelocityDebug", velocityDebug);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        ChangeDisplayTexture();

        SimStep();
    }

    void ChangeDisplayTexture()
    {
        switch (debugTextureType)
        {
            case DebugTextureType.Dye: rawOwo.texture = dyeTexture; break;
            case DebugTextureType.Density: rawOwo.texture = density; break;
            case DebugTextureType.Velocity: rawOwo.texture = velocityDebug; break;
            case DebugTextureType.Pressure: rawOwo.texture = pressure; break;
            case DebugTextureType.Divergence: rawOwo.texture = divergence; break;
        }
    }

    void Update()
    {
        //ChangeAspectDrive();

        HandleMouseInput(rawOwo.rectTransform);
        if (useKeyboardShortcuts && Input.GetKeyDown(KeyCode.R)) ResetSimulation();
    }


    //void ChangeAspectDrive()
    //{
    //    if (aspectDriven == AspectDriven.width) FitAsspectWidth();
    //    else FitAsspectHeight();
    //}

    //void FitAsspectWidth()
    //{
    //    float aspect = resolution / resolution;
    //    rawOwo.rectTransform.sizeDelta = new Vector2(resolution, resolution / aspect);
    //}
    //void FitAsspectHeight()
    //{
    //    float aspect = resolution / resolution;
    //    rawOwo.rectTransform.sizeDelta = new Vector2(resolution, resolution / aspect);
    //}



    void SetGlobals()
    {
        injRadius = radius / 100;

        computeShader.SetVector("InvResolution", new Vector2(1.0f / resolution, 1.0f / resolution));
        computeShader.SetFloat("DeltaTime", Time.deltaTime);
        computeShader.SetFloat("VelocityDissipation", velocityDissipation);
        computeShader.SetFloat("DensityDissipation", densityDissipation);

        float alpha = diffusionRate * resolution * resolution;
        float beta = 4.0f + alpha;
        computeShader.SetFloat("DiffuseAlpha", alpha);
        computeShader.SetFloat("DiffuseBeta", beta);

        computeShader.SetVector("simRect", new Vector4((uint)simRect.width, (uint)simRect.height, (uint)simRect.x, simRect.y));
    }

    void SimStep()
    {
        SetGlobals();

        // 1. Inject forces (already handled in HandleMouseInput before this step)
        // in Update()

        // 2. Diffuse velocity
        for (int i = 0; i < jacobiIterations; i++)
        {
            int kernel = computeShader.FindKernel("DiffuseVelocity");
            computeShader.SetTexture(kernel, "VelocityRead", velocity);
            computeShader.SetTexture(kernel, "VelocityWrite", velocityTemp);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);

            Swap(ref velocity, ref velocityTemp);
        }

        // 3. Project (make velocity divergence-free)
        Project();

        // 4. Advect velocity
        {
            int kernel = computeShader.FindKernel("AdvectVelocity");
            computeShader.SetTexture(kernel, "VelocityRead", velocity);
            computeShader.SetTexture(kernel, "VelocityWrite", velocityTemp);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);

            Swap(ref velocity, ref velocityTemp);
        }

        // 5. Project again
        Project();

        // 6. Diffuse density
        for (int i = 0; i < jacobiIterations; i++)
        {
            int kernel = computeShader.FindKernel("DiffuseDensity");
            computeShader.SetTexture(kernel, "DensityRead", density);
            computeShader.SetTexture(kernel, "DensityWrite", densityTemp);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);

            Swap(ref density, ref densityTemp);
        }

        // 7. Advect density
        {
            int kernel = computeShader.FindKernel("AdvectDensity");
            computeShader.SetTexture(kernel, "DensityRead", density);
            computeShader.SetTexture(kernel, "VelocityRead", velocity);
            computeShader.SetTexture(kernel, "DensityWrite", densityTemp);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);

            Swap(ref density, ref densityTemp);
        }

        Colorize();
    }

    void Project()
    {
        // compute divergence
        {
            int kernel = computeShader.FindKernel("ComputeDivergence");
            computeShader.SetTexture(kernel, "VelocityRead", velocity);
            computeShader.SetTexture(kernel, "DivergenceWrite", divergence);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        // solve pressure using Jacobi
        for (int i = 0; i < jacobiIterations; i++)
        {
            int kernel = computeShader.FindKernel("PressureJacobi");
            computeShader.SetTexture(kernel, "PressureRead", pressure);
            computeShader.SetTexture(kernel, "DivergenceRead", divergence);
            computeShader.SetTexture(kernel, "PressureWrite", pressureTemp);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);

            Swap(ref pressure, ref pressureTemp);
        }

        // subtract pressure gradient from velocity
        {
            int kernel = computeShader.FindKernel("SubtractGradient");
            computeShader.SetTexture(kernel, "VelocityRead", velocity);
            computeShader.SetTexture(kernel, "PressureRead", pressure);
            computeShader.SetTexture(kernel, "VelocityWrite", velocityTemp);
            computeShader.SetInts("Resolution", resolution, resolution);

            int groupsX = Mathf.CeilToInt(resolution / 8.0f);
            int groupsY = Mathf.CeilToInt(resolution / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);

            Swap(ref velocity, ref velocityTemp);
        }
    }


    void HandleMouseInput(RectTransform targetRect)
    {
        if (!Input.GetMouseButton(0))
        {
            hasLastUV = false;
            return;
        }

        if (!GetMouseUV(targetRect, out Vector2 uv)) return;

        if (hasLastUV)
        {
            int kernel = computeShader.FindKernel("AddForceLine");
            computeShader.SetTexture(kernel, "DensityWrite", density);
            computeShader.SetTexture(kernel, "VelocityWrite", velocity);

            computeShader.SetVector("LineStart", lastUV);
            computeShader.SetVector("LineEnd", uv);

            // direction of drag as velocity
            Vector2 drag = (uv - lastUV) * new Vector2(density.width, density.height);
            computeShader.SetVector("ForceVector", drag * force * Time.deltaTime);

            computeShader.SetFloat("Radius", injRadius);
            computeShader.SetFloat("DensityAmount", densityAmount * Time.deltaTime);
            computeShader.SetInts("Resolution", density.width, density.height);


            int groupsX = Mathf.CeilToInt(density.width / 8.0f);
            int groupsY = Mathf.CeilToInt(density.height / 8.0f);
            computeShader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        lastUV = uv;
        hasLastUV = true;
    }


    bool GetMouseUV(RectTransform rectTransform, out Vector2 uv)
    {
        uv = Vector2.zero;
        Vector2 localPoint;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, Input.mousePosition, Camera.main, out localPoint))
            return false;

        Rect rect = rectTransform.rect;
        Vector2 normalized = new Vector2(
            (localPoint.x - rect.xMin) / rect.width,
            (localPoint.y - rect.yMin) / rect.height
        );

        uv = normalized;
        return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    }


    void UpdateGradientTexture()
    {
        if (densityGradient == null)
            return;

        if (gradientTex == null || gradientTex.width != 256)
        {
            gradientTex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            gradientTex.wrapMode = TextureWrapMode.Clamp;
        }

        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            Color col = densityGradient.Evaluate(t);
            gradientTex.SetPixel(i, 0, col);
        }
        gradientTex.Apply();
    }


    void Colorize()
    {
        int kernel = computeShader.FindKernel("ColorizeDensity");

        computeShader.SetTexture(kernel, "DensityRead", density);
        computeShader.SetTexture(kernel, "DyeWrite", dyeTexture);

        // gradient lookup
        UpdateGradientTexture();
        computeShader.SetTexture(kernel, "DensityGradient", gradientTex);

        computeShader.SetVector("BackgroundColor", backgroundColor);
        computeShader.SetFloat("absConst", absConst);
        computeShader.SetInts("Resolution", resolution, resolution);

        int groupsX = Mathf.CeilToInt(density.width / 8.0f);
        int groupsY = Mathf.CeilToInt(density.height / 8.0f);
        computeShader.Dispatch(kernel, groupsX, groupsY, 1);
    }


    private void CreateOrUpdateRTs()
    {
        // clamp reasonable minimum
        resolution = Mathf.Max(8, resolution);
        resolution = Mathf.Max(8, resolution);

        // if unchanged, nothing to do
        if (currentWidth == resolution && currentHeight == resolution && velocity != null) return;

        // free old ones (safe)
        ReleaseAll();

        currentWidth = resolution; 
        currentHeight = resolution;

        // Create the RTs
        velocity = CreateRT(currentWidth, currentHeight, RenderTextureFormat.ARGBFloat); // store vec2 in RG
        velocityTemp = CreateRT(currentWidth, currentHeight, RenderTextureFormat.ARGBFloat);
        velocityDebug = CreateRT(resolution, resolution, RenderTextureFormat.ARGBFloat);
        density = CreateRT(currentWidth, currentHeight, RenderTextureFormat.RFloat);    // scalar dye
        densityTemp = CreateRT(currentWidth, currentHeight, RenderTextureFormat.RFloat);
        pressure = CreateRT(currentWidth, currentHeight, RenderTextureFormat.RFloat);
        pressureTemp = CreateRT(currentWidth, currentHeight, RenderTextureFormat.RFloat);
        divergence = CreateRT(currentWidth, currentHeight, RenderTextureFormat.RFloat);
        dyeTexture = CreateRT(resolution, resolution, RenderTextureFormat.ARGBFloat);

        // optional: clear them (a simple clear with Graphics.Blit or compute clear kernel is fine)
        // e.g. Graphics.SetRenderTarget(density); GL.Clear(false, true, Color.clear);
    }

    RenderTexture CreateRT(int w, int h, RenderTextureFormat format)
    {
        // Fallbacks: some platforms might not support ARGBFloat/RFloat; check in runtime if needed.
        var rt = new RenderTexture(w, h, 0, format)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        rt.Create();
        return rt;
    }

    // ---------- Release ----------
    void ReleaseAll()
    {
        Release(ref velocity);
        Release(ref velocityTemp);
        Release(ref density);
        Release(ref densityTemp);
        Release(ref pressure);
        Release(ref pressureTemp);
        Release(ref divergence);
        currentWidth = 0;
        currentHeight = 0;
    }

    void Release(ref RenderTexture rt)
    {
        if (rt == null) return;
        if (rt.IsCreated()) rt.Release();
        DestroyImmediate(rt);
        rt = null;
    }

    // ---------- Ping-Pong swap ----------
    public void Swap(ref RenderTexture a, ref RenderTexture b)
    {
        var t = a; a = b; b = t;
    }

    void RunComputeShader() // shows uv coordinates on texture
    {
        int kernelHandle = computeShader.FindKernel("CSMain");
        computeShader.SetVector("TextureSize", new Vector2(resolution, resolution));

        computeShader.SetTexture(kernelHandle, "Result", renderTexture);
        computeShader.Dispatch(kernelHandle, Mathf.CeilToInt(resolution / 8.0f), Mathf.CeilToInt(resolution / 8.0f), 1);

        rawOwo.texture = renderTexture;
    }


    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;

        // Draw a 2D rectangle as a thin cube (for visibility)
        Vector2 center = new Vector2((boundsPos.x - ((GetComponent<RectTransform>().rect.width - boundsSize.x)/2)) /(100 / GetComponent<RectTransform>().localScale.x) + GetComponent<RectTransform>().position.x, (boundsPos.y - ((GetComponent<RectTransform>().rect.height - boundsSize.y) / 2)) / (100 / GetComponent<RectTransform>().localScale.y) + GetComponent<RectTransform>().position.y);
        Vector2 size = new Vector2(boundsSize.x/ (100 / GetComponent<RectTransform>().localScale.x), boundsSize.y/ (100 / GetComponent<RectTransform>().localScale.y));
        Gizmos.DrawWireCube(center, size);


        //// Optional: fill with translucent overlay
        //Gizmos.color = new Color(0, 1, 1, 0.1f);
        //Gizmos.DrawCube(center, size);
    }

}
