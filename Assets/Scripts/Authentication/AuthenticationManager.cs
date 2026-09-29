using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace CloudGame.Auth
{
    public enum AuthState
    {
        NotInitialized = 0,
        Initializing = 1,
        SigningIn = 2,
        SignedIn = 3,
        SigningOut = 4,
        Error = 5,

        /// <summary>El navegador está abierto esperando el retorno de Unity Player Accounts.</summary>
        WaitingForUnityAccount = 6
    }

    public class AuthenticationManager : MonoBehaviour
    {
        public static AuthenticationManager Instance { get; private set; }

        [Header("Authentication")]
        [SerializeField] private bool autoSignInOnStart = true;
        [SerializeField] private string profile = "";

        [Header("Unity Player Accounts")]
        [SerializeField] private bool enableUnityAccounts = true;
        [SerializeField] private float unityAccountTimeoutSeconds = 300f;

        public event Action<AuthState> StateChanged;
        public event Action<AuthError> ErrorOccurred;

        public AuthState State { get; private set; } = AuthState.NotInitialized;

        public bool IsSignedIn => ServicesReady && AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => ServicesReady ? AuthenticationService.Instance.PlayerId : "";
        public string PlayerName => ServicesReady ? AuthenticationService.Instance.PlayerName : "";
        public string ActiveProfile => ServicesReady ? AuthenticationService.Instance.Profile : "";
        public bool HasStoredSession => ServicesReady && AuthenticationService.Instance.SessionTokenExists;

        /// <summary>Unity Player Accounts está disponible (el proyecto tiene Client ID).</summary>
        public bool UnityAccountsAvailable => enableUnityAccounts && UnityAccounts.IsConfigured;

        /// <summary>Hay una cuenta de Unity vinculada a esta sesión de UGS.</summary>
        public bool HasUnityAccount => UnityAccounts.IsSignedIn;

        public string UnityAccountEmail => UnityAccounts.Email;

        /// <summary>El navegador está abierto esperando que el jugador termine en Unity Accounts.</summary>
        public bool WaitingForUnityAccount => State == AuthState.WaitingForUnityAccount;

        public UnityAccountService UnityAccounts { get; } = new UnityAccountService();

        private static bool ServicesReady =>
            UnityServices.State == ServicesInitializationState.Initialized;

        private bool subscribedToEvents;
        private Task currentOperation = Task.CompletedTask;
        private Coroutine unityAccountTimeout;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            subscribedToEvents = false;
        }

        private void Start()
        {
            if (autoSignInOnStart)
            {
                _ = InitializeAndSignInAsync();
            }
        }

        public Task InitializeAndSignInAsync()
        {
            return RunSerializedAsync(InitializeAndSignInCoreAsync);
        }

        public Task SignInWithUsernamePasswordAsync(string username, string password)
        {
            return RunSerializedAsync(() => SignInWithCredentialsCoreAsync(username, password, createAccount: false));
        }

        public Task SignUpWithUsernamePasswordAsync(string username, string password)
        {
            return RunSerializedAsync(() => SignInWithCredentialsCoreAsync(username, password, createAccount: true));
        }

        public async Task SignInAnonymouslyAsync()
        {
            if (!ServicesReady || AuthenticationService.Instance.IsSignedIn)
            {
                return;
            }

            CancelUnityAccountTimeout();
            SetState(AuthState.SigningIn);
            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync(new SignInOptions { CreateAccount = true });
            }
            catch (RequestFailedException exception)
            {
                ReportError(AuthError.ForRequestFailed(exception));
                Debug.LogError($"[AuthenticationManager] Anonymous sign-in failed (code {exception.ErrorCode}): {exception.Message}");
            }
        }

        /// <summary>
        /// Abre el navegador para iniciar sesión con una cuenta de Unity. Si el proyecto no tiene
        /// un Client ID configurado, devuelve un error de configuración en lugar de fallar en silencio.
        /// </summary>
        /// <param name="signUp">true para abrir directamente el formulario de registro.</param>
        public Task SignInWithUnityAccountAsync(bool signUp = false)
        {
            return RunSerializedAsync(() => SignInWithUnityAccountCoreAsync(signUp));
        }

        /// <summary>
        /// Cancela un flujo de Unity Accounts que el jugador ha abandonado. Si el navegador llegó a
        /// completarse mientras tanto, se mantiene la sesión de UGS ya iniciada.
        /// </summary>
        public void CancelUnityAccountSignIn()
        {
            if (State != AuthState.WaitingForUnityAccount)
            {
                return;
            }

            CancelUnityAccountTimeout();
            UnityAccounts.SignOut();
            SetState(IsSignedIn ? AuthState.SignedIn : AuthState.NotInitialized);
        }

        public void SignOut()
        {
            CancelUnityAccountTimeout();
            UnityAccounts.SignOut();

            if (!ServicesReady || !AuthenticationService.Instance.IsSignedIn)
            {
                SetState(AuthState.NotInitialized);
                return;
            }

            SetState(AuthState.SigningOut);
            AuthenticationService.Instance.SignOut();
        }

        private async Task InitializeAndSignInCoreAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Initializing)
            {
                return;
            }

            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                SubscribeToAuthEvents();
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    SetState(AuthState.SignedIn);
                    return;
                }

                await SignInAnonymouslyAsync();
                return;
            }

            SetState(AuthState.Initializing);
            try
            {
                await InitializeServicesAsync();
                SubscribeToAuthEvents();
                await SignInAnonymouslyAsync();
            }
            catch (Exception exception)
            {
                ReportError(AuthError.ForInitialization(exception));
                Debug.LogError($"[AuthenticationManager] Initialization failed: {exception.Message}");
            }
        }

        private async Task SignInWithCredentialsCoreAsync(string username, string password, bool createAccount)
        {
            if (!CredentialValidator.IsValid(username, password, out string validationError))
            {
                ReportError(AuthError.ForValidation(validationError));
                return;
            }

            SetState(AuthState.SigningIn);
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await InitializeServicesAsync();
                }

                SubscribeToAuthEvents();
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    AuthenticationService.Instance.SignOut();
                }

                if (createAccount)
                {
                    await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                }
                else
                {
                    await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                }
            }
            catch (RequestFailedException exception)
            {
                ReportError(AuthError.ForRequestFailed(exception));
                Debug.LogError($"[AuthenticationManager] Username/password {(createAccount ? "sign-up" : "sign-in")} failed (code {exception.ErrorCode}): {exception.Message}");
            }
        }

        private async Task SignInWithUnityAccountCoreAsync(bool signUp)
        {
            if (!enableUnityAccounts || !UnityAccounts.IsConfigured)
            {
                // Se comprueba antes de tocar la sesión: si falta el Client ID no se debe
                // expulsar al jugador de la sesión anónima que ya tenía.
                ReportError(AuthError.ForPlayerAccountsNotConfigured());
                return;
            }

            SetState(AuthState.SigningIn);
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await InitializeServicesAsync();
                }

                SubscribeToAuthEvents();

                // El canje de token exige no tener sesión activa, y al arrancar el juego se entra
                // como anónimo. La sesión anónima no se conserva: el jugador pidió una cuenta real.
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    AuthenticationService.Instance.SignOut();
                }

                AuthError? launchError = await UnityAccounts.StartSignInAsync(signUp);
                if (launchError.HasValue)
                {
                    ReportError(launchError.Value);
                    return;
                }

                SetState(AuthState.WaitingForUnityAccount);
                StartUnityAccountTimeout();
            }
            catch (RequestFailedException exception)
            {
                ReportError(AuthError.ForRequestFailed(exception));
                Debug.LogError($"[AuthenticationManager] Unity Accounts sign-in failed (code {exception.ErrorCode}): {exception.Message}");
            }
        }

        private async Task ExchangeUnityAccountTokenAsync()
        {
            SetState(AuthState.SigningIn);
            try
            {
                string accessToken = UnityAccounts.AccessToken;
                if (string.IsNullOrEmpty(accessToken))
                {
                    ReportError(AuthError.ForPlayerAccountsNotConfigured());
                    return;
                }

                if (AuthenticationService.Instance.IsSignedIn)
                {
                    AuthenticationService.Instance.SignOut();
                }

                // CreateAccount = true permite que una cuenta de Unity recién creada genere su
                // primer jugador de UGS en el mismo paso.
                await AuthenticationService.Instance.SignInWithUnityAsync(
                    accessToken,
                    new SignInOptions { CreateAccount = true });
            }
            catch (RequestFailedException exception)
            {
                ReportError(AuthError.ForRequestFailed(exception));
                Debug.LogError($"[AuthenticationManager] Unity account token exchange failed (code {exception.ErrorCode}): {exception.Message}");
            }
        }

        private Task RunSerializedAsync(Func<Task> operation)
        {
            Task previous = currentOperation;
            Task next = RunAfterPrevious(previous, operation);
            currentOperation = next;
            return next;
        }

        private static async Task RunAfterPrevious(Task previous, Func<Task> operation)
        {
            try
            {
                await previous;
            }
            catch
            {
                // A failed previous operation must not block the next one.
            }

            await operation();
        }

        private async Task InitializeServicesAsync()
        {
            SetState(AuthState.Initializing);
            InitializationOptions options = new InitializationOptions();
            if (!string.IsNullOrEmpty(profile))
            {
                options.SetProfile(profile);
            }

            await UnityServices.InitializeAsync(options);
        }

        private void SubscribeToAuthEvents()
        {
            if (subscribedToEvents)
            {
                return;
            }

            subscribedToEvents = true;
            AuthenticationService.Instance.SignedIn += OnSignedIn;
            AuthenticationService.Instance.SignedOut += OnSignedOut;
            AuthenticationService.Instance.SignInFailed += OnSignInFailed;

            UnityAccounts.SignedIn += OnUnityAccountSignedIn;
            UnityAccounts.SignedOut += OnUnityAccountSignedOut;
            UnityAccounts.SignInFailed += OnUnityAccountSignInFailed;
            UnityAccounts.EnsureSubscribed();
        }

        private void OnSignedIn()
        {
            CancelUnityAccountTimeout();
            Debug.Log($"[AuthenticationManager] Signed in (ID: {PlayerId}, Name: '{PlayerName}').");
            SetState(AuthState.SignedIn);
        }

        private void OnSignedOut()
        {
            CancelUnityAccountTimeout();
            Debug.Log("[AuthenticationManager] Signed out.");
            SetState(AuthState.NotInitialized);
        }

        private void OnSignInFailed(RequestFailedException exception)
        {
            CancelUnityAccountTimeout();
            ReportError(AuthError.ForRequestFailed(exception));
            Debug.LogError($"[AuthenticationManager] Sign-in failed (code {exception.ErrorCode}): {exception.Message}");
        }

        private void OnUnityAccountSignedIn()
        {
            Debug.Log($"[AuthenticationManager] Unity account signed in ({UnityAccounts.Email}).");
            CancelUnityAccountTimeout();
            _ = ExchangeUnityAccountTokenAsync();
        }

        private void OnUnityAccountSignedOut()
        {
            Debug.Log("[AuthenticationManager] Unity account signed out.");
            if (State == AuthState.WaitingForUnityAccount)
            {
                SetState(AuthState.NotInitialized);
            }
        }

        private void OnUnityAccountSignInFailed(AuthError error)
        {
            CancelUnityAccountTimeout();
            ReportError(error);
            Debug.LogError($"[AuthenticationManager] Unity account sign-in failed (code {error.Code}): {error.Message}");
        }

        private void StartUnityAccountTimeout()
        {
            CancelUnityAccountTimeout();
            if (unityAccountTimeoutSeconds > 0f)
            {
                unityAccountTimeout = StartCoroutine(UnityAccountTimeoutRoutine());
            }
        }

        private void CancelUnityAccountTimeout()
        {
            if (unityAccountTimeout == null)
            {
                return;
            }

            StopCoroutine(unityAccountTimeout);
            unityAccountTimeout = null;
        }

        private IEnumerator UnityAccountTimeoutRoutine()
        {
            yield return new WaitForSecondsRealtime(unityAccountTimeoutSeconds);

            unityAccountTimeout = null;
            if (State != AuthState.WaitingForUnityAccount)
            {
                yield break;
            }

            UnityAccounts.SignOut();
            SetState(IsSignedIn ? AuthState.SignedIn : AuthState.NotInitialized);
            ErrorOccurred?.Invoke(AuthError.ForPlayerAccountsTimeout());
        }

        private void SetState(AuthState newState)
        {
            if (State == newState)
            {
                return;
            }

            State = newState;
            StateChanged?.Invoke(State);
        }

        /// <summary>
        /// Notifica un error sin perder la sesión activa: si el jugador sigue dentro, el estado
        /// vuelve a <see cref="AuthState.SignedIn"/> para no bloquear a quien dependa de él.
        /// El aviso llega igualmente por <see cref="ErrorOccurred"/>.
        /// </summary>
        private void ReportError(AuthError error)
        {
            SetState(IsSignedIn ? AuthState.SignedIn : AuthState.Error);
            ErrorOccurred?.Invoke(error);
        }
    }
}
