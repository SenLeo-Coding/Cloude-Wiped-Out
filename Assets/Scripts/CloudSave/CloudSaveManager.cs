using System;
using System.Threading.Tasks;
using CloudGame.Auth;
using UnityEngine;

namespace CloudGame.Save
{
    /// <summary>
    /// Punto de entrada de Cloud Save para el juego. Descarga el documento al iniciar sesión,
    /// lo deja en memoria como <see cref="Data"/> y ofrece los métodos que usa el juego para
    /// registrar progreso y subirlo.
    ///
    /// La sincronización se basa en <see cref="AuthenticationManager.StateChanged"/>: cada
    /// cambio de jugador dispara una carga y un borrado de la caché, de modo que dos cuentas
    /// en el mismo dispositivo nunca comparten documento.
    /// </summary>
    public class CloudSaveManager : MonoBehaviour
    {
        public static CloudSaveManager Instance { get; private set; }

        [Header("Cloud Save")]
        [Tooltip("Descargar el guardado en cuanto UGS termine de iniciar sesión.")]
        [SerializeField] private bool autoLoadOnSignIn = true;

        /// <summary>Documento en memoria. Nunca es null una vez firmado el jugador.</summary>
        public event Action<PlayerSaveData> DataLoaded;
        public event Action<PlayerSaveData> DataSaved;
        public event Action DataCleared;
        public event Action<CloudSaveError> ErrorOccurred;

        public CloudSaveStore Store { get; } = new CloudSaveStore();

        public PlayerSaveData Data { get; private set; }

        /// <summary>Hay un documento listo para jugar, ya venga de la nube o sea nuevo.</summary>
        public bool IsReady => Data != null;

        /// <summary>Hay una operación de red en curso.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>
        /// El documento descargado ya existía. Es false para un jugador nuevo, cuyos datos
        /// todavía no se han subido nunca.
        /// </summary>
        public bool LoadedFromCloud { get; private set; }

        private bool authenticationBound;
        private int loadGeneration;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            TryBindAuthentication();
        }

        private void Update()
        {
            // AuthenticationManager se crea en otra escena o más tarde en el arranque, así que
            // se busca hasta encontrarlo en lugar de asumir el orden de Awake.
            if (!authenticationBound)
            {
                TryBindAuthentication();
            }
        }

