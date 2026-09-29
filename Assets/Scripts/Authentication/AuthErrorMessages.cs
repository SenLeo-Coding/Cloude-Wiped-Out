using System;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;

namespace CloudGame.Auth
{
    public enum AuthErrorKind
    {
        Validation = 0,
        Credentials = 1,
        Network = 2,
        Service = 3,
        Session = 4,
        Configuration = 5,
        Unknown = 6
    }

    /// <summary>
    /// Error de autenticación normalizado. UGS no siempre devuelve un código numérico,
    /// así que también se interpreta el <c>title</c> y el <c>detail</c> que el servicio
    /// incluye en el mensaje de la excepción.
    /// </summary>
    public readonly struct AuthError
    {
        private const int MaxRawMessageLength = 160;

        public AuthError(AuthErrorKind kind, int code, string message, string serviceTitle = null)
        {
            Kind = kind;
            Code = code;
            Message = string.IsNullOrEmpty(message) ? string.Empty : message;
            ServiceTitle = string.IsNullOrEmpty(serviceTitle) ? null : serviceTitle;
        }

        public AuthErrorKind Kind { get; }
        public int Code { get; }

        /// <summary>Mensaje limpio del servicio, sin JSON ni request-id.</summary>
        public string Message { get; }

        /// <summary>Título del servicio, por ejemplo <c>WRONG_USERNAME_PASSWORD</c>.</summary>
        public string ServiceTitle { get; }

        public bool HasCode => Code > 0;

        public static AuthError ForValidation(string message)
        {
            return new AuthError(AuthErrorKind.Validation, 0, message);
        }

        public static AuthError ForRequestFailed(RequestFailedException exception)
        {
            if (exception == null)
            {
                return new AuthError(AuthErrorKind.Unknown, 0, string.Empty);
            }

            string title = ExtractJsonField(exception.Message, "title");
            string detail = ExtractJsonField(exception.Message, "detail");
            string clean = !string.IsNullOrEmpty(detail) ? detail : Sanitize(exception.Message);

            return new AuthError(Classify(exception.ErrorCode, title, clean), exception.ErrorCode, clean, title);
        }

        public static AuthError ForInitialization(Exception exception)
        {
            if (exception is RequestFailedException requestFailed)
            {
                return ForRequestFailed(requestFailed);
            }

            return new AuthError(AuthErrorKind.Service, 0, Sanitize(exception?.Message));
        }

        /// <summary>
        /// El proyecto no tiene Client ID de Unity Player Accounts, así que el servicio ni
        /// siquiera llega a registrarse. Es un problema de configuración del proyecto, no del jugador.
        /// </summary>
        public static AuthError ForPlayerAccountsNotConfigured()
        {
            return new AuthError(
                AuthErrorKind.Configuration,
                PlayerAccountsErrorCodes.MissingClientId,
                "Unity Player Accounts no está configurado en este proyecto.",
                "MISSING_CLIENT_ID");
        }

        /// <summary>El jugador no volvió del navegador de Unity Accounts dentro del tiempo previsto.</summary>
        public static AuthError ForPlayerAccountsTimeout()
        {
            return new AuthError(
                AuthErrorKind.Session,
                PlayerAccountsErrorCodes.InvalidGrant,
                "No se completó el inicio de sesión en el navegador de Unity Accounts.",
                "UNITY_ACCOUNT_TIMEOUT");
        }

        /// <summary>
        /// Normaliza los errores de Unity Player Accounts. Sus códigos van de 10100 a 10199 y
        /// no coinciden con los de Authentication, así que se clasifican aparte.
        /// </summary>
        public static AuthError ForPlayerAccounts(RequestFailedException exception)
        {
            if (exception == null)
            {
                return ForPlayerAccountsNotConfigured();
            }

            int code = exception.ErrorCode;
            string clean = Sanitize(exception.Message);

            if (code == PlayerAccountsErrorCodes.MissingClientId)
            {
                return ForPlayerAccountsNotConfigured();
            }

            if (code == PlayerAccountsErrorCodes.MissingRefreshToken
                || code == PlayerAccountsErrorCodes.InvalidState
                || code == PlayerAccountsErrorCodes.InvalidGrant)
            {
                return new AuthError(AuthErrorKind.Session, code, clean, TitleForCode(code));
            }

            if (code == PlayerAccountsErrorCodes.InvalidClient
                || code == PlayerAccountsErrorCodes.InvalidScope
                || code == PlayerAccountsErrorCodes.InvalidRequest
                || code == PlayerAccountsErrorCodes.UnauthorizedClient
                || code == PlayerAccountsErrorCodes.UnsupportedGrantType
                || code == PlayerAccountsErrorCodes.UnsupportedResponseType)
            {
                return new AuthError(AuthErrorKind.Configuration, code, clean, TitleForCode(code));
            }

            if (IsNetworkCode(code))
            {
                return new AuthError(AuthErrorKind.Network, code, clean, TitleForCode(code));
            }

            if (LooksLikeCredentialProblem(clean))
            {
                return new AuthError(AuthErrorKind.Credentials, code, clean, TitleForCode(code));
            }

            return new AuthError(AuthErrorKind.Unknown, code, clean, TitleForCode(code));
        }

        private static string TitleForCode(int code)
        {
            switch (code)
            {
                case PlayerAccountsErrorCodes.UnknownError: return "UNKNOWN_ERROR";
                case PlayerAccountsErrorCodes.InvalidState: return "INVALID_STATE";
                case PlayerAccountsErrorCodes.MissingClientId: return "MISSING_CLIENT_ID";
                case PlayerAccountsErrorCodes.InvalidClient: return "INVALID_CLIENT";
                case PlayerAccountsErrorCodes.InvalidScope: return "INVALID_SCOPE";
                case PlayerAccountsErrorCodes.InvalidRequest: return "INVALID_REQUEST";
                case PlayerAccountsErrorCodes.InvalidGrant: return "INVALID_GRANT";
                case PlayerAccountsErrorCodes.MissingRefreshToken: return "MISSING_REFRESH_TOKEN";
                case PlayerAccountsErrorCodes.UnauthorizedClient: return "UNAUTHORIZED_CLIENT";
                case PlayerAccountsErrorCodes.UnsupportedGrantType: return "UNSUPPORTED_GRANT_TYPE";
                case PlayerAccountsErrorCodes.UnsupportedResponseType: return "UNSUPPORTED_RESPONSE_TYPE";
                default: return null;
            }
        }

        private static AuthErrorKind Classify(int code, string serviceTitle, string message)
        {
            string title = serviceTitle != null ? serviceTitle.ToUpperInvariant() : string.Empty;

            if (code == CommonErrorCodes.TransportError
                || code == CommonErrorCodes.Timeout
                || code == CommonErrorCodes.ServiceUnavailable
                || code == CommonErrorCodes.TooManyRequests
                || title == "SERVICE_UNAVAILABLE"
                || title == "TOO_MANY_REQUESTS")
            {
                return AuthErrorKind.Network;
            }

            if (code == CommonErrorCodes.InvalidToken
                || code == CommonErrorCodes.TokenExpired
                || code == AuthenticationErrorCodes.ClientNoActiveSession
                || code == AuthenticationErrorCodes.InvalidSessionToken
                || title == "INVALID_SESSION_TOKEN")
            {
                return AuthErrorKind.Session;
            }

            if (title == "WRONG_USERNAME_PASSWORD"
                || title == "ACCOUNT_ALREADY_EXISTS"
                || title == "ACCOUNT_NOT_FOUND"
                || title == "BANNED_USER"
                || title == "ACCOUNT_SUSPENDED"
                || code == AuthenticationErrorCodes.InvalidParameters
                || code == AuthenticationErrorCodes.BannedUser
                || code == AuthenticationErrorCodes.AccountAlreadyLinked
                || code == AuthenticationErrorCodes.AccountLinkLimitExceeded
                || code == AuthenticationErrorCodes.ClientInvalidUserState
                || code == AuthenticationErrorCodes.InvalidProvider
                || code == AuthenticationErrorCodes.ClientInvalidProfile
                || code == AuthenticationErrorCodes.EnvironmentMismatch)
            {
                return AuthErrorKind.Credentials;
            }

            if (code == CommonErrorCodes.Forbidden
                || code == CommonErrorCodes.NotFound
                || code == CommonErrorCodes.InvalidRequest
                || code == CommonErrorCodes.Conflict
                || code == CommonErrorCodes.ProjectPolicyAccessDenied
                || code == CommonErrorCodes.PlayerPolicyAccessDenied)
            {
                return AuthErrorKind.Service;
            }

            return LooksLikeCredentialProblem(title) || LooksLikeCredentialProblem(message)
                ? AuthErrorKind.Credentials
                : AuthErrorKind.Unknown;
        }

        private static bool IsNetworkCode(int code)
        {
            return code == CommonErrorCodes.TransportError
                || code == CommonErrorCodes.Timeout
                || code == CommonErrorCodes.ServiceUnavailable
                || code == CommonErrorCodes.TooManyRequests;
        }

        private static bool LooksLikeCredentialProblem(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string lowered = text.ToLowerInvariant();
            return lowered.Contains("username")
                || lowered.Contains("password")
                || lowered.Contains("credential")
                || lowered.Contains("contrase")
                || lowered.Contains("usuario")
                || lowered.Contains("banned")
                || lowered.Contains("suspended")
                || lowered.Contains("already exists");
        }

        /// <summary>Lee un valor de cadena del JSON plano que UGS mete en Exception.Message.</summary>
        private static string ExtractJsonField(string message, string fieldName)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            string needle = "\"" + fieldName + "\":\"";
            int start = message.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += needle.Length;
            var builder = new System.Text.StringBuilder();
            for (int i = start; i < message.Length; i++)
            {
                char current = message[i];
                if (current == '\\' && i + 1 < message.Length)
                {
                    builder.Append(message[i + 1]);
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    break;
                }

                builder.Append(current);
            }

            string value = builder.ToString().Trim();
            return value.Length > 0 ? value : null;
        }

