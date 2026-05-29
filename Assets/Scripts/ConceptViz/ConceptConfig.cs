using System;
using UnityEngine;

/// <summary>
/// Serializable data model for a Mixed Reality concept visualization.
///
/// Loaded from a JSON file in StreamingAssets via JsonConfigLoader. Every field
/// uses primitive types so JsonUtility can deserialize without reflection issues
/// on Quest (IL2CPP build). Vector3 and Color are represented as flat structs.
///
/// JSON example (StreamingAssets/ConceptViz/default.json):
/// {
///   "conceptTitle": "Electric Field",
///   "description": "Visualise field lines around a positive charge.",
///   "layers": [
///     { "id": "field",  "displayName": "Field Lines", "defaultVisible": true },
///     { "id": "labels", "displayName": "Labels",      "defaultVisible": true }
///   ],
///   "objects": [
///     { "id": "chargeA", "modelPath": "ConceptViz/Sphere",
///       "position": {"x":0,"y":0.5,"z":0},
///       "rotation": {"x":0,"y":0,"z":0},
///       "scale":    {"x":0.1,"y":0.1,"z":0.1},
///       "layerIds": ["field"] }
///   ],
///   "fieldLines": [
///     { "id": "fl_0", "fromObjectId": "chargeA", "toObjectId": "chargeB",
///       "color": {"r":1,"g":0.5,"b":0.1,"a":1}, "width": 0.004, "layerId": "field" }
///   ],
///   "annotations": [
///     { "id": "ann_0", "targetObjectId": "chargeA", "text": "+Q",
///       "offset": {"x":0,"y":0.15,"z":0}, "layerId": "labels" }
///   ]
/// }
/// </summary>

// ── Primitive helpers ─────────────────────────────────────────────────────────

[Serializable]
public class Vector3Config
{
    public float x, y, z;

    public Vector3 ToVector3() => new Vector3(x, y, z);

    public static Vector3Config FromVector3(Vector3 v) =>
        new Vector3Config { x = v.x, y = v.y, z = v.z };

    public static readonly Vector3Config Zero  = new Vector3Config { x = 0, y = 0, z = 0 };
    public static readonly Vector3Config One   = new Vector3Config { x = 1, y = 1, z = 1 };
}

[Serializable]
public class ColorConfig
{
    public float r = 1f, g = 1f, b = 1f, a = 1f;

    public Color ToColor() => new Color(r, g, b, a);

    public static ColorConfig FromColor(Color c) =>
        new ColorConfig { r = c.r, g = c.g, b = c.b, a = c.a };

    public static readonly ColorConfig White  = new ColorConfig { r = 1, g = 1, b = 1, a = 1 };
    public static readonly ColorConfig Cyan   = new ColorConfig { r = 0, g = 1, b = 1, a = 1 };
}

// ── Config data classes ───────────────────────────────────────────────────────

/// <summary>A single conceptual layer that groups objects and can be toggled.</summary>
[Serializable]
public class LayerConfig
{
    /// <summary>Unique identifier referenced by SpawnedObjectConfig, FieldLineConfig, AnnotationConfig.</summary>
    public string id;
    /// <summary>Human-readable name shown in the layer toggle UI.</summary>
    public string displayName;
    /// <summary>Whether the layer is visible at startup.</summary>
    public bool   defaultVisible = true;
}

/// <summary>A 3D model to instantiate as part of the concept.</summary>
[Serializable]
public class SpawnedObjectConfig
{
    /// <summary>Unique identifier for cross-referencing in field lines and annotations.</summary>
    public string         id;
    /// <summary>Resources path (no extension) used to load the model prefab.</summary>
    public string         modelPath;
    public Vector3Config  position;
    public Vector3Config  rotation;
    public Vector3Config  scale;
    /// <summary>Layer IDs this object belongs to. Visibility follows any matching layer.</summary>
    public string[]       layerIds;
}

/// <summary>A LineRenderer-based connection between two spawned objects.</summary>
[Serializable]
public class FieldLineConfig
{
    public string      id;
    public string      fromObjectId;
    public string      toObjectId;
    public ColorConfig color;
    [Tooltip("Line width in metres.")]
    public float       width   = 0.005f;
    public string      layerId;
}

/// <summary>A world-space text label anchored to a spawned object.</summary>
[Serializable]
public class AnnotationConfig
{
    public string       id;
    public string       targetObjectId;
    public string       text;
    /// <summary>Offset from the target object's position in local space.</summary>
    public Vector3Config offset;
    public string        layerId;
}

/// <summary>Top-level concept configuration — root of the JSON document.</summary>
[Serializable]
public class ConceptConfig
{
    public string             conceptTitle;
    public string             description;
    public LayerConfig[]      layers;
    public SpawnedObjectConfig[] objects;
    public FieldLineConfig[]  fieldLines;
    public AnnotationConfig[] annotations;
}
