using System;
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
        Error = 5
    }

    public class AuthenticationManager : MonoBehaviour
    {
        public static AuthenticationManager Instance { get; private set; }

        [Header("Authentication")]
        [SerializeField] private bool autoSignInOnStart = true;
        [SerializeField] private string profile = "";

        public event Action<AuthState> StateChanged;
        public event Action<string> ErrorOccurred;

        public AuthState State { get; private set; } = AuthState.NotInitialized;

        public bool IsSignedIn => ServicesReady && AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => ServicesReady ? AuthenticationService.Instance.PlayerId : "";
        public string PlayerName => ServicesReady ? AuthenticationService.Instance.PlayerName : "";
        public string ActiveProfile => ServicesReady ? AuthenticationService.Instance.Profile : "";
        public bool HasStoredSession => ServicesReady && AuthenticationService.Instance.SessionTokenExists;

        private static bool ServicesReady =>
            UnityServices.State == ServicesInitializationState.Initialized;

        private bool subscribedToEvents;
        private Task currentOperation = Task.CompletedTask;

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

            SetState(AuthState.SigningIn);
            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync(new SignInOptions { CreateAccount = true });
            }
            catch (RequestFailedException exception)
            {
                SetState(AuthState.Error);
                ErrorOccurred?.Invoke($"{exception.ErrorCode}: {exception.Message}");
                Debug.LogError($"[AuthenticationManager] Anonymous sign-in failed (code {exception.ErrorCode}): {exception.Message}");
            }
        }

        public void SignOut()
        {
            if (!ServicesReady || !AuthenticationService.Instance.IsSignedIn)
            {
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
                SetState(AuthState.Error);
                ErrorOccurred?.Invoke(exception.Message);
                Debug.LogError($"[AuthenticationManager] Initialization failed: {exception.Message}");
            }
        }

        private async Task SignInWithCredentialsCoreAsync(string username, string password, bool createAccount)
        {
            if (!CredentialValidator.IsValid(username, password, out string validationError))
            {
                SetState(AuthState.Error);
                ErrorOccurred?.Invoke(validationError);
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
                SetState(AuthState.Error);
                ErrorOccurred?.Invoke($"{exception.ErrorCode}: {exception.Message}");
                Debug.LogError($"[AuthenticationManager] Username/password {(createAccount ? "sign-up" : "sign-in")} failed (code {exception.ErrorCode}): {exception.Message}");
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
        }

        private void OnSignedIn()
        {
            Debug.Log($"[AuthenticationManager] Signed in (ID: {PlayerId}, Name: '{PlayerName}').");
            SetState(AuthState.SignedIn);
        }

        private void OnSignedOut()
        {
            Debug.Log("[AuthenticationManager] Signed out.");
            SetState(AuthState.NotInitialized);
        }

        private void OnSignInFailed(RequestFailedException exception)
        {
            SetState(AuthState.Error);
            ErrorOccurred?.Invoke($"{exception.ErrorCode}: {exception.Message}");
            Debug.LogError($"[AuthenticationManager] Sign-in failed (code {exception.ErrorCode}): {exception.Message}");
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
    }
}