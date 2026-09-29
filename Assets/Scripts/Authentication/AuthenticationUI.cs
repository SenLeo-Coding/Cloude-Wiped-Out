using UnityEngine;
using UnityEngine.UI;

namespace CloudGame.Auth
{
    public class AuthenticationUI : MonoBehaviour
    {
        [SerializeField] private Button signInButton;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Button guestButton;
        [SerializeField] private Button signOutButton;
        [SerializeField] private InputField usernameInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Text statusText;
        [SerializeField] private Text playerIdText;
        [SerializeField] private Text sessionText;

        private AuthenticationManager manager;

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
                guestButton.onClick.AddListener(() => _ = manager.InitializeAndSignInAsync());
            }

            if (signOutButton != null)
            {
                signOutButton.onClick.AddListener(manager.SignOut);
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
                sessionText.text = $"Perfil: {manager.ActiveProfile} | Sesión guardada: {(manager.HasStoredSession ? "Sí" : "No")}";
            }
        }

        private void OnError(string message)
        {
            if (statusText != null)
            {
                statusText.text = $"Estado: error ({message})";
            }
        }

        private void SignInWithCredentials()
        {
            if (!ReadCredentials(out string username, out string password))
            {
                return;
            }

            _ = manager.SignInWithUsernamePasswordAsync(username, password);
        }

        private void SignUpWithCredentials()
        {
            if (!ReadCredentials(out string username, out string password))
            {
                return;
            }

            _ = manager.SignUpWithUsernamePasswordAsync(username, password);
        }

        private bool ReadCredentials(out string username, out string password)
        {
            username = usernameInput != null ? usernameInput.text.Trim() : "";
            password = passwordInput != null ? passwordInput.text : "";

            if (!CredentialValidator.IsValid(username, password, out string validationError))
            {
                if (statusText != null)
                {
                    statusText.text = $"Estado: {validationError}";
                }

                return false;
            }

            return true;
        }

        private string StateLabel(AuthState state)
        {
            switch (state)
            {
                case AuthState.Initializing: return "Inicializando servicios Cloud...";
                case AuthState.SigningIn: return "Iniciando sesión...";
                case AuthState.SignedIn: return "Sesión iniciada";
                case AuthState.SigningOut: return "Cerrando sesión...";
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