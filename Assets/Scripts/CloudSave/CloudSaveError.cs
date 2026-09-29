using System;
using Unity.Services.CloudSave;
using Unity.Services.Core;

namespace CloudGame.Save
{
    /// <summary>Origen del fallo de Cloud Save, para poder reaccionar de forma distinta.</summary>
    public enum CloudSaveErrorKind
    {
        // Unknown es el valor 0 a propósito: CloudSaveError aparece dentro de CloudSaveResult
        // como struct, y un resultado correcto lleva el error por defecto. Si el valor 0 fuera
        // una categoría real, un resultado correcto se leería como si el error fuera real.
        Unknown = 0,
        Network = 1,
        Service = 2,
        Session = 3,
        Conflict = 4,
        Data = 5,
        Configuration = 6
    }

    /// <summary>
    /// Error de Cloud Save normalizado. El SDK lanza <see cref="CloudSaveException"/> con un
    /// <see cref="CloudSaveExceptionReason"/> que se traduce aquí a un mensaje en español.
    /// </summary>
    public readonly struct CloudSaveError
    {
        public CloudSaveError(CloudSaveErrorKind kind, int code, string message, string serviceTitle = null)
        {
            Kind = kind;
            Code = code;
            Message = string.IsNullOrEmpty(message) ? string.Empty : message;
            ServiceTitle = string.IsNullOrEmpty(serviceTitle) ? null : serviceTitle;
        }

        public CloudSaveErrorKind Kind { get; }
        public int Code { get; }
        public string Message { get; }
        public string ServiceTitle { get; }
        public bool HasCode => Code > 0;

        /// <summary>No hay sesión de UGS activa, así que el guardado pertenece a otro jugador.</summary>
        public static CloudSaveError ForNotSignedIn()
        {
            return new CloudSaveError(
                CloudSaveErrorKind.Session,
                0,
                "Cloud Save necesita una sesión de UGS activa.",
                "NOT_SIGNED_IN");
        }

        /// <summary>
        /// El proyecto no tiene el servicio de Cloud Save habilitado, algo que se configura
        /// en el UGS Dashboard y no desde el editor.
        /// </summary>
        public static CloudSaveError ForServiceDisabled()
        {
            return new CloudSaveError(
                CloudSaveErrorKind.Configuration,
                0,
                "Cloud Save no está habilitado para este proyecto en el UGS Dashboard.",
                "SERVICE_DISABLED");
        }

        public static CloudSaveError ForRequestFailed(CloudSaveException exception)
        {
            if (exception == null)
            {
                return new CloudSaveError(CloudSaveErrorKind.Unknown, 0, string.Empty);
            }

            return new CloudSaveError(
                Classify(exception.Reason),
                exception.ErrorCode,
                Sanitize(exception.Message),
                exception.Reason.ToString());
        }

        /// <summary>
        /// Otro dispositivo escribió el mismo documento. Se descarta el cambio remoto y se
        /// reintenta con el nuevo write lock, de modo que el progreso local no se pierde.
        /// </summary>
        /// <summary>
        /// Otro dispositivo escribió el mismo documento y el reintento con el write lock
        /// actualizado también falló, así que no se puede decidir cuál versión gana.
        /// </summary>
        public static CloudSaveError ForConflict(string existingWriteLock)
        {
            return new CloudSaveError(
                CloudSaveErrorKind.Conflict,
                0,
                "El guardado cambió en otro sitio y no se pudo resolver el conflicto.",
                "CONFLICT");
        }

        public static CloudSaveError ForCorruptedData()
        {
            return new CloudSaveError(
                CloudSaveErrorKind.Data,
                0,
                "El guardado guardado en la nube está dañado y no se puede leer.",
                "CORRUPTED_SAVE");
        }

        public static CloudSaveError ForUnexpected(Exception exception)
        {
            return new CloudSaveError(
                CloudSaveErrorKind.Unknown,
                0,
                Sanitize(exception?.Message));
        }

        private static CloudSaveErrorKind Classify(CloudSaveExceptionReason reason)
        {
            switch (reason)
            {
                case CloudSaveExceptionReason.NoInternetConnection:
                case CloudSaveExceptionReason.TooManyRequests:
                case CloudSaveExceptionReason.ServiceUnavailable:
                    return CloudSaveErrorKind.Network;

                case CloudSaveExceptionReason.ProjectIdMissing:
                case CloudSaveExceptionReason.PlayerIdMissing:
                case CloudSaveExceptionReason.AccessTokenMissing:
                case CloudSaveExceptionReason.Unauthorized:
                case CloudSaveExceptionReason.Unknown:
                    return CloudSaveErrorKind.Service;

                case CloudSaveExceptionReason.Conflict:
                    return CloudSaveErrorKind.Conflict;

                case CloudSaveExceptionReason.InvalidArgument:
                case CloudSaveExceptionReason.KeyLimitExceeded:
                case CloudSaveExceptionReason.NotFound:
                    return CloudSaveErrorKind.Data;

                default:
                    return CloudSaveErrorKind.Unknown;
            }
        }

        private static string Sanitize(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string cleaned = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return cleaned.Length > 160 ? cleaned.Substring(0, 160) + "…" : cleaned;
        }
    }

