using Unity.Netcode;
using UnityEngine;

public sealed class PushableBox : NetworkBehaviour
{
    private const float PushRange = 1.2f;
    private const float PushSpeed = 2.5f;

    private readonly NetworkVariable<Vector3> networkPosition = new(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Rigidbody2D body;
    private SpriteRenderer spriteRenderer;
    private float pendingPushDirection;

    public override void OnNetworkSpawn()
    {
        body = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        body.simulated = IsServer;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (IsServer)
        {
            networkPosition.Value = transform.position;
            body.position = networkPosition.Value;
        }
        else
        {
            transform.position = networkPosition.Value;
        }

        networkPosition.OnValueChanged += HandlePositionChanged;
        UpdateVisual();
    }

    public override void OnNetworkDespawn()
    {
        networkPosition.OnValueChanged -= HandlePositionChanged;
    }

    public bool CanBePushedBy(Vector3 playerPosition)
    {
        if (!IsServer)
        {
            return false;
        }

        return Vector2.Distance(body.position, playerPosition) <= PushRange;
    }

    public void RequestPush(Vector3 playerPosition, float direction)
    {
        if (!CanBePushedBy(playerPosition))
        {
            return;
        }

        pendingPushDirection = Mathf.Clamp(direction, -1f, 1f);
    }

    private void FixedUpdate()
    {
        if (!IsServer || NetworkBootstrap.IsMatchPaused || NetworkBootstrap.IsLevelCompleted)
        {
            return;
        }

        if (Mathf.Approximately(pendingPushDirection, 0f))
        {
            return;
        }

        Vector2 nextPosition = body.position + Vector2.right * pendingPushDirection * PushSpeed * Time.fixedDeltaTime;
        body.MovePosition(nextPosition);
        pendingPushDirection = 0f;
        networkPosition.Value = nextPosition;
    }

    private void HandlePositionChanged(Vector3 previousPosition, Vector3 currentPosition)
    {
        if (!IsServer)
        {
            transform.position = currentPosition;
        }
    }

    private void UpdateVisual()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = new Color(0.65f, 0.35f, 0.08f);
        }
    }
}