        /// <summary>Evita que un volcado de JSON o un request-id lleguen al jugador.</summary>
        private static string Sanitize(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string cleaned = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (cleaned.StartsWith("{", StringComparison.Ordinal) || cleaned.StartsWith("[", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return cleaned.Length > MaxRawMessageLength
                ? cleaned.Substring(0, MaxRawMessageLength) + "…"
                : cleaned;
        }
    }

    /// <summary>Convierte los errores de Unity Gaming Services en texto legible en español.</summary>
    public static class AuthErrorMessages
    {
        public static string TitleFor(AuthError error)
        {
            switch (error.Kind)
            {
                case AuthErrorKind.Validation: return "REVISA LOS DATOS";
                case AuthErrorKind.Credentials: return "NO SE PUDO INICIAR SESIÓN";
                case AuthErrorKind.Network: return "SIN CONEXIÓN";
                case AuthErrorKind.Session: return "SESIÓN EXPIRADA";
                case AuthErrorKind.Service: return "SERVICIO NO DISPONIBLE";
                case AuthErrorKind.Configuration: return "CONFIGURACIÓN INCOMPLETA";
                default: return "ALGO SALIÓ MAL";
            }
        }

        public static string BodyFor(AuthError error)
        {
            switch (error.Kind)
            {
                case AuthErrorKind.Validation:
                    return string.IsNullOrEmpty(error.Message)
                        ? "Los datos introducidos no son válidos."
                        : error.Message;

                case AuthErrorKind.Credentials:
                    return CredentialBody(error);

                case AuthErrorKind.Network:
                    return NetworkBody(error);

                case AuthErrorKind.Session:
                    return SessionBody(error);

                case AuthErrorKind.Service:
                    return ServiceBody(error);

                case AuthErrorKind.Configuration:
                    return ConfigurationBody(error);

                default:
                    return UnknownBody(error);
            }
        }

        /// <summary>Referencia técnica en texto pequeño, útil para soporte.</summary>
        public static string DetailFor(AuthError error)
        {
            if (error.HasCode)
            {
                return string.IsNullOrEmpty(error.ServiceTitle)
                    ? $"Código de error: {error.Code}"
                    : $"{error.ServiceTitle} ({error.Code})";
            }

            return error.ServiceTitle;
        }

        private static string CredentialBody(AuthError error)
        {
            string title = error.ServiceTitle != null ? error.ServiceTitle.ToUpperInvariant() : string.Empty;

            if (title == "BANNED_USER" || title == "ACCOUNT_SUSPENDED" || error.Code == AuthenticationErrorCodes.BannedUser)
            {
                return "Esta cuenta está bloqueada. Contacta con soporte si crees que es un error.";
            }

            if (title == "ACCOUNT_ALREADY_EXISTS")
            {
                return "Ese usuario ya está registrado. Prueba a iniciar sesión en lugar de crear la cuenta.";
            }

            if (title == "ACCOUNT_NOT_FOUND" || title == "WRONG_USERNAME_PASSWORD")
            {
                return "El usuario o la contraseña no coinciden. Verifica los datos e inténtalo de nuevo.";
            }

            if (error.Code == AuthenticationErrorCodes.InvalidParameters)
            {
                return "El usuario o la contraseña no coinciden. Verifica los datos e inténtalo de nuevo.";
            }

            return "El usuario o la contraseña son incorrectos. Revisa que estén bien escritos e inténtalo de nuevo.";
        }

        private static string NetworkBody(AuthError error)
        {
            if (error.Code == CommonErrorCodes.TooManyRequests)
            {
                return "Has realizado demasiados intentos. Espera unos segundos antes de volver a intentarlo.";
            }

            if (error.Code == CommonErrorCodes.Timeout)
            {
                return "La solicitud tardó demasiado. Revisa tu conexión e inténtalo de nuevo.";
            }

            return "No se pudo conectar con el servidor. Revisa tu conexión a internet e inténtalo de nuevo.";
        }

        private static string ServiceBody(AuthError error)
        {
            if (error.Code == CommonErrorCodes.ProjectPolicyAccessDenied
                || error.Code == CommonErrorCodes.PlayerPolicyAccessDenied
                || error.Code == AuthenticationErrorCodes.EnvironmentMismatch)
            {
                return "El servicio de autenticación no está habilitado o no coincide con la configuración del proyecto.";
            }

            return "El servicio no pudo completar la operación en este momento. Inténtalo de nuevo en unos segundos.";
        }

        private static string SessionBody(AuthError error)
        {
            if (error.ServiceTitle == "UNITY_ACCOUNT_TIMEOUT")
            {
                return "No se completó el inicio de sesión en el navegador. "
                    + "Vuelve a intentarlo o usa otra forma de entrar.";
            }

            return "Tu sesión ya no es válida. Vuelve a iniciar sesión para continuar.";
        }

        private static string ConfigurationBody(AuthError error)
        {
            if (error.Code == PlayerAccountsErrorCodes.MissingClientId)
            {
                return "Unity Accounts no está configurado en este proyecto. "
                    + "Abre Project Settings > Services > Unity Player Accounts y pega el Client ID.";
            }

            if (error.Code == PlayerAccountsErrorCodes.InvalidClient
                || error.Code == PlayerAccountsErrorCodes.UnauthorizedClient)
            {
                return "El Client ID de Unity Accounts no es válido o no tiene permisos sobre este proyecto. "
                    + "Revísalo en Project Settings > Services > Unity Player Accounts.";
            }

            if (error.Code == PlayerAccountsErrorCodes.InvalidRequest
                || error.Code == PlayerAccountsErrorCodes.InvalidScope
                || error.Code == PlayerAccountsErrorCodes.UnsupportedGrantType
                || error.Code == PlayerAccountsErrorCodes.UnsupportedResponseType)
            {
                return "La configuración de Unity Accounts no admite esta forma de inicio de sesión. "
                    + "Revisa el Client ID y el Redirect URI en Project Settings.";
            }

            return "Unity Accounts no está configurado correctamente en este proyecto.";
        }

        private static string UnknownBody(AuthError error)
        {
            if (string.IsNullOrEmpty(error.Message))
            {
                return "Ocurrió un error inesperado. Inténtalo de nuevo.";
            }

            return $"Ocurrió un error inesperado: {error.Message}";
        }
    }
}