    /// <summary>Texto legible en español para cada <see cref="CloudSaveError"/>.</summary>
    public static class CloudSaveErrorMessages
    {
        public static string TitleFor(CloudSaveError error)
        {
            switch (error.Kind)
            {
                case CloudSaveErrorKind.Network: return "SIN CONEXIÓN";
                case CloudSaveErrorKind.Service: return "SERVICIO NO DISPONIBLE";
                case CloudSaveErrorKind.Session: return "SESIÓN EXPIRADA";
                case CloudSaveErrorKind.Conflict: return "GUARDADO SINCRONIZADO";
                case CloudSaveErrorKind.Data: return "GUARDADO DAÑADO";
                case CloudSaveErrorKind.Configuration: return "CONFIGURACIÓN INCOMPLETA";
                default: return "NO SE PUDO GUARDAR";
            }
        }

        public static string BodyFor(CloudSaveError error)
        {
            switch (error.Kind)
            {
                case CloudSaveErrorKind.Network:
                    return "No se pudo contactar con el servidor. Tu progreso sigue intacto en este dispositivo.";

                case CloudSaveErrorKind.Session:
                    return "Tu sesión ya no es válida. Vuelve a iniciar sesión para sincronizar tu progreso.";

                case CloudSaveErrorKind.Conflict:
                    return "Tu guardado cambió en otro dispositivo, así que se ha sustituido por la versión más reciente.";

                case CloudSaveErrorKind.Data:
                    return "El guardado de la nube no se puede leer. Se ha vuelto a empezar con un perfil limpio.";

                case CloudSaveErrorKind.Configuration:
                    return "Cloud Save no está habilitado para este proyecto en el UGS Dashboard.";

                case CloudSaveErrorKind.Service:
                    return "El servicio de guardado no pudo completar la operación. Inténtalo de nuevo en unos segundos.";

                default:
                    return string.IsNullOrEmpty(error.Message)
                        ? "Ocurrió un error inesperado al guardar. Inténtalo de nuevo."
                        : $"Ocurrió un error inesperado al guardar: {error.Message}";
            }
        }

        public static string DetailFor(CloudSaveError error)
        {
            if (error.HasCode)
            {
                return string.IsNullOrEmpty(error.ServiceTitle)
                    ? $"Código de error: {error.Code}"
                    : $"{error.ServiceTitle} ({error.Code})";
            }

            return error.ServiceTitle;
        }
    }

    /// <summary>
    /// Resultado de una operación de Cloud Save. Evita excepciones en la capa de juego:
    /// el flujo normal es comprobar <see cref="Success"/> y, si no lo es, mostrar
    /// <see cref="Error"/>.
    /// </summary>
    public readonly struct CloudSaveResult<T>
    {
        private CloudSaveResult(bool success, T value, CloudSaveError error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public bool Success { get; }
        public T Value { get; }
        public CloudSaveError Error { get; }

        public static CloudSaveResult<T> Ok(T value) => new CloudSaveResult<T>(true, value, default);

        public static CloudSaveResult<T> Fail(CloudSaveError error) => new CloudSaveResult<T>(false, default, error);
    }
}
