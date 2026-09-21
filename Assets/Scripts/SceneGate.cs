using UnityEngine;

public sealed class SceneGate : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Collider2D gateCollider;
    private Color closedColor;
    private Color openColor = new Color(0.2f, 0.85f, 0.25f);

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        gateCollider = GetComponent<Collider2D>();
        if (spriteRenderer != null)
        {
            closedColor = spriteRenderer.color;
        }

        SetOpen(false);
    }

    public void SetOpen(bool isOpen)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = isOpen ? openColor : closedColor;
        }

        if (gateCollider != null)
        {
            gateCollider.enabled = !isOpen;
        }
    }
}
