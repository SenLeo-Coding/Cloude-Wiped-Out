using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.Core;
using UnityEngine;

// El SDK declara SaveOptions y DeleteOptions tanto en el espacio de nombres raíz como en
// Models.Data.Player, así que se usan alias explícitos para no dejar la referencia ambigua.
using PlayerDeleteOptions = Unity.Services.CloudSave.Models.Data.Player.DeleteOptions;
using PlayerLoadOptions = Unity.Services.CloudSave.Models.Data.Player.LoadOptions;
using PlayerSaveOptions = Unity.Services.CloudSave.Models.Data.Player.SaveOptions;

namespace CloudGame.Save
{
    /// <summary>
    /// Envoltura de Cloud Save para el jugador firmado en. Guarda el documento completo en una
    /// sola clave y usa el write lock del servicio para detectar si otro dispositivo escribió
    /// entre una lectura y una escritura.
    ///
    /// El servicio exige una sesión de UGS activa: Cloud Save siempre se aplica al jugador
    /// que hay conectado, así que esta clase no acepta un player id y delega el acceso en
    /// <c>AccessClass.Default</c>.
    /// </summary>
    public sealed class CloudSaveStore
    {
        /// <summary>Clave del documento dentro del Cloud Save del jugador.</summary>
        public const string SaveKey = "wipedout_save";

        /// <summary>
        /// El write lock solo puede fallar una vez por escritura: en cuanto se acepta el lock
        /// remoto se reintenta una vez con él. Reintentar sin límite convertiría un conflicto
        /// puntual en un bucle que solo se detiene con un error.
        /// </summary>
        private const int MaxConflictRetries = 1;

        private string writeLock;
        private bool hasWriteLock;

        /// <summary>Write lock del último documento leído o escrito, vacío si aún no hay ninguno.</summary>
        public string WriteLock => writeLock ?? string.Empty;

        /// <summary>
        /// El write lock conocido se envía al guardar. Un write lock desactualizado provoca un
        /// conflicto en el servidor, que es justo lo que permite no pisar progreso ajeno.
        /// </summary>
        public bool HasWriteLock => hasWriteLock;

        /// <summary>Cloud Save solo está disponible con Unity Services y una sesión activos.</summary>
        public bool IsAvailable =>
            UnityServices.State == ServicesInitializationState.Initialized
            && AuthenticationService.Instance.IsSignedIn;

        /// <summary>
        /// Olvida el write lock local. Se llama al cerrar sesión para que el siguiente jugador
        /// no intente escribir con el lock del anterior.
        /// </summary>
        public void ForgetLocalState()
        {
            writeLock = null;
            hasWriteLock = false;
        }

        /// <summary>
        /// Lee el documento guardado. <see cref="CloudSaveResult{T}.Value"/> es null cuando el
        /// jugador todavía no ha guardado nada, no cuando la lectura falla: eso se distingue
        /// mirando <see cref="CloudSaveResult{T}.Success"/>.
        /// </summary>
        public async Task<CloudSaveResult<PlayerSaveData>> LoadAsync()
        {
            if (!IsAvailable)
            {
                return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForNotSignedIn());
            }

            try
            {
                var items = await CloudSaveService.Instance.Data.Player.LoadAsync(
                    new HashSet<string> { SaveKey },
                    new PlayerLoadOptions());

                if (items == null || !items.TryGetValue(SaveKey, out Item item) || item?.Value == null)
                {
                    // Un jugador nuevo: no hay lock que enviar, y el primer guardado crea la clave.
                    ForgetLocalState();
                    return CloudSaveResult<PlayerSaveData>.Ok(null);
                }

                SetWriteLock(item.WriteLock);

                var data = Deserialize(item.Value.GetAsString());
                if (data == null)
                {
                    ForgetLocalState();
                    return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForCorruptedData());
                }

