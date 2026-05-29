using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Loads a ConceptConfig JSON file from StreamingAssets and fires
/// ConceptVizEvents.OnConfigLoaded once parsed.
///
/// Why UnityWebRequest and not File.ReadAllText?
///   On Android (Quest), StreamingAssets files live inside the APK jar archive.
///   File.ReadAllText cannot read jar:// paths. UnityWebRequest handles the
///   platform-specific URI scheme on every platform automatically.
///
/// File location: StreamingAssets/ConceptViz/fileName
/// Usage: assign fileName in Inspector. This component calls LoadConfig at Start.
/// </summary>
public class ConceptConfigLoader : MonoBehaviour
{
    [Header("Config File")]
    [Tooltip("File name inside StreamingAssets/ConceptViz/ (include .json extension).")]
    [SerializeField] private string fileName = "default.json";

    [Tooltip("Sub-folder inside StreamingAssets.")]
    [SerializeField] private string subFolder = "ConceptViz";

    [Header("Fallback")]
    [Tooltip("If true, generate a built-in demo config when the file is missing.")]
    [SerializeField] private bool useFallbackIfMissing = true;

    private bool _loaded;

    public bool IsLoaded => _loaded;

    public void LoadConfig() => LoadConfig(fileName);

    public void LoadConfig(string file)
    {
        if (_loaded) return;
        StartCoroutine(LoadCoroutine(file));
    }

    private void Start() => LoadConfig();

    private IEnumerator LoadCoroutine(string file)
    {
        string path = System.IO.Path.Combine(
            Application.streamingAssetsPath, subFolder, file);

        UnityWebRequest request = UnityWebRequest.Get(path);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(
                "[ConceptConfigLoader] Failed to load '" + path + "': " + request.error);
            request.Dispose();

            if (useFallbackIfMissing)
            {
                Debug.Log("[ConceptConfigLoader] Using built-in demo config.");
                ConceptVizEvents.RaiseConfigLoaded(BuildDemoConfig());
                _loaded = true;
            }
            yield break;
        }

        string json = request.downloadHandler.text;
        request.Dispose();

        ConceptConfig config;
        try
        {
            config = JsonUtility.FromJson<ConceptConfig>(json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[ConceptConfigLoader] JSON parse error: " + ex.Message);
            if (useFallbackIfMissing)
            {
                ConceptVizEvents.RaiseConfigLoaded(BuildDemoConfig());
                _loaded = true;
            }
            yield break;
        }

        if (config == null)
        {
            Debug.LogError("[ConceptConfigLoader] Parsed config is null.");
            yield break;
        }

        _loaded = true;
        Debug.Log("[ConceptConfigLoader] Loaded concept: '" + config.conceptTitle + "'");
        ConceptVizEvents.RaiseConfigLoaded(config);
    }

    private static ConceptConfig BuildDemoConfig()
    {
        var zero = new Vector3Config { x = 0f, y = 0f, z = 0f };

        return new ConceptConfig
        {
            conceptTitle = "Electric Field (Demo)",
            description  = "Two charges with a field line",
            layers = new LayerConfig[]
            {
                new LayerConfig { id = "objects", displayName = "Objects",     defaultVisible = true },
                new LayerConfig { id = "field",   displayName = "Field Lines", defaultVisible = true },
                new LayerConfig { id = "labels",  displayName = "Labels",      defaultVisible = true },
            },
            objects = new SpawnedObjectConfig[]
            {
                new SpawnedObjectConfig
                {
                    id        = "chargePos",
                    modelPath = "ConceptViz/Sphere",
                    position  = new Vector3Config { x = -0.15f, y = 0f, z = 0f },
                    rotation  = zero,
                    scale     = new Vector3Config { x = 0.06f,  y = 0.06f, z = 0.06f },
                    layerIds  = new string[] { "objects" }
                },
                new SpawnedObjectConfig
                {
                    id        = "chargeNeg",
                    modelPath = "ConceptViz/Sphere",
                    position  = new Vector3Config { x = 0.15f, y = 0f, z = 0f },
                    rotation  = zero,
                    scale     = new Vector3Config { x = 0.06f, y = 0.06f, z = 0.06f },
                    layerIds  = new string[] { "objects" }
                }
            },
            fieldLines = new FieldLineConfig[]
            {
                new FieldLineConfig
                {
                    id           = "fl_0",
                    fromObjectId = "chargePos",
                    toObjectId   = "chargeNeg",
                    color        = new ColorConfig { r = 1f, g = 0.5f, b = 0.1f, a = 1f },
                    width        = 0.005f,
                    layerId      = "field"
                }
            },
            annotations = new AnnotationConfig[]
            {
                new AnnotationConfig
                {
                    id             = "ann_pos",
                    targetObjectId = "chargePos",
                    text           = "+Q",
                    offset         = new Vector3Config { x = 0f, y = 0.10f, z = 0f },
                    layerId        = "labels"
                },
                new AnnotationConfig
                {
                    id             = "ann_neg",
                    targetObjectId = "chargeNeg",
                    text           = "-Q",
                    offset         = new Vector3Config { x = 0f, y = 0.10f, z = 0f },
                    layerId        = "labels"
                }
            }
        };
    }
}
