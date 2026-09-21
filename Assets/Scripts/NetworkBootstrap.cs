using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NetworkBootstrap : MonoBehaviour
{
    private const ushort Port = 7777;

    private static NetworkBootstrap instance;

    public static bool IsMatchPaused { get; private set; }
    public static bool IsLevelCompleted { get; private set; }
    public static float SimulatedMoveDelaySeconds { get; private set; }
    public static float SimulatedMoveJitterSeconds { get; private set; }
    public static float SimulatedMoveLossProbability { get; private set; }
    public static int QueuedMoveCommands { get; private set; }
    public static int SentMoveCommands { get; private set; }
    public static int DroppedMoveCommands { get; private set; }
    public static int StaleMoveCommands { get; private set; }

    private NetworkManager networkManager;
    private GameObject playerPrefab;
    private PushableBox spawnedBox;
    private string status = "Aguardando inicio da rede.";
    private bool isShuttingDown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Create();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        Create();
    }

    private static void Create()
    {
        if (instance != null)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject("Network Bootstrap");
        DontDestroyOnLoad(bootstrapObject);
        bootstrapObject.AddComponent<NetworkBootstrap>();
    }

    private void Awake()
    {
        instance = this;
        IsLevelCompleted = false;
        CreateTestArea();
        EnsureSceneControllers();
        networkManager = gameObject.AddComponent<NetworkManager>();
        UnityTransport transport = gameObject.AddComponent<UnityTransport>();

        transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
        networkManager.NetworkConfig = new NetworkConfig();
        networkManager.NetworkConfig.NetworkTransport = transport;
        playerPrefab = Resources.Load<GameObject>("NetworkPlayer");
        if (playerPrefab == null)
        {
            Debug.LogError("O prefab Resources/NetworkPlayer nao foi encontrado.");
            return;
        }

        networkManager.AddNetworkPrefab(playerPrefab);
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, 20, 280, 400), GUI.skin.box);
        GUILayout.Label("CatDog Rescue - Teste de Rede");
        GUILayout.Space(8);

        if (!networkManager.IsListening)
        {
            if (GUILayout.Button("Iniciar Host"))
            {
                StartHost();
            }

            if (GUILayout.Button("Conectar como Client"))
            {
                StartClient();
            }
        }
        else
        {
            GUILayout.Label($"Conectados: {networkManager.ConnectedClientsIds.Count}");
            if (GUILayout.Button("Encerrar rede"))
            {
                ReturnToStartScreen();
            }
        }

        GUILayout.Space(8);
        GUILayout.Label(status);
        GUILayout.Space(8);
        GUILayout.Label($"Atraso simulado no movimento: {SimulatedMoveDelaySeconds * 1000f:0} ms");
        SimulatedMoveDelaySeconds = GUILayout.HorizontalSlider(SimulatedMoveDelaySeconds, 0f, 0.5f);
        GUILayout.Label($"Jitter simulado: {SimulatedMoveJitterSeconds * 1000f:0} ms");
        SimulatedMoveJitterSeconds = GUILayout.HorizontalSlider(SimulatedMoveJitterSeconds, 0f, 0.25f);
        GUILayout.Label($"Perda simulada no movimento: {SimulatedMoveLossProbability * 100f:0}%");
        SimulatedMoveLossProbability = GUILayout.HorizontalSlider(SimulatedMoveLossProbability, 0f, 0.3f);
        GUILayout.Space(8);
        GUILayout.Label("Comandos de movimento desta instancia");
        GUILayout.Label($"Pendentes: {QueuedMoveCommands}");
        GUILayout.Label($"Enviados: {SentMoveCommands}");
        GUILayout.Label($"Descartados: {DroppedMoveCommands}");
        GUILayout.Label($"Antigos descartados: {StaleMoveCommands}");
        GUILayout.EndArea();

        if (IsLevelCompleted)
        {
            DrawCompletionOverlay();
        }
        else if (IsMatchPaused)
        {
            DrawDisconnectOverlay();
        }
    }

    private void StartHost()
    {
        if (playerPrefab == null)
        {
            status = "Prefab de jogador ausente. Verifique o Console.";
            return;
        }

        ResetSessionState();
        if (networkManager.StartHost())
        {
            status = "Host iniciado. Aguardando Client na porta 7777.";
            return;
        }

        status = "Nao foi possivel iniciar o Host.";
    }

    private void StartClient()
    {
        if (playerPrefab == null)
        {
            status = "Prefab de jogador ausente. Verifique o Console.";
            return;
        }

        ResetSessionState();
        if (networkManager.StartClient())
        {
            status = "Tentando conectar ao Host local.";
            return;
        }

        status = "Nao foi possivel iniciar o Client.";
    }

    private void HandleClientConnected(ulong clientId)
    {
        status = $"Client {clientId} conectado.";
        Debug.Log($"Client {clientId} conectado ao Host.");

        if (networkManager.IsServer)
        {
            SpawnPlayer(clientId);
            RegisterSceneBox();
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        status = $"Client {clientId} desconectado.";
        Debug.Log($"Client {clientId} desconectado.");

        if (!isShuttingDown)
        {
            IsMatchPaused = true;
            status = "O outro jogador desconectou. Partida pausada.";
        }
    }

    private void SpawnPlayer(ulong clientId)
    {
        GameObject start = GameObject.Find("Start");
        Vector3 startPosition = start != null ? start.transform.position : new Vector3(-2f, 0f, 0f);
        float horizontalOffset = clientId == NetworkManager.ServerClientId ? -0.5f : 0.5f;
        Vector3 spawnPosition = startPosition + Vector3.right * horizontalOffset;

        GameObject player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    private void RegisterSceneBox()
    {
        if (spawnedBox != null)
        {
            return;
        }

        GameObject box = GameObject.Find("PushableBox");
        if (box == null)
        {
            Debug.LogError("Objeto PushableBox nao encontrado na cena. Renomeie sua caixa para PushableBox e adicione NetworkObject.");
            return;
        }

        spawnedBox = box.GetComponent<PushableBox>();
        if (spawnedBox == null)
        {
            spawnedBox = box.AddComponent<PushableBox>();
        }

        NetworkObject networkObject = box.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("PushableBox precisa do componente NetworkObject para ser sincronizada.");
        }
    }

    private void DrawDisconnectOverlay()
    {
        Rect area = new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 70f, 360f, 140f);
        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label("PARTIDA PAUSADA");
        GUILayout.Space(10);
        GUILayout.Label("O outro jogador desconectou.");
        GUILayout.Space(12);
        if (GUILayout.Button("Retornar a tela inicial"))
        {
            ReturnToStartScreen();
        }

        GUILayout.EndArea();
    }

    private void DrawCompletionOverlay()
    {
        Rect area = new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 70f, 360f, 140f);
        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label("FASE CONCLUIDA");
        GUILayout.Space(10);
        GUILayout.Label("O animal foi resgatado pelos dois jogadores.");
        GUILayout.Space(12);
        if (GUILayout.Button("Reiniciar fase"))
        {
            ReturnToStartScreen();
        }

        GUILayout.EndArea();
    }

    public static void MarkLevelCompleted()
    {
        IsLevelCompleted = true;
    }

    public static void RecordMoveQueued()
    {
        QueuedMoveCommands++;
    }

    public static void RecordMoveSent()
    {
        QueuedMoveCommands--;
        SentMoveCommands++;
    }

    public static void RecordMoveDropped()
    {
        DroppedMoveCommands++;
    }

    public static void RecordStaleMoveDropped()
    {
        QueuedMoveCommands--;
        StaleMoveCommands++;
    }

    private void ReturnToStartScreen()
    {
        isShuttingDown = true;
        networkManager.Shutdown();
        IsMatchPaused = false;
        IsLevelCompleted = false;
        SimulatedMoveDelaySeconds = 0f;
        SimulatedMoveJitterSeconds = 0f;
        SimulatedMoveLossProbability = 0f;
        ResetMoveStatistics();
        instance = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ResetSessionState()
    {
        isShuttingDown = false;
        IsMatchPaused = false;
        IsLevelCompleted = false;
        ResetMoveStatistics();
    }

    private static void ResetMoveStatistics()
    {
        QueuedMoveCommands = 0;
        SentMoveCommands = 0;
        DroppedMoveCommands = 0;
        StaleMoveCommands = 0;
    }

    private static void CreateTestArea()
    {
        GameObject floor = new GameObject("Test Floor");
        TextMesh floorText = floor.AddComponent<TextMesh>();
        floorText.text = "________________________________________________";
        floorText.anchor = TextAnchor.MiddleCenter;
        floorText.alignment = TextAlignment.Center;
        floorText.characterSize = 0.18f;
        floorText.fontSize = 48;
        floorText.color = new Color(0.2f, 0.7f, 0.35f);
        floor.transform.position = new Vector3(0f, -1f, 0f);
    }

    private static void EnsureSceneControllers()
    {
        GameObject lever = GameObject.Find("lever");
        if (lever != null && lever.GetComponent<SceneLever>() == null)
        {
            lever.AddComponent<SceneLever>();
        }

        GameObject gate = GameObject.Find("Gate");
        if (gate != null && gate.GetComponent<SceneGate>() == null)
        {
            gate.AddComponent<SceneGate>();
        }

        GameObject box = GameObject.Find("PushableBox");
        if (box != null && box.GetComponent<PushableBox>() == null)
        {
            box.AddComponent<PushableBox>();
        }

        AddSceneEndpoint("endCat", true);
        AddSceneEndpoint("endDog", false);
        AddSceneComponent<SceneRescue>("Rescue");
    }

    private static void AddSceneComponent<T>(string objectName) where T : Component
    {
        GameObject sceneObject = FindSceneObject(objectName);
        if (sceneObject != null && sceneObject.GetComponent<T>() == null)
        {
            sceneObject.AddComponent<T>();
        }
    }

    private static void AddSceneEndpoint(string objectName, bool catEndpoint)
    {
        GameObject sceneObject = FindSceneObject(objectName);
        if (sceneObject == null)
        {
            return;
        }

        SceneEndPoint endpoint = sceneObject.GetComponent<SceneEndPoint>();
        if (endpoint == null)
        {
            endpoint = sceneObject.AddComponent<SceneEndPoint>();
        }

        endpoint.SetEndpointRole(catEndpoint);
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject sceneObject in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (string.Equals(sceneObject.name, objectName, System.StringComparison.OrdinalIgnoreCase))
            {
                return sceneObject;
            }
        }

        return null;
    }
}