                return CloudSaveResult<PlayerSaveData>.Ok(data);
            }
            catch (CloudSaveException exception)
            {
                return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForRequestFailed(exception));
            }
            catch (Exception exception)
            {
                return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForUnexpected(exception));
            }
        }

        /// <summary>
        /// Sube el documento. Si el write lock local no coincide con el del servidor, se adopta
        /// el remoto y se reintenta una vez: en un juego de un solo jugador por cuenta, perder la
        /// última partida frente a un dispositivo ya sincronizado no compensa.
        /// </summary>
        public async Task<CloudSaveResult<PlayerSaveData>> SaveAsync(PlayerSaveData data)
        {
            if (!IsAvailable)
            {
                return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForNotSignedIn());
            }

            if (data == null)
            {
                return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForUnexpected(
                    new ArgumentNullException(nameof(data))));
            }

            data.EnsureValid();
            string json = JsonUtility.ToJson(data);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var payload = new Dictionary<string, SaveItem>
                    {
                        { SaveKey, new SaveItem(json, hasWriteLock ? writeLock : null) }
                    };

                    var locks = await CloudSaveService.Instance.Data.Player.SaveAsync(payload, new PlayerSaveOptions());
                    SetWriteLock(ExtractWriteLock(locks));
                    return CloudSaveResult<PlayerSaveData>.Ok(data);
                }
                catch (CloudSaveConflictException exception)
                {
                    string existingWriteLock = FindExistingWriteLock(exception, SaveKey);
                    if (attempt >= MaxConflictRetries || string.IsNullOrEmpty(existingWriteLock))
                    {
                        return CloudSaveResult<PlayerSaveData>.Fail(
                            CloudSaveError.ForConflict(existingWriteLock));
                    }

                    Debug.LogWarning(
                        $"[CloudSaveStore] '{SaveKey}' cambió en otro dispositivo; "
                        + "se reintenta con el write lock remoto.");
                    writeLock = existingWriteLock;
                    hasWriteLock = true;
                }
                catch (CloudSaveException exception)
                {
                    return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForRequestFailed(exception));
                }
                catch (Exception exception)
                {
                    return CloudSaveResult<PlayerSaveData>.Fail(CloudSaveError.ForUnexpected(exception));
                }
            }
        }

        /// <summary>
        /// Borra el documento del jugador, usado por el reset de progreso. Si el jugador nunca
        /// guardó nada no hay nada que borrar y se devuelve éxito igualmente.
        /// </summary>
        public async Task<CloudSaveResult<bool>> DeleteAsync()
        {
            if (!IsAvailable)
            {
                return CloudSaveResult<bool>.Fail(CloudSaveError.ForNotSignedIn());
            }

            var options = new PlayerDeleteOptions
            {
                WriteLock = hasWriteLock ? writeLock : null
            };

            try
            {
                await CloudSaveService.Instance.Data.Player.DeleteAsync(SaveKey, options);
                ForgetLocalState();
                return CloudSaveResult<bool>.Ok(true);
            }
            catch (CloudSaveConflictException exception)
            {
                // El borrado también valida el lock. Se reintenta una vez con el remoto.
                string existingWriteLock = FindExistingWriteLock(exception, SaveKey);
                if (string.IsNullOrEmpty(existingWriteLock))
                {
                    return CloudSaveResult<bool>.Fail(CloudSaveError.ForConflict(existingWriteLock));
                }

                options.WriteLock = existingWriteLock;

                try
                {
                    await CloudSaveService.Instance.Data.Player.DeleteAsync(SaveKey, options);
                    ForgetLocalState();
                    return CloudSaveResult<bool>.Ok(true);
                }
                catch (CloudSaveException retryException)
                {
                    return CloudSaveResult<bool>.Fail(CloudSaveError.ForRequestFailed(retryException));
                }
            }
            catch (CloudSaveException exception)
            {
                return CloudSaveResult<bool>.Fail(CloudSaveError.ForRequestFailed(exception));
            }
            catch (Exception exception)
            {
                return CloudSaveResult<bool>.Fail(CloudSaveError.ForUnexpected(exception));
            }
        }

        private void SetWriteLock(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writeLock = null;
                hasWriteLock = false;
                return;
            }

            writeLock = value;
            hasWriteLock = true;
        }

        private static string ExtractWriteLock(IDictionary<string, string> locks)
        {
            return locks != null && locks.TryGetValue(SaveKey, out string value) ? value : null;
        }

        private static string FindExistingWriteLock(CloudSaveConflictException exception, string key)
        {
            if (exception?.Details == null)
            {
                return null;
            }

            foreach (CloudSaveConflictErrorDetail detail in exception.Details)
            {
                if (detail != null && detail.Key == key && !string.IsNullOrEmpty(detail.ExistingWriteLock))
                {
                    return detail.ExistingWriteLock;
                }
            }

            return null;
        }

        /// <summary>
        /// Convierte el JSON almacenado en el documento. Se usa <c>JsonUtility</c> en lugar del
        /// serializador del SDK para que el formato guardado dependa solo de este proyecto.
        /// </summary>
        private static PlayerSaveData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            PlayerSaveData data;
            try
            {
                data = JsonUtility.FromJson<PlayerSaveData>(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"[CloudSaveStore] No se pudo leer el guardado: {exception.Message}");
                return null;
            }

            if (data == null)
            {
                return null;
            }

            data.Migrate();
            data.EnsureValid();
            return data;
        }
    }
}