        /// <summary>
        /// Descarga el documento del jugador firmado. Si no hay sesión activa, la caché se
        /// vacía y no se intenta ninguna llamada de red.
        /// </summary>
        public async Task<PlayerSaveData> LoadAsync()
        {
            if (IsBusy)
            {
                return Data;
            }

            AuthenticationManager auth = AuthenticationManager.Instance;
            if (auth == null || !auth.IsSignedIn)
            {
                ClearLocalData();
                return null;
            }

            // Si el jugador cambia de cuenta mientras se descarga, el resultado se descarta:
            // el número de generación ya no coincide.
            int generation = ++loadGeneration;
            string playerId = auth.PlayerId;
            IsBusy = true;

            try
            {
                var result = await Store.LoadAsync();
                if (generation != loadGeneration || !IsStillSamePlayer(playerId))
                {
                    Debug.Log("[CloudSaveManager] Descarga descartada: el jugador cambió a mitad de la petición.");
                    return Data;
                }

                if (!result.Success)
                {
                    // Un fallo de red no debe vaciar el progreso ya descargado.
                    if (Data == null)
                    {
                        Data = PlayerSaveData.CreateNew(auth.PlayerName);
                        LoadedFromCloud = false;
                    }

                    ReportError(result.Error);
                    return Data;
                }

                Data = result.Value ?? PlayerSaveData.CreateNew(auth.PlayerName);
                LoadedFromCloud = result.Value != null;
                DataLoaded?.Invoke(Data);
                return Data;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Sube el documento en memoria. Devuelve false si no hay datos, no hay sesión o el
        /// servicio responde con error.
        /// </summary>
        public async Task<bool> SaveAsync()
        {
            if (Data == null)
            {
                return false;
            }

            AuthenticationManager auth = AuthenticationManager.Instance;
            if (auth == null || !auth.IsSignedIn)
            {
                ReportError(CloudSaveError.ForNotSignedIn());
                return false;
            }

            if (IsBusy)
            {
                return false;
            }

            int generation = ++loadGeneration;
            string playerId = auth.PlayerId;
            IsBusy = true;

            try
            {
                var result = await Store.SaveAsync(Data);
                if (generation != loadGeneration || !IsStillSamePlayer(playerId))
                {
                    Debug.Log("[CloudSaveManager] Guardado descartado: el jugador cambió a mitad de la petición.");
                    return false;
                }

                if (!result.Success)
                {
                    ReportError(result.Error);
                    return false;
                }

                DataSaved?.Invoke(Data);
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Borra el progreso guardado en la nube y empieza un documento nuevo en memoria.
        /// </summary>
        public async Task<bool> ResetProgressAsync()
        {
            AuthenticationManager auth = AuthenticationManager.Instance;
            if (auth == null || !auth.IsSignedIn || IsBusy)
            {
                return false;
            }

            int generation = ++loadGeneration;
            string playerId = auth.PlayerId;
            IsBusy = true;

            try
            {
                var result = await Store.DeleteAsync();
                if (generation != loadGeneration || !IsStillSamePlayer(playerId))
                {
                    return false;
                }

                if (!result.Success)
                {
                    ReportError(result.Error);
                    return false;
                }

                Data = PlayerSaveData.CreateNew(auth.PlayerName);
                LoadedFromCloud = false;
                DataCleared?.Invoke();
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Registra que el jugador terminó un nivel. Actualiza la memoria; llama a
        /// <see cref="SaveAsync"/> cuando quieras subirlo.
        /// </summary>
        public void RecordLevelCompleted(int level)
        {
            if (Data == null)
            {
                return;
            }

            Data.stats.levelsCompleted++;
            if (level > Data.profile.highestLevelCompleted)
            {
                Data.profile.highestLevelCompleted = level;
            }
        }

        public void RecordMinesRevealed(int count = 1)
        {
            if (Data != null)
            {
                Data.stats.minesRevealed += Math.Max(0, count);
            }
        }

        /// <summary>Registra una partida perdida al quedarse sin vidas.</summary>
        public void RecordGameLost()
        {
            if (Data != null)
            {
                Data.stats.gamesLost++;
            }
        }

        /// <summary>
        /// Suma el resultado de una partida al modo indicado. Casual y Ranked llevan contadores
        /// y rating propios.
        /// </summary>
        public void RecordMatchResult(GameMode mode, MatchOutcome outcome, int ratingDelta = 0)
        {
            Data?.For(mode)?.Record(outcome, ratingDelta);
        }

        public void SetDisplayName(string displayName)
        {
            if (Data?.profile != null && !string.IsNullOrWhiteSpace(displayName))
            {
                Data.profile.displayName = displayName.Trim();
            }
        }

        private void TryBindAuthentication()
        {
            if (authenticationBound)
            {
                return;
            }

            AuthenticationManager auth = AuthenticationManager.Instance;
            if (auth == null)
            {
                return;
            }

            auth.StateChanged += OnAuthStateChanged;
            authenticationBound = true;

            // Si el jugador ya estaba dentro cuando se creó este objeto, la carga no llega
            // por evento: hay que pedirla explícitamente.
            if (auth.IsSignedIn && autoLoadOnSignIn)
            {
                _ = LoadAsync();
            }
        }

        private void OnAuthStateChanged(AuthState state)
        {
            switch (state)
            {
                case AuthState.SignedIn:
                    if (autoLoadOnSignIn)
                    {
                        _ = LoadAsync();
                    }

                    break;

                case AuthState.NotInitialized:
                case AuthState.Error:
                    ClearLocalData();
                    break;
            }
        }

        private void ClearLocalData()
        {
            loadGeneration++;
            Store.ForgetLocalState();
            Data = null;
            LoadedFromCloud = false;
            DataCleared?.Invoke();
        }

        private static bool IsStillSamePlayer(string playerId)
        {
            AuthenticationManager auth = AuthenticationManager.Instance;
            return auth != null && auth.IsSignedIn && auth.PlayerId == playerId;
        }

        private void ReportError(CloudSaveError error)
        {
            Debug.LogError($"[CloudSaveManager] {error.Kind} ({error.Code}): {error.Message}");
            ErrorOccurred?.Invoke(error);
        }
    }
}
