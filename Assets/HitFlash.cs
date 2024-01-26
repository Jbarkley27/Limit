using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HitFlash : MonoBehaviour
{
    // Duration of the flash effect in seconds
    public float flashDuration = 0.5f;

    public Material whiteMat;

    // Original colors of materials
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

    Coroutine flashCo;

    // Function to start the flash effect
    public void Flash()
    {

        if (flashCo != null)
        {
            StopCoroutine(flashCo);
            ResetColors();
        }

        // Save original materials and start the flash effect
        SaveOriginalMaterials();
        flashCo = StartCoroutine(StartFlashEffect());
    }

    // Save the original materials of the GameObject and its children
    private void SaveOriginalMaterials()
    {
        originalMaterials.Clear();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            originalMaterials.Add(renderer, renderer.materials);
        }
    }

    // Start the flash effect by changing colors to white
    public IEnumerator StartFlashEffect()
    {
        foreach (var entry in originalMaterials)
        {
            Renderer renderer = entry.Key;
            Material[] materials = entry.Value;

            Material[] whiteMaterials = new Material[materials.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                whiteMaterials[i] = whiteMat;
            }

            renderer.materials = whiteMaterials;
        }

        yield return new WaitForSeconds(flashDuration);

        ResetColors();
    }

    // Reset colors back to their original values
    public void ResetColors()
    {
        foreach (var entry in originalMaterials)
        {
            Renderer renderer = entry.Key;
            Material[] originalMaterials = entry.Value;

            renderer.materials = originalMaterials;
        }
    }
}

