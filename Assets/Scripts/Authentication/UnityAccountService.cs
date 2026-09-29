using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;

namespace CloudGame.Auth
{
    /// <summary>
    /// Envoltura de Unity Player Accounts. El servicio solo se registra si el proyecto tiene
    /// un Client ID válido en Project Settings > Services > Unity Player Accounts, así que
    /// <see cref="PlayerAccountService.Instance"/> puede lanzar o devolver null. Toda la API
    /// se consulta de forma diferida y tolera esa ausencia.
    /// </summary>
    public sealed class UnityAccountService
    {
        /// <summary>Devuelto cuando el proyecto todavía no tiene configurado Unity Player Accounts.</summary>
        public const string NotConfiguredDetail =
            "Abre Project Settings > Services > Unity Player Accounts y pega el Client ID de tu cuenta.";

        public event Action SignedIn;
        public event Action SignedOut;
        public event Action<AuthError> SignInFailed;

        public bool IsConfigured => TryGetService() != null;

        public bool IsSignedIn
        {
            get
            {
                IPlayerAccountService service = TryGetService();
                return service != null && service.IsSignedIn;
            }
        }

        /// <summary>Se puso a true cuando se lanzó el navegador y sigue esperando el deep link.</summary>
        public bool IsBrowserFlowPending { get; private set; }

        public string Email { get; private set; } = "";

        public string Subject { get; private set; } = "";

        /// <summary>
        /// Token de la sesión de Unity. Se canjea en Authentication para obtener un jugador de UGS.
        /// </summary>
        public string AccessToken
        {
            get
            {
                IPlayerAccountService service = TryGetService();
                return service != null ? service.AccessToken ?? string.Empty : string.Empty;
            }
        }

        private bool subscribed;
        private bool failureReportedByEvent;

        /// <summary>
        /// Registra los eventos del SDK. Es idempotente porque UnityServices se puede reinicializar.
        /// </summary>
        public void EnsureSubscribed()
        {
            IPlayerAccountService service = TryGetService();
            if (service == null || subscribed)
            {
                return;
            }

            service.SignedIn += HandleSignedIn;
            service.SignedOut += HandleSignedOut;
            service.SignInFailed += HandleSignInFailed;
            subscribed = true;
        }

        public void ForgetSubscription()
        {
            IPlayerAccountService service = TryGetService();
            if (service != null && subscribed)
            {
                service.SignedIn -= HandleSignedIn;
                service.SignedOut -= HandleSignedOut;
                service.SignInFailed -= HandleSignInFailed;
            }

            subscribed = false;
        }

        /// <summary>
        /// Abre el navegador para iniciar sesión con una cuenta de Unity. El método vuelve en
        /// cuanto el navegador se lanza; el resultado real llega por <see cref="SignedIn"/> o
        /// <see cref="SignInFailed"/> cuando el navegador redirige de vuelta a la aplicación.
        /// </summary>
        /// <param name="signUp">true para mostrar el formulario de registro.</param>
        /// <returns>null si el navegador se lanzó; el error si no fue posible lanzarlo.</returns>
        public async Task<AuthError?> StartSignInAsync(bool signUp)
        {
            IPlayerAccountService service = TryGetService();
            if (service == null)
            {
                return AuthError.ForPlayerAccountsNotConfigured();
            }

            EnsureSubscribed();
            IsBrowserFlowPending = true;
            failureReportedByEvent = false;

            try
            {
                await service.StartSignInAsync(signUp);
                return null;
            }
            catch (RequestFailedException exception)
            {
                // El SDK dispara SignInFailed y relanza la misma excepción, así que el wrapper
                // solo fabrica un error si el evento no llegó a reportarlo.
                if (failureReportedByEvent)
                {
                    return null;
                }

                return AuthError.ForPlayerAccounts(exception);
            }
            catch (Exception exception)
            {
                return AuthError.ForInitialization(exception);
            }
        }

        /// <summary>
        /// Cierra la sesión de la cuenta de Unity. También sirve para cancelar un flujo de
        /// navegador que el jugador ha abandonado, porque el SDK no dispara evento en ese caso.
        /// </summary>
        public void SignOut()
        {
            IPlayerAccountService service = TryGetService();
            IsBrowserFlowPending = false;
            Email = string.Empty;
            Subject = string.Empty;

            if (service == null)
            {
                return;
            }

            if (!service.IsSignedIn && string.IsNullOrEmpty(service.AccessToken))
            {
                return;
            }

            service.SignOut();
        }

        /// <summary>
        /// Renueva el access token si la sesión de Unity Player Accounts está a punto de caducar.
        /// </summary>
        public async Task<AuthError?> RefreshTokenAsync()
        {
            IPlayerAccountService service = TryGetService();
            if (service == null)
            {
                return AuthError.ForPlayerAccountsNotConfigured();
            }

            if (!service.IsSignedIn)
            {
                return null;
            }

            try
            {
                await service.RefreshTokenAsync();
                return null;
            }
            catch (RequestFailedException exception)
            {
                return AuthError.ForPlayerAccounts(exception);
            }
        }

        private void HandleSignedIn()
        {
            IPlayerAccountService service = TryGetService();
            if (service?.IdTokenClaims != null)
            {
                Email = service.IdTokenClaims.Email ?? string.Empty;
                Subject = service.IdTokenClaims.Subject ?? string.Empty;
            }

            SignedIn?.Invoke();
        }

        private void HandleSignedOut()
        {
            IsBrowserFlowPending = false;
            Email = string.Empty;
            Subject = string.Empty;
            SignedOut?.Invoke();
        }

        private void HandleSignInFailed(RequestFailedException exception)
        {
            IsBrowserFlowPending = false;
            failureReportedByEvent = true;
            SignInFailed?.Invoke(AuthError.ForPlayerAccounts(exception));
        }

        private static IPlayerAccountService TryGetService()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                return null;
            }

            try
            {
                // Evita PlayerAccountService.Instance, que lanza si el servicio no está registrado.
                return UnityServices.Instance.GetPlayerAccountService();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
