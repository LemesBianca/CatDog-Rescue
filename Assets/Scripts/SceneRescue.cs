using UnityEngine;

public sealed class SceneRescue : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color inactiveColor;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        inactiveColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
    }

    public void SetRescued(bool rescued)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = rescued ? Color.green : inactiveColor;
        }
    }
}
