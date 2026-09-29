using CloudGame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CloudGame.Auth
{
    public class AuthenticationUI : MonoBehaviour
    {
        [SerializeField] private Button signInButton;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Button guestButton;
        [SerializeField] private Button signOutButton;
        [SerializeField] private Button unityAccountButton;
        [SerializeField] private Text unityAccountButtonLabel;
        [SerializeField] private InputField usernameInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Text statusText;
        [SerializeField] private Text playerIdText;
        [SerializeField] private Text sessionText;
        [SerializeField] private string gameSceneName = "GameScene";

        private AuthenticationManager manager;
        private bool loadingGameScene;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            manager = AuthenticationManager.Instance;
            if (manager == null)
            {
                Debug.LogError("[AuthenticationUI] No AuthenticationManager found in the scene.");
                return;
            }

            manager.StateChanged += OnStateChanged;
            manager.ErrorOccurred += OnError;

            if (signInButton != null)
            {
                signInButton.onClick.AddListener(SignInWithCredentials);
            }

            if (signUpButton != null)
            {
                signUpButton.onClick.AddListener(SignUpWithCredentials);
            }

            if (guestButton != null)
            {
                guestButton.onClick.AddListener(SignInAsGuest);
            }

            if (signOutButton != null)
            {
                signOutButton.onClick.AddListener(SignOut);
            }

            if (unityAccountButton != null)
            {
                unityAccountButton.onClick.AddListener(ToggleUnityAccount);
            }

            OnStateChanged(manager.State);
        }

        private void OnDestroy()
        {
            if (manager == null)
            {
                return;
            }

            manager.StateChanged -= OnStateChanged;
            manager.ErrorOccurred -= OnError;
        }

        private void OnStateChanged(AuthState state)
        {
            if (statusText != null)
            {
                statusText.text = $"Estado: {StateLabel(state)}";
            }

            if (playerIdText != null)
            {
                playerIdText.text = manager.IsSignedIn ? $"PlayerId: {manager.PlayerId}" : "PlayerId: -";
            }

            if (sessionText != null)
            {
                string account = manager.HasUnityAccount
                    ? $"Unity: {manager.UnityAccountEmail}"
                    : "Unity: no vinculado";
                sessionText.text = $"Perfil: {manager.ActiveProfile} | Sesión guardada: {(manager.HasStoredSession ? "Sí" : "No")} | {account}";
            }

            bool waiting = manager.WaitingForUnityAccount;
            SetInteractable(!IsBusy(state) || waiting, waiting);
        }

        private void OnError(AuthError error)
        {
            if (statusText != null)
            {
                statusText.text = $"Estado: {AuthErrorMessages.TitleFor(error)}";
            }

            if (error.Kind == AuthErrorKind.Credentials && passwordInput != null)
            {
                passwordInput.text = string.Empty;
            }

            ErrorDialog.Show(
                AuthErrorMessages.TitleFor(error),
                AuthErrorMessages.BodyFor(error),
                AuthErrorMessages.DetailFor(error));
        }

        private async void SignInWithCredentials()
        {
            if (!CanInteract())
            {
                return;
            }

            if (!ReadCredentials(out string username, out string password))
            {
                return;
            }

            await manager.SignInWithUsernamePasswordAsync(username, password);
            EnterGameIfSignedIn();
        }

        private async void SignUpWithCredentials()
        {
            if (!CanInteract())
            {
                return;
            }

            if (!ReadCredentials(out string username, out string password))
            {
                return;
            }

            await manager.SignUpWithUsernamePasswordAsync(username, password);
            EnterGameIfSignedIn();
        }

        private async void SignInAsGuest()
        {
            if (!CanInteract())
            {
                return;
            }

            await manager.InitializeAndSignInAsync();
            EnterGameIfSignedIn();
        }

        private void SignOut()
        {
            if (!CanInteract())
            {
                return;
            }

            manager.SignOut();
        }

        /// <summary>
        /// Mientras Unity Accounts está esperando el retorno del navegador el mismo botón cancela
        /// el flujo, para que el jugador no se quede bloqueado si cierra la pestaña.
        /// </summary>
        private async void ToggleUnityAccount()
        {
            if (!CanInteract())
            {
                return;
            }

            if (manager.WaitingForUnityAccount)
            {
                manager.CancelUnityAccountSignIn();
                return;
            }

            await manager.SignInWithUnityAccountAsync();
            EnterGameIfSignedIn();
        }

        private bool CanInteract()
        {
            return manager != null && !ErrorDialog.IsOpen;
        }

        private bool ReadCredentials(out string username, out string password)
        {
            username = usernameInput != null ? usernameInput.text.Trim() : "";
            password = passwordInput != null ? passwordInput.text : "";

            if (CredentialValidator.IsValid(username, password, out string validationError))
            {
                return true;
            }

            AuthError error = AuthError.ForValidation(validationError);
            if (statusText != null)
            {
                statusText.text = $"Estado: {AuthErrorMessages.TitleFor(error)}";
            }

            ErrorDialog.Show(AuthErrorMessages.TitleFor(error), AuthErrorMessages.BodyFor(error));
            return false;
        }

        /// <summary>
        /// Entra en la partida solo si la sesión ha quedado iniciada. Se comprueba
        /// después de cada intento de acceso en lugar de reaccionar al evento de
        /// estado: si ya había una sesión guardada, el estado no cambia y el
        /// evento no se dispara, pero el jugador sí ha pedido jugar.
        /// </summary>
        private void EnterGameIfSignedIn()
        {
            if (manager != null && manager.IsSignedIn && !loadingGameScene)
            {
                EnterGame();
            }
        }

        /// <summary>Lleva a la pantalla de partida una vez iniciada la sesión.</summary>
        private void EnterGame()
        {
            if (loadingGameScene)
            {
                return;
            }

            loadingGameScene = true;
            SceneManager.LoadScene(gameSceneName);
        }

        private static bool IsBusy(AuthState state)
        {
            return state == AuthState.Initializing
                || state == AuthState.SigningIn
                || state == AuthState.SigningOut;
        }

        private void SetInteractable(bool interactable, bool waitingForUnityAccount)
        {
            SetButtonInteractable(signInButton, interactable);
            SetButtonInteractable(signUpButton, interactable);
            SetButtonInteractable(guestButton, interactable);
            SetButtonInteractable(signOutButton, interactable);

            // Siempre accionable: si el proyecto no tiene Client ID, al pulsarlo se explica
            // cómo configurarlo; si estamos esperando el navegador, sirve para cancelar.
            if (unityAccountButton != null)
            {
                unityAccountButton.interactable = true;
                SetUnityAccountLabel(waitingForUnityAccount);
            }
        }

        private void SetUnityAccountLabel(bool cancelMode)
        {
            if (unityAccountButton == null)
            {
                return;
            }

            Text label = unityAccountButtonLabel != null
                ? unityAccountButtonLabel
                : unityAccountButton.GetComponentInChildren<Text>();

            if (label != null)
            {
                label.text = cancelMode ? "CANCELAR" : "UNITY ACCOUNTS";
            }
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private string StateLabel(AuthState state)
        {
            switch (state)
            {
                case AuthState.Initializing: return "Inicializando servicios Cloud...";
                case AuthState.SigningIn: return "Iniciando sesión...";
                case AuthState.SignedIn: return "Sesión iniciada";
                case AuthState.SigningOut: return "Cerrando sesión...";
                case AuthState.WaitingForUnityAccount: return "Completa el acceso en el navegador...";
                case AuthState.Error: return "Error";
                default: return "Sin sesión";
            }
        }

        private void ResolveReferences()
        {
            signInButton = signInButton != null ? signInButton : FindInScene<Button>("AuthSignInButton");
            signUpButton = signUpButton != null ? signUpButton : FindInScene<Button>("AuthSignUpButton");
            guestButton = guestButton != null ? guestButton : FindInScene<Button>("AuthGuestButton");
            signOutButton = signOutButton != null ? signOutButton : FindInScene<Button>("AuthSignOutButton");
            unityAccountButton = unityAccountButton != null ? unityAccountButton : FindInScene<Button>("AuthUnityAccountButton");
            if (unityAccountButton != null)
            {
                unityAccountButtonLabel = unityAccountButtonLabel != null
                    ? unityAccountButtonLabel
                    : unityAccountButton.GetComponentInChildren<Text>();
            }
            usernameInput = usernameInput != null ? usernameInput : FindInScene<InputField>("AuthUsernameInput");
            passwordInput = passwordInput != null ? passwordInput : FindInScene<InputField>("AuthPasswordInput");
            statusText = statusText != null ? statusText : FindInScene<Text>("AuthStatusText");
            playerIdText = playerIdText != null ? playerIdText : FindInScene<Text>("AuthPlayerIdText");
            sessionText = sessionText != null ? sessionText : FindInScene<Text>("AuthSessionText");
        }

        private static T FindInScene<T>(string objectName) where T : Component
        {
            foreach (T instance in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (instance.gameObject.name == objectName)
                {
                    return instance;
                }
            }

            return null;
        }
    }
}
