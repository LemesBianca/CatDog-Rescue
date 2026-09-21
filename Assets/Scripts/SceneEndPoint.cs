using UnityEngine;

public sealed class SceneEndPoint : MonoBehaviour
{
    [SerializeField] private bool catEndpoint;

    private SpriteRenderer spriteRenderer;
    private Color inactiveColor;

    public bool IsCatEndpoint => catEndpoint;

    public void SetEndpointRole(bool isCat)
    {
        catEndpoint = isCat;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        inactiveColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
    }

    public bool IsInRange(Vector3 playerPosition)
    {
        return Vector2.Distance(transform.position, playerPosition) <= 1.25f;
    }

    public void SetActivated(bool activated)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = activated ? Color.green : inactiveColor;
        }
    }
}
