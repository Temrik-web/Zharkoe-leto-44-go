using UnityEditor;
using UnityEngine;

// Add one inexpensive, stationary collision shape to every instance of this FBX.
public sealed class AnimalColliderImporter : AssetPostprocessor
{
    public override uint GetVersion() => 2;

    private void OnPostprocessModel(GameObject model)
    {
        if (assetPath != "Assets/Chickenn(GR).fbx" && assetPath != "Assets/Cow.fbx")
            return;

        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0)
            return;

        var bounds = new Bounds();
        bool initialized = false;
        foreach (var renderer in renderers)
        {
            var localBounds = renderer.localBounds;
            // Skinned renderer bounds are expressed in the root bone coordinate space.
            var boundsTransform = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
            var toRoot = model.transform.worldToLocalMatrix * boundsTransform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = Vector3.Scale(localBounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1,
                    (corner & 2) == 0 ? -1 : 1,
                    (corner & 4) == 0 ? -1 : 1));
                var point = toRoot.MultiplyPoint3x4(localBounds.center + offset);
                if (!initialized)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                    bounds.Encapsulate(point);
            }
        }

        var collider = model.GetComponent<BoxCollider>();
        if (collider == null)
            collider = model.AddComponent<BoxCollider>();
        collider.center = bounds.center;
        collider.size = bounds.size;
        collider.isTrigger = false;
    }
}
