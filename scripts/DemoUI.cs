using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsUI : MonoBehaviour
{
    [Header("UI References")]
    public Slider sizeSlider;
    public Slider forceSlider;
    public TMP_Dropdown gradientDropdown;
    public TMP_InputField iterationsInput;

    [Header("Simulation Reference")]
    public FluidSimulator simulation;

    [Header("Settings")]
    public float objectSize;
    public Vector2 maxsize;
    public float forceValue;
    public Vector2 maxvalue;
    public float iterations;
    public Gradient[] gradients;
    public Gradient selectedGradient;

    // Store initial values for reset
    private float initialSize;
    private float initialForce;
    private float initialIterations;
    private Gradient initialGradient;

    void Start()
    {
        // Initialize from simulation
        if (simulation != null)
        {
            objectSize = simulation.smallRadius;
            forceValue = simulation.force;
            iterations = simulation.jacobiIterations;
            selectedGradient = simulation.densityGradient;

            // Save initial values
            initialSize = objectSize;
            initialForce = forceValue;
            initialIterations = iterations;
            initialGradient = selectedGradient;
        }

        // --- Size Slider ---
        sizeSlider.minValue = maxsize.x;
        sizeSlider.maxValue = maxsize.y;
        sizeSlider.value = objectSize;
        sizeSlider.onValueChanged.AddListener(OnSizeChanged);

        // --- Force Slider ---
        forceSlider.minValue = maxvalue.x;
        forceSlider.maxValue = maxvalue.y;
        forceSlider.value = forceValue;
        forceSlider.onValueChanged.AddListener(OnForceChanged);

        // --- Gradient Dropdown ---
        gradientDropdown.RefreshShownValue();
        gradientDropdown.onValueChanged.AddListener(OnGradientChanged);

        // --- Iterations Input ---
        if (iterationsInput != null)
        {
            iterationsInput.text = iterations.ToString();
            iterationsInput.onEndEdit.AddListener(OnIterationsChanged);
        }

        ApplyToSimulation();
    }

    private void Update()
    {
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.R))
            ResetSettings();
    }

    void OnSizeChanged(float value)
    {
        objectSize = value;
        ApplyToSimulation();
    }

    void OnForceChanged(float value)
    {
        forceValue = value;
        ApplyToSimulation();
    }

    void OnGradientChanged(int index)
    {
        if (index >= 0 && index < gradients.Length)
        {
            selectedGradient = gradients[index];
            ApplyToSimulation();
        }
    }

    void OnIterationsChanged(string value)
    {
        if (float.TryParse(value, out float newIterations))
        {
            iterations = Mathf.Max(1, newIterations);
            ApplyToSimulation();
        }
        else
        {
            // Reset text if invalid input
            iterationsInput.text = iterations.ToString();
        }
    }

    void ApplyToSimulation()
    {
        if (!Application.isPlaying) return;

        if (simulation != null)
        {
            simulation.smallRadius = objectSize;
            simulation.force = forceValue;
            simulation.densityGradient = selectedGradient;
            simulation.jacobiIterations = (int)iterations;
        }
    }

    public void ResetSettings()
    {
        objectSize = initialSize;
        forceValue = initialForce;
        iterations = initialIterations;
        selectedGradient = initialGradient;

        // Update UI
        sizeSlider.value = objectSize;
        forceSlider.value = forceValue;
        gradientDropdown.value = 0;
        iterationsInput.text = iterations.ToString();

        ApplyToSimulation();
    }

    public void ExitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // stops Play Mode in editor
#else
        Application.Quit(); // quits in a built game
#endif
    }
}
