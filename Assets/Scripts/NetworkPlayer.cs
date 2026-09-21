using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class NetworkPlayer : NetworkBehaviour
{
    private const float MoveSpeed = 4f;
    private const float CatJumpSpeed = 7.5f;
    private const float DogJumpSpeed = 5.5f;
    private const float CatColliderSize = 0.7f;
    private const float DogColliderSize = 1f;
    private const int MaxQueuedMoveCommands = 30;

    private readonly NetworkVariable<Vector3> networkPosition = new(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> mechanismActivated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> gateOpen = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> animalRescued = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> levelCompleted = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> catEndActivated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> dogEndActivated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Rigidbody2D body;
    private float pendingMoveDirection;
    private bool isGrounded;
    private readonly Queue<PendingMove> pendingMoves = new();
    private TextMesh mechanismText;
    private TextMesh animalText;

    public override void OnNetworkSpawn()
    {
        body = gameObject.AddComponent<Rigidbody2D>();
        BoxCollider2D collider = gameObject.AddComponent<BoxCollider2D>();
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.simulated = IsServer;

        bool isCat = OwnerClientId == NetworkManager.ServerClientId;
        collider.size = Vector2.one * (isCat ? CatColliderSize : DogColliderSize);
        CreatePlaceholder(isCat);

        if (IsServer)
        {
            networkPosition.Value = transform.position;
            body.position = networkPosition.Value;
        }

        transform.position = networkPosition.Value;
        networkPosition.OnValueChanged += HandlePositionChanged;
        mechanismActivated.OnValueChanged += HandleMechanismChanged;

        if (isCat)
        {
            gateOpen.OnValueChanged += HandleGateStateChanged;
            animalRescued.OnValueChanged += HandleAnimalRescuedChanged;
            levelCompleted.OnValueChanged += HandleLevelCompletedChanged;
            catEndActivated.OnValueChanged += HandleCatEndChanged;
            dogEndActivated.OnValueChanged += HandleDogEndChanged;
            UpdateGateVisual(gateOpen.Value);
            UpdateEndVisuals();
            if (levelCompleted.Value)
            {
                NetworkBootstrap.MarkLevelCompleted();
            }
        }

        UpdateMechanismText(mechanismActivated.Value);
    }

    public override void OnNetworkDespawn()
    {
        networkPosition.OnValueChanged -= HandlePositionChanged;
        mechanismActivated.OnValueChanged -= HandleMechanismChanged;

        if (OwnerClientId == NetworkManager.ServerClientId)
        {
            gateOpen.OnValueChanged -= HandleGateStateChanged;
            animalRescued.OnValueChanged -= HandleAnimalRescuedChanged;
            levelCompleted.OnValueChanged -= HandleLevelCompletedChanged;
            catEndActivated.OnValueChanged -= HandleCatEndChanged;
            dogEndActivated.OnValueChanged -= HandleDogEndChanged;
        }
    }

    private void Update()
    {
        if (NetworkBootstrap.IsMatchPaused || NetworkBootstrap.IsLevelCompleted)
        {
            return;
        }

        if (!IsOwner || Keyboard.current == null)
        {
            return;
        }

        float direction = ReadHorizontalInput();
        if (!Mathf.Approximately(direction, 0f))
        {
            QueueMove(direction);
        }

        SendReadyMoves();

        if (ReadJumpInput())
        {
            SubmitJumpRpc();
        }

        if (ReadMechanismInput())
        {
            ActivateMechanismRpc();
        }

        if (ReadCatEndInput())
        {
            RequestCatEndRpc();
        }

        if (!Mathf.Approximately(direction, 0f) && OwnerClientId != NetworkManager.ServerClientId)
        {
            RequestPushRpc(direction);
        }

        if (OwnerClientId == NetworkManager.ServerClientId)
        {
            UpdateLeverPrompt();
        }

        if (ReadDogEndInput())
        {
            RequestDogEndRpc();
        }
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void SubmitMoveRpc(float direction)
    {
        pendingMoveDirection = Mathf.Clamp(direction, -1f, 1f);
    }

    [Rpc(SendTo.Server)]
    private void SubmitJumpRpc()
    {
        if (!isGrounded)
        {
            return;
        }

        bool isCat = OwnerClientId == NetworkManager.ServerClientId;
        float jumpSpeed = isCat ? CatJumpSpeed : DogJumpSpeed;
        body.linearVelocity = new Vector2(body.linearVelocity.x, jumpSpeed);
        isGrounded = false;
    }

    [Rpc(SendTo.Server)]
    private void ActivateMechanismRpc()
    {
        if (OwnerClientId != NetworkManager.ServerClientId || mechanismActivated.Value)
        {
            return;
        }

        SceneLever lever = FindFirstObjectByType<SceneLever>();
        if (lever == null || !lever.IsPlayerInRange(networkPosition.Value))
        {
            return;
        }

        mechanismActivated.Value = true;
        gateOpen.Value = true;
        UpdateGateVisual(true);
        lever.SetActivated(true);
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void RequestPushRpc(float direction)
    {
        if (OwnerClientId == NetworkManager.ServerClientId)
        {
            return;
        }

        PushableBox box = FindFirstObjectByType<PushableBox>();
        box?.RequestPush(networkPosition.Value, direction);
    }

    [Rpc(SendTo.Server)]
    private void RequestCatEndRpc()
    {
        NetworkPlayer catPlayer = FindCatPlayer();
        if (catPlayer == null || catPlayer.OwnerClientId != NetworkManager.ServerClientId || !catPlayer.gateOpen.Value)
        {
            return;
        }

        SceneEndPoint endPoint = FindEndPoint("endCat");
        if (endPoint == null || !endPoint.IsInRange(networkPosition.Value))
        {
            return;
        }

        catPlayer.catEndActivated.Value = true;
        catPlayer.TryCompleteRescue();
    }

    [Rpc(SendTo.Server)]
    private void RequestDogEndRpc()
    {
        NetworkPlayer dogPlayer = this;
        if (dogPlayer.OwnerClientId == NetworkManager.ServerClientId)
        {
            return;
        }

        SceneEndPoint endPoint = FindEndPoint("endDog");
        if (endPoint == null || !endPoint.IsInRange(networkPosition.Value))
        {
            return;
        }

        NetworkPlayer catPlayer = FindCatPlayer();
        if (catPlayer == null || !catPlayer.gateOpen.Value)
        {
            return;
        }

        catPlayer.dogEndActivated.Value = true;
        catPlayer.TryCompleteRescue();
    }

    private float ReadHorizontalInput()
    {
        bool isCat = OwnerClientId == NetworkManager.ServerClientId;
        bool moveLeft = isCat ? Keyboard.current.aKey.isPressed : Keyboard.current.leftArrowKey.isPressed;
        bool moveRight = isCat ? Keyboard.current.dKey.isPressed : Keyboard.current.rightArrowKey.isPressed;

        return (moveRight ? 1f : 0f) - (moveLeft ? 1f : 0f);
    }

    private void QueueMove(float direction)
    {
        if (Random.value < NetworkBootstrap.SimulatedMoveLossProbability)
        {
            NetworkBootstrap.RecordMoveDropped();
            return;
        }

        if (pendingMoves.Count >= MaxQueuedMoveCommands)
        {
            pendingMoves.Dequeue();
            NetworkBootstrap.RecordStaleMoveDropped();
        }

        float jitter = Random.Range(
            -NetworkBootstrap.SimulatedMoveJitterSeconds,
            NetworkBootstrap.SimulatedMoveJitterSeconds);
        float delay = Mathf.Max(0f, NetworkBootstrap.SimulatedMoveDelaySeconds + jitter);
        float sendTime = Time.unscaledTime + delay;
        pendingMoves.Enqueue(new PendingMove(direction, sendTime));
        NetworkBootstrap.RecordMoveQueued();
    }

    private void SendReadyMoves()
    {
        while (pendingMoves.Count > 0 && pendingMoves.Peek().SendTime <= Time.unscaledTime)
        {
            SubmitMoveRpc(pendingMoves.Dequeue().Direction);
            NetworkBootstrap.RecordMoveSent();
        }
    }

    private bool ReadJumpInput()
    {
        bool isCat = OwnerClientId == NetworkManager.ServerClientId;
        return isCat ? Keyboard.current.wKey.wasPressedThisFrame : Keyboard.current.upArrowKey.wasPressedThisFrame;
    }

    private bool ReadMechanismInput()
    {
        return OwnerClientId == NetworkManager.ServerClientId && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private bool ReadCatEndInput()
    {
        return OwnerClientId == NetworkManager.ServerClientId && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private bool ReadDogEndInput()
    {
        return OwnerClientId != NetworkManager.ServerClientId && Keyboard.current.rightShiftKey.wasPressedThisFrame;
    }

    private void FixedUpdate()
    {
        if (!IsServer || NetworkBootstrap.IsMatchPaused || NetworkBootstrap.IsLevelCompleted)
        {
            return;
        }

        body.linearVelocity = new Vector2(pendingMoveDirection * MoveSpeed, body.linearVelocity.y);
        pendingMoveDirection = 0f;
        networkPosition.Value = body.position;
    }

    private void HandlePositionChanged(Vector3 previousPosition, Vector3 currentPosition)
    {
        if (!IsServer)
        {
            transform.position = currentPosition;
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y > 0.5f)
            {
                isGrounded = true;
                return;
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        isGrounded = false;
    }

    private void HandleMechanismChanged(bool previousValue, bool currentValue)
    {
        UpdateMechanismText(currentValue);
    }

    private void HandleGateStateChanged(bool previousValue, bool currentValue)
    {
        UpdateGateVisual(currentValue);
        SceneLever lever = FindFirstObjectByType<SceneLever>();
        lever?.SetActivated(currentValue);
    }

    private void HandleAnimalRescuedChanged(bool previousValue, bool currentValue)
    {
        UpdateAnimalText(currentValue, levelCompleted.Value);
    }

    private void HandleLevelCompletedChanged(bool previousValue, bool currentValue)
    {
        UpdateAnimalText(animalRescued.Value, currentValue);
        if (currentValue)
        {
            NetworkBootstrap.MarkLevelCompleted();
        }
    }

    private void HandleCatEndChanged(bool previousValue, bool currentValue)
    {
        UpdateEndVisuals();
    }

    private void HandleDogEndChanged(bool previousValue, bool currentValue)
    {
        UpdateEndVisuals();
    }

    private void TryCompleteRescue()
    {
        if (!catEndActivated.Value || !dogEndActivated.Value || animalRescued.Value)
        {
            return;
        }

        animalRescued.Value = true;
        levelCompleted.Value = true;
        SceneRescue rescue = FindFirstObjectByType<SceneRescue>();
        rescue?.SetRescued(true);
    }

    private void UpdateEndVisuals()
    {
        FindEndPoint("endCat")?.SetActivated(catEndActivated.Value);
        FindEndPoint("endDog")?.SetActivated(dogEndActivated.Value);
        FindFirstObjectByType<SceneRescue>()?.SetRescued(animalRescued.Value);
    }

    private static SceneEndPoint FindEndPoint(string objectName)
    {
        foreach (SceneEndPoint endPoint in FindObjectsByType<SceneEndPoint>(FindObjectsSortMode.None))
        {
            if (string.Equals(endPoint.name, objectName, System.StringComparison.OrdinalIgnoreCase))
            {
                return endPoint;
            }
        }

        return null;
    }

    private static NetworkPlayer FindCatPlayer()
    {
        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (NetworkPlayer player in players)
        {
            if (player.OwnerClientId == NetworkManager.ServerClientId)
            {
                return player;
            }
        }

        return null;
    }

    private void CreatePlaceholder(bool isCat)
    {
        SpriteRenderer visual = transform.Find("Visual")?.GetComponent<SpriteRenderer>();
        if (visual != null)
        {
            visual.color = isCat ? Color.black : new Color(1f, 0.45f, 0.05f);
            visual.sortingOrder = 1;
            visual.transform.localScale *= isCat ? 0.75f : 1.15f;
        }

        GameObject mechanism = new GameObject("Mechanism Status");
        mechanism.transform.SetParent(transform, false);
        mechanism.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        mechanismText = mechanism.AddComponent<TextMesh>();
        mechanismText.anchor = TextAnchor.MiddleCenter;
        mechanismText.alignment = TextAlignment.Center;
        mechanismText.characterSize = 0.08f;
        mechanismText.fontSize = 36;
        mechanismText.color = Color.white;
        mechanismText.text = isCat ? "E: mecanismo | Q: resgatar" : "Enter: mecanismo | Shift direito: resgatar";
    }

    private void UpdateMechanismText(bool isActivated)
    {
        if (mechanismText != null)
        {
            mechanismText.text = isActivated ? "MECANISMO ATIVO" : "Mecanismo pendente";
            mechanismText.color = isActivated ? Color.green : Color.white;
        }
    }

    private void UpdateGateVisual(bool isOpen)
    {
        SceneGate gate = FindFirstObjectByType<SceneGate>();
        gate?.SetOpen(isOpen);
    }

    private void UpdateLeverPrompt()
    {
        SceneLever lever = FindFirstObjectByType<SceneLever>();
        if (lever == null)
        {
            return;
        }

        bool canInteract = IsOwner
            && OwnerClientId == NetworkManager.ServerClientId
            && !mechanismActivated.Value
            && lever.IsPlayerInRange(networkPosition.Value);
        lever.SetPromptVisible(canInteract);
    }

    private void UpdateAnimalText(bool isRescued, bool isLevelCompleted)
    {
        if (animalText == null)
        {
            return;
        }

        if (isLevelCompleted)
        {
            animalText.text = "[ ANIMAL RESGATADO ]\n[ FASE CONCLUIDA ]";
            animalText.color = Color.green;
            return;
        }

        animalText.text = isRescued ? "[ ANIMAL RESGATADO ]" : "[ ANIMAL PRESO ]\nAproxime-se e resgate";
        animalText.color = isRescued ? Color.green : Color.yellow;
    }

    private readonly struct PendingMove
    {
        public PendingMove(float direction, float sendTime)
        {
            Direction = direction;
            SendTime = sendTime;
        }

        public float Direction { get; }

        public float SendTime { get; }
    }
}