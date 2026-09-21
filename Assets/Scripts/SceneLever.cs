using UnityEngine;

public sealed class SceneLever : MonoBehaviour
{
    private const float InteractionRange = 1.25f;

    private SpriteRenderer spriteRenderer;
    private TextMesh promptText;
    private Color offColor;
    private Color onColor = new Color(0.2f, 0.85f, 0.25f);

    public bool IsPlayerInRange(Vector3 playerPosition)
    {
        return Vector2.Distance(transform.position, playerPosition) <= InteractionRange;
    }

    public void SetActivated(bool activated)
    {
        EnsureVisuals();
        spriteRenderer.color = activated ? onColor : offColor;
        transform.localRotation = Quaternion.Euler(0f, 0f, activated ? -35f : 35f);
    }

    public void SetPromptVisible(bool visible)
    {
        EnsureVisuals();
        promptText.gameObject.SetActive(visible);
    }

    private void Awake()
    {
        EnsureVisuals();
        SetActivated(false);
        SetPromptVisible(false);
    }

    private void EnsureVisuals()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                offColor = spriteRenderer.color;
            }
        }

        if (promptText != null)
        {
            return;
        }

        GameObject prompt = new GameObject("Interaction Prompt");
        prompt.transform.SetParent(transform, false);
        prompt.transform.localPosition = new Vector3(0f, 1f, 0f);
        promptText = prompt.AddComponent<TextMesh>();
        promptText.text = "[ E ] Interagir";
        promptText.anchor = TextAnchor.MiddleCenter;
        promptText.alignment = TextAlignment.Center;
        promptText.characterSize = 0.1f;
        promptText.fontSize = 42;
        promptText.color = Color.white;
    }
}